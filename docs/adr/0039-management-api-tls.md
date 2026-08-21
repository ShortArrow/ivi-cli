# 0039. Management API TLS / mTLS

- Status: Accepted
- Date: 2026-05-28

## Context

[ADR 0034 §5](0034-management-api.md) and
[ADR 0036 §6](0036-management-api-authentication.md) both pinned the
v1 stance — "plaintext HTTP, loopback default, PAT auth for the few
operators who reach for non-loopback" — as the cost of shipping
the Management API without holding for a TLS design. Lab networks
that need off-host access have so far relied on a reverse proxy
(nginx, Caddy, Traefik) terminating TLS in front of `ivicli api
start`. That works but adds an operational hop the project's own
docs cannot recommend without a story.

ADR 0036 (Batch K) closed the auth gap; this ADR closes the
transport-confidentiality gap.

## Decision

### 1. TLS is opt-in

`[api.tls] enabled = false` (the default) means the listener
serves plaintext HTTP exactly as it has since ADR 0034. Every
existing operator's setup keeps working unchanged. TLS turns on
when:

- `[api.tls] enabled = true` in `config.toml`, **or**
- `ivicli api start --tls ...` is supplied on the CLI (CLI flags
  override the config file for that single run).

### 2. Default port: 8443 when TLS is on

`--port` becomes nullable. Default falls back to `8080` for HTTP
and `8443` for HTTPS — mirrors web idioms so operators don't have
to think about port numbers when toggling.

### 3. Certificate sources

```
exactly one of:
  --tls-cert <path>          PFX (single file) or PEM (with --tls-key)
  --tls-self-signed          ephemeral cert generated at startup
```

- **PFX**: `X509CertificateLoader.LoadPkcs12FromFile(path, password)`.
  Password comes from an env var named by `--tls-password-env` so it
  never enters argv / process listings.
- **PEM**: `X509Certificate2.CreateFromPemFile(certPath, keyPath)`,
  then round-tripped through `Export(Pfx)` + reimport so Kestrel can
  use the private key cross-platform.
- **Self-signed**: `CertificateRequest` with SHA-256 RSA-2048, SAN
  covering `localhost` + `127.0.0.1` + `::1`, **24-hour validity** so
  abuse stays visible. Startup logs a Warning so an operator
  doesn't accidentally ship a self-signed cert to production.

### 4. mTLS

`--tls-client-required` + `--tls-client-ca <pem-bundle>` enforce
client cert validation. The bundle is a PEM file with one or more
`-----BEGIN CERTIFICATE-----` blocks; Kestrel's
`HttpsConnectionAdapterOptions.ClientCertificateValidation` rebinds
the chain to that bundle via
`X509ChainPolicy.CustomTrustStore`, so operators pin a private CA
without modifying the machine trust store.

This is layered with PAT auth (ADR 0036): a connection that
presents a valid client cert still has to present a valid
`Authorization: Bearer <token>` (HTTP) or
`Sec-WebSocket-Protocol: ivi-cli-pat.<token>` (WebSocket) when
tokens are configured. mTLS gates *who* can speak to the listener;
PAT gates *what* they can do.

### 5. Decorator order with PAT auth

```
TCP/TLS ──► Kestrel HTTPS handshake (mTLS optional)
        ──► ApiTokenAuthentication middleware (ADR 0036)
        ──► Routing → handler
```

No changes to the PAT middleware: it inspects HTTP headers and
WebSocket sub-protocols, which are visible whether the underlying
transport is plain HTTP or TLS-wrapped. Bypass paths (`/healthz`,
`/openapi/v1.json`) still skip the token check; mTLS still gates
them (the TLS handshake happens before any HTTP routing).

### 6. WebSocket: `wss://` on the same port

When Kestrel listens with HTTPS, WebSocket upgrades arrive over
`wss://` automatically. No additional config; existing WebSocket
endpoints (`/v1/devices/{name}/ws`) work the same way clients just
use `wss://` instead of `ws://`.

### 7. Loopback gate (ADR 0036 §4) is unchanged

Non-loopback binding still requires at least one configured PAT
**or** `--allow-anonymous` — independent of TLS. The point of the
non-loopback gate is "don't expose write operations to a network
without proving the operator wanted to." TLS encrypts but does
not authorize; the two concerns are orthogonal.

### 8. Certificate hot-reload

Long-running listeners pick up rotated certificate files without a
restart (issue #16). `RotatingTlsCertificate` polls the file
timestamps under `[api.tls]` every 5 seconds — polling rather than a
`FileSystemWatcher`, because ACME clients replace files by rename,
which watchers miss on some mounts, and a 5 s stat of up to three
paths costs nothing. Kestrel reads the current bundle through
`ServerCertificateSelector` on every TLS handshake, so a swap applies
from the next connection.

A rotation is rejected — the old material stays active and a warning
is logged — when the new files fail to load or the new certificate is
already expired. A failed load leaves the timestamps unrecorded, so
the next tick retries until it heals: a half-written cert+key pair
recovers once the writer finishes, and a scanner briefly holding the
fresh file (observed on the v0.3.0 win-arm64 release runner) costs one
tick, not the rotation. An expired certificate records the timestamps
instead — waiting cannot un-expire it, so one warning per file change
is enough. Chain validation is deliberately not part of the gate:
internal-CA
and self-signed deployments would never pass it. A successful reload
appends a `server.lifecycle` audit event with action `cert-reloaded`
(ADR 0043). The in-memory `--tls-self-signed` certificate has nothing
on disk to watch and never rotates.

### 9. Errors

Validation surfaces three `TlsConfigError` variants:

- `TlsCertSourceAmbiguous` — `enabled` is true but neither (or
  both) of `cert_path` / `self_signed` is set.
- `TlsDisabledButOptionsSet` — `enabled` is false but
  cert/key/ca options are set (catches "the operator meant to
  enable TLS but forgot the flag").
- `TlsClientCaMissing` — `client_required` is true but
  `client_ca_path` is empty.

Runtime cert load surfaces three `TlsLoadError` variants:

- `TlsLoadDisabled` — programmer error: the loader was called on a
  disabled config.
- `TlsCertFileMissing` — file at the supplied path does not exist.
- `TlsCertLoadFailure` — file existed but did not parse (bad
  password, malformed PEM, etc.).

Startup failures print one error line and exit with the
`DeviceError` exit code; the listener does not start.

## Consequences

- **Production-deploy story.** Operators can run
  `ivicli api start` directly on a lab-network listener with TLS
  and (optionally) mTLS, no reverse proxy required.
- **Opt-in.** Every existing operator's setup keeps working.
  Config-file change or `--tls*` flags toggle the new behaviour.
- **Self-signed dev path.** New operators on `localhost` can
  enable HTTPS in one flag (`--tls-self-signed`) without standing
  up a CA. Production operators see the Warning log and reach for
  the cert-file paths.
- **mTLS for AI agents.** Tool frameworks that wrap the Management
  API can pin a private CA + provision per-agent client certs,
  matching the ADR 0036 PAT story with a stronger transport gate.

## Out of scope (v1)

- ~~**Cert hot-reload.**~~ Shipped (§8): rotated cert files are
  served from the next handshake, no restart.
- **ACME / Let's Encrypt.** `--tls-self-signed` covers dev,
  cert files cover prod. ACME automation is one more dependency
  (Certes / `LettuceEncrypt`) and outside the v1 audience.
- **OS certificate store.** No `--tls-cert-store windows:CurrentUser`
  knob; cert files are universal across Windows / Linux / macOS.
  v2 if operators ask.
- **Per-route TLS policy** (some endpoints HTTPS-only, others
  HTTP). All endpoints share the listener; the operator chooses
  HTTP-only or HTTPS-only for the process.
- **HTTP-to-HTTPS redirect.** Single listener; no plaintext
  fallback port. Operators who need both run two processes.
- **OCSP stapling**, **session ticket key rotation** — Kestrel
  defaults are accepted; rotating them is a Kestrel-config story,
  not an ivi-cli config story.

## Verification

- `ivicli api start` (no flags) → plaintext HTTP on 8080 (unchanged).
- `ivicli api start --tls --tls-self-signed` → HTTPS on 8443 with
  a dev cert and a startup Warning.
- `ivicli api start --tls --tls-cert /etc/ivi/cert.pfx
  --tls-password-env IVI_TLS_PASS` → HTTPS on 8443 with the
  operator's cert.
- `ivicli api start --tls --tls-cert ... --tls-client-required
  --tls-client-ca clients.pem` → HTTPS + mTLS on 8443.
- `dotnet test --filter "Category!=Integration"` covers cert
  resolution + a live Kestrel HTTPS handshake + mTLS accept/reject
  paths.
