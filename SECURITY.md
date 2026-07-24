# Security Policy

## Supported versions

Only the **latest release** receives security fixes. Fixes ship as a
new release from `main`; there are no backport branches. If you are
pinned to an older version, upgrade to the latest release before
reporting.

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Use
GitHub's private vulnerability reporting instead:

1. Go to the repository's **Security** tab.
2. Choose **Report a vulnerability** and describe the issue —
   affected command/component, reproduction steps, and impact.

Reports are acknowledged on a best-effort basis (this is a
volunteer-maintained project, no SLA). Confirmed vulnerabilities are
fixed in the next release and credited in the advisory unless you
ask otherwise.

## Scope notes

- The **Management API** (`ivicli api start`) binds to loopback by
  default; binding beyond loopback is designed to be used with PAT
  authentication and TLS. Configurations that expose the API without
  them are out of scope.
- The **gateway servers** (HiSLIP / VXI-11 / SOCKET) implement
  instrument protocols that have no authentication by design. Run
  them on trusted networks only; "an unauthenticated peer can send
  SCPI to the gateway" is inherent to those protocols, not a
  vulnerability.
- The **mock container** is a test fixture. Reports about hardening
  the image are welcome, but it is not intended for exposure to
  untrusted networks.
