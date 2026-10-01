# 0052. APT repository for Debian and Ubuntu

- Status: Draft
- Date: 2026-09-29

## Context

A Linux user can install ivi-cli today by unpacking a release archive
by hand, through mise (which reads the same archives), or as a .NET tool
if a .NET SDK is present. None of these is updated by the system's own
package manager, so a Debian or Ubuntu machine keeps whatever version was
unpacked until someone repeats the steps.

APT reads a repository, not a list of files. `apt update` fetches
`dists/<suite>/InRelease`, checks its signature against the key the
source line names, and follows the hashes in it to the `Packages`
index. Each entry in `Packages` names its `.deb` by a `Filename:` path
relative to the repository's base URL, and APT checks the file's size
and SHA-256 against that entry after downloading it.

That rules out GitHub Releases as the repository itself. Release assets
sit in one flat directory per tag with no index, and a `Filename:` cannot
name another host or another tag's directory. The `releases/latest/download/`
URL resolves to the release marked Latest, so a repository based there
could only ever offer that one version.

Size also matters. The self-contained linux-x64 archive of v0.4.0 is
47.2 MB and the linux-arm64 one 45.0 MB, so every release adds about
90 MB of packages before the `.deb` compression is counted. GitHub Pages
limits a site to 1 GB, which about eleven releases would exceed, and
Cloudflare Pages refuses any single file over 25 MiB.

## Decision

### 1. Channels

| Channel | For | Install |
| --- | --- | --- |
| NuGet | .NET users | `dotnet tool install -g ivi-cli` |
| GitHub Releases | downloading a file by hand, and mise | archives, and from this ADR `.deb` files |
| APT repository | Debian and Ubuntu | `apt install ivicli` |

The container image (ADR 0018) keeps its own role as a mock instrument.
`packaging/aur` holds AUR build files, but nothing is published to the
AUR yet.

### 2. Where each part lives

The repository is served at `https://pkg.shortarrow.jp/apt`. The
`shortarrow.jp` zone is already on Cloudflare.

- The signed metadata, `dists/` and the keyring, is a Cloudflare
  Pages site. It is small and changes on every release and every
  re-signing (§5).
- The `.deb` files are GitHub Release assets and nothing else. The
  repository's `pool/<tag>/<file>` path answers with a 302 to
  `https://github.com/ShortArrow/ivi-cli/releases/download/<tag>/<file>`,
  set by one rule in the site's `_redirects`.

A package therefore has one copy, in the release that built it, and
every version stays installable without a size limit. The user's source
line names only `pkg.shortarrow.jp`, so the files behind it can move
later without asking anyone to edit it.

Trust rests on the signature alone. A file changed behind the redirect
fails APT's size or hash check against the signed `Packages`, and the
redirect target needs no trust of its own.

### 3. Layout

The standard layout, with one suite:

```
apt/
├── ivicli-archive-keyring.gpg
├── dists/stable/
│   ├── InRelease
│   ├── Release
│   └── main/binary-{amd64,arm64}/Packages(.gz)
└── pool/<tag>/ivicli_<version>_<arch>.deb   (redirected)
```

A flat repository (`Packages` and `Release` beside the files, source
line ending in `./`) would serve one package too. The standard layout
was chosen because the tools that generate repositories produce it at no
extra cost, and because it leaves room to add a component or an
architecture without changing the line every user has already written.

### 4. The package

- Named `ivicli`, the command's name. The NuGet package keeps `ivi-cli`,
  the name that ecosystem already knows.
- Built from the self-contained binary, for amd64 and arm64. A package
  of the framework-dependent build would be about 3 MB, but Debian ships
  no .NET runtime, and Ubuntu's `dotnet-runtime-10.0` would need a
  separate package with its own dependency line. One self-contained
  package serves both.
- Installed as `/usr/lib/ivicli/ivicli` with a `/usr/bin/ivicli` symlink.
  `Depends` lists the native libraries the self-contained runtime loads:
  libc6, libgcc-s1, libstdc++6, zlib1g, ca-certificates, and OpenSSL as
  `libssl3 | libssl3t64`, the name Debian 12 and Ubuntu 24.04 give it
  respectively. ICU is not needed, since the CLI is built with
  invariant globalization.
- It also installs the repository's keyring as
  `/usr/share/keyrings/ivicli-archive-keyring.gpg` (§5).
- The Debian version is the release version with revision `1`
  (`0.4.0-1`). Packaging changes ship with the next release.
- Pre-releases never enter the repository. A version containing `-`
  would sort after the final release in Debian's ordering
  (`0.4.0-beta.1` > `0.4.0`), so the publishing job refuses one rather
  than relying on the release channel alone. A maintenance release on an
  older line (ADR 0022) does enter it: APT picks the highest version,
  and `0.3.3` sorts below `0.4.0`.
- A `dotnet tool` install of the same command lives in
  `~/.dotnet/tools`, and whichever directory comes first on `PATH`
  wins. The install guide says how to tell which one runs.

### 5. Signing keys and freshness

- The repository has its own Ed25519 keys, not the maintainer's personal
  key, so the key that signs every update can be replaced without
  touching anything else. There are two: a signing key, which expires
  after three years, and a standby key, kept offline and never used
  until it is needed.
- The `ivicli` package carries the keyring with every current key,
  standby included. The install guide's first step downloads the same
  keyring to the same path, and the source line names it:

  ```
  deb [signed-by=/usr/share/keyrings/ivicli-archive-keyring.gpg] https://pkg.shortarrow.jp/apt stable main
  ```

  Installing the package then takes the file over, and every upgrade
  replaces it. Keys change on users' machines through `apt upgrade`,
  never by hand after the first step.
- Rotation: a release whose keyring adds the next signing key ships at
  least a year before the current one expires, and `InRelease` is signed
  by both until it does. A machine that never upgrades in that year
  stops verifying when the old key expires.
- Compromise: the standby key becomes the signing key at once, and the
  next keyring drops the compromised one. Clients already trust the
  standby key, so the replacement reaches them through an ordinary
  upgrade.
- The keys and the Cloudflare API token are secrets of a GitHub Actions
  environment, `apt-publish`, which accepts `v*` tags and the `main`
  branch. From `main`, only the re-signing and withdrawal workflows
  below name that environment.
- `Release` carries `Date` and `Valid-Until`, 90 days after signing. A
  scheduled workflow re-signs the metadata weekly. Without an expiry, an
  attacker or a stale mirror could keep serving an old, validly signed
  index and hide a fix; with it, a client refuses metadata that has not
  been re-signed for 90 days. If re-signing stops, users see an expired
  repository rather than a silently stale one. A client whose clock is
  far off sees the repository as expired or not yet valid, as for any
  APT source.

### 6. Publishing

The `.deb` files are built from the `publish` job's self-contained
artifacts before the GitHub Release is created, so they are attached to
it and listed in its `SHA256SUMS` with every other asset. A separate
`apt` job then updates the repository:

1. Skip a pre-release, and fail on a version containing `-`.
2. Fetch the published `InRelease` and `Packages` and verify them
   against the keyring in the repository source tree. Any failure stops
   the job; it never signs what it could not verify. Creating the
   repository for the first time is an explicit input, not a fallback.
3. Replace every entry for this upstream version with entries computed
   from the `.deb` files this run built, for both architectures. The
   index is keyed by version: re-running a release, or re-creating its
   tag, rewrites that version's entries instead of leaving hashes of
   assets that no longer exist.
4. Write `Release` with `Date` and `Valid-Until`, sign `InRelease`, and
   deploy the whole site in one Cloudflare Pages deployment.

A failed `apt` job is retried by re-running that job; the GitHub Release
it depends on is already in place. Two workflows on `main` touch the
repository besides it: the weekly re-signing, and a manual withdrawal
that removes one version's entries and re-signs. Machines that
installed a withdrawn version keep it, and `apt` offers the highest
remaining version. All three share one concurrency group, so no two of
them read and deploy the index at the same time.

The maintainer creates the Cloudflare Pages project and the
`pkg.shortarrow.jp` record once, by hand, before the first publish.

## Consequences

- `apt update` and `apt upgrade` deliver new versions, and new keys, to
  Debian and Ubuntu machines.
- The release workflow depends on Cloudflare and on the repository
  keys. If either is unavailable, the GitHub Release still ships, and
  the repository lags until the `apt` job is re-run.
- The repository must be re-signed at least every 90 days, by the
  scheduled workflow, and a new signing key must ship a year before the
  current one expires.
- The standby key must be stored offline and separately from the
  signing key, or it protects nothing.
- Every install depends on both Cloudflare and GitHub being reachable.
- An RPM repository, if wanted later, can sit beside `apt/` on the same
  host and the same redirect.

## Verification

On 2026-09-29, in `debian:bookworm` (apt 2.6.1) and `ubuntu:24.04`
(apt 2.8.3) containers, against a signed repository in the standard
layout served over HTTPS by a local server:

- `apt update` and `apt install ivicli` succeeded through two chained
  302 redirects from `pool/`, the shape of a GitHub asset URL.
- With `pool/` redirected to the real v0.4.0 release asset,
  `apt-get download` followed GitHub's redirects and saved a file whose
  SHA-256 matched the asset.
- A package changed behind the redirect was refused, by size when the
  length changed and by hash when one byte was flipped at the same
  length. A `Packages.gz` changed without re-signing was refused by
  `apt update`.

`dpkg --compare-versions` in `debian:bookworm` gives `0.4.0-beta.1` >
`0.4.0`, `0.4.0~beta.1` < `0.4.0` and `0.3.3` < `0.4.0`.

In `debian:bookworm`, a keyring placed at
`/usr/share/keyrings/ivicli-archive-keyring.gpg` by hand was taken over
by a package that ships the same path: `dpkg -i` replaced it without an
error, `dpkg -S` then named the package as its owner, and the next
version of the package replaced it again. An `InRelease` signed by two
Ed25519 keys verified with a keyring holding only one of them, in both
containers, and one signed by a key that had expired in the client's
keyring failed with `EXPKEYSIG`.

Not yet verified: that the Cloudflare Pages `_redirects` rule with
`:tag` and `:file` placeholders returns the 302 to an external host as
intended, and the repository over Cloudflare's own certificate. Both are
checked on a test site before the first publish.
