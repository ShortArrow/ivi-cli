# 0052. Debian packages for the APT repository

- Status: Draft
- Date: 2026-09-29, revised 2026-10-20

## Context

A Linux user can install ivi-cli today by unpacking a release archive
by hand, through mise (which reads the same archives), or as a .NET tool
if a .NET SDK is present. None of these is updated by the system's own
package manager, so a Debian or Ubuntu machine keeps whatever version was
unpacked until someone repeats the steps.

APT needs a signed repository, which GitHub Releases cannot be. That
repository is shared with ShortArrow's other tools and lives in
[ShortArrow/pkg](https://github.com/ShortArrow/pkg), served at
`https://pkg.shortarrow.jp`; its ADR 0001 records how it is laid out,
signed and published, and why its packages stay GitHub Release assets
behind a redirect. This ADR covers what ivi-cli provides it: the
packages, built and tested here and attached to each release.

## Decision

### 1. Channels

| Channel | For | Install |
| --- | --- | --- |
| NuGet | .NET users | `dotnet tool install -g ivi-cli` |
| GitHub Releases | downloading a file by hand, and mise | archives, and from this ADR `.deb` files |
| APT repository | Debian and Ubuntu | `apt install ivicli` |
| AUR | Arch Linux | `ivi-cli-bin` or `ivi-cli`, from `packaging/aur` |

The container image (ADR 0018) keeps its own role as a mock instrument.

### 2. The package

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
  respectively. ICU is not needed: the CLI runs with invariant
  globalization, without which it aborts at start-up on a machine that
  has no ICU.
- It also depends on the registry's keyring package,
  `shortarrow-archive-keyring`, so that a rotated key reaches every
  machine that has ivicli installed, whichever version it runs. That
  package lives only in the registry, so a `.deb` downloaded from a
  release installs with `apt` once the registry's source is set up, and
  not on its own.
- The Debian version is the release version with revision `1`
  (`0.4.0-1`). Packaging changes ship with the next release.
- Pre-releases get no package. A version containing `-` would sort after
  the final release in Debian's ordering (`0.4.0-beta.1` > `0.4.0`), and
  the registry refuses one.
- A `dotnet tool` install of the same command lives in
  `~/.dotnet/tools`, and whichever directory comes first on `PATH`
  wins. The install guide says how to tell which one runs.

### 3. Building and attaching

The `.deb` files are built from the `publish` job's self-contained
artifacts before the GitHub Release is created, so they are attached to
it and listed in its `SHA256SUMS` with every other asset. The registry
checks each package against that file before indexing it.

The registry finds the release itself. An hourly workflow there lists
the stable releases of the projects it serves and publishes each new
version, so this repository holds no credential for the registry and
sends it nothing. A token that could notify it would carry write access
to the registry's repository, keyring included.

Once a version is published, its `.deb` assets are not replaced by
different files under the same names. The registry's index records each
file's size and hash, so `apt` rejects a replaced file until the
registry publishes that version again.

### 4. The install test

Both `.deb` files are built from a self-contained publish, amd64 on
`ubuntu-latest` and arm64 on `ubuntu-24.04-arm`. Each runner serves them
from a throwaway repository signed with a key generated for the run,
beside a stand-in `shortarrow-archive-keyring` that holds that key,
installs both with `apt` in fresh `debian:bookworm` and `ubuntu:24.04`
containers, and runs `ivicli --version` (`packaging/apt/install-test.sh`).
The package itself is built by `packaging/apt/build-deb.sh`. A missing
`Depends` entry for a library the runtime loads at start-up shows as a
binary that does not start in a container that has only what `apt`
installed. A library loaded only on first use, such as OpenSSL on the
first TLS connection, and the certificate files of `ca-certificates` are
not exercised; their entries are reviewed in the pull request that
changes `Depends`.

- It runs on pull requests that change `packaging/apt/`, the workflows
  that build the packages, or what decides the published binary: `src/`,
  `Directory.Build.props`, `Directory.Packages.props` and `global.json`.
  No secret is involved, so pull requests from forks run it too.
- It also runs in `release.yml`, in a job after `publish` that `pack`,
  `docker` and `release` wait for, so a package that does not install
  stops the release before NuGet, ghcr.io or the GitHub Release receives
  anything.

The check that the published repository serves the package, through
Cloudflare and GitHub's asset URLs, runs in the registry after it
publishes.

## Consequences

- `apt update` and `apt upgrade` deliver new versions to Debian and
  Ubuntu machines once the registry indexes them.
- Every stable release carries two `.deb` files, about 90 MB together
  before compression, beside the archives they repeat.
- A release that builds a package which does not install stops before
  anything is published.
- A release reaches APT within about an hour of its GitHub Release,
  when the registry's next run finds it.
- Reaching APT users depends on the registry's workflow and keys. If
  either is unavailable, the GitHub Release still ships with its `.deb`
  files, and the repository lags until the registry's run is repeated.

## Verification

On 2026-09-29, in `debian:bookworm` (apt 2.6.1) and `ubuntu:24.04`
(apt 2.8.3) containers, `apt install ivicli` from a signed repository
installed a package whose `pool/` path redirected twice, the shape of a
GitHub asset URL, and with `pool/` redirected to the real v0.4.0 release
asset `apt-get download` saved a file whose SHA-256 matched the asset.
The registry's ADR 0001 carries the rest of that day's checks.

`dpkg --compare-versions` in `debian:bookworm` gives `0.4.0-beta.1` >
`0.4.0`, `0.4.0~beta.1` < `0.4.0` and `0.3.3` < `0.4.0`.
