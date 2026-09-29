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
URL does resolve to the newest tag, but a repository based there can only
ever offer the latest version.

Size also matters. The self-contained linux-x64 archive of v0.4.0 is
45 MB and the linux-arm64 one 42 MB, so every release adds about 87 MB
of packages. GitHub Pages limits a site to 1 GB, which 18 stable
releases would already exceed, and Cloudflare Pages refuses any single
file over 25 MB.

## Decision

### 1. Channels

| Channel | For | Install |
| --- | --- | --- |
| NuGet | .NET users | `dotnet tool install -g ivi-cli` |
| GitHub Releases | downloading a file by hand, and mise | archives, and from this ADR `.deb` files |
| APT repository | Debian and Ubuntu | `apt install ivicli` |

The container image (ADR 0018) keeps its own role as a mock instrument.

### 2. Where each part lives

The repository is served at `https://pkg.shortarrow.jp/apt`. The
`shortarrow.jp` zone is already on Cloudflare.

- The signed metadata, `dists/` and the public key, is a Cloudflare
  Pages site. It is small and changes once per release.
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
├── ivicli.gpg
├── dists/stable/
│   ├── InRelease
│   ├── Release
│   └── main/binary-{amd64,arm64}/Packages(.gz)
└── pool/<tag>/ivicli_<version>_<arch>.deb   (redirected)
```

The source line is:

```
deb [signed-by=/etc/apt/keyrings/ivicli.gpg] https://pkg.shortarrow.jp/apt stable main
```

A flat repository (`Packages` and `Release` beside the files, source
line ending in `./`) would work for one package too. It was not chosen
because a second suite, for pre-releases, would then mean a new layout
and a new source line for every existing user, while in the standard
layout it is one more directory under `dists/`.

### 4. The package

- Named `ivicli`, the command's name. The NuGet and AUR packages keep
  `ivi-cli`, which is what those ecosystems already know.
- Built from the self-contained binary, for amd64 and arm64. A package
  of the framework-dependent build would be 2 MB, but Debian ships no
  .NET runtime, and the user would first have to add Microsoft's
  repository, which is the step this channel exists to remove.
- Version is the release version. Pre-releases do not enter `stable`.
  A maintenance release on an older line (ADR 0022) does, since APT
  always picks the highest version and the older one cannot displace it.

### 5. Signing key

The repository has its own signing key, kept as a GitHub Actions secret
for the release workflow. It is not the maintainer's personal key: the
key that signs every repository update should be one that can be
revoked and replaced without touching anything else. Its public half is
published at `https://pkg.shortarrow.jp/apt/ivicli.gpg`, and the install
guide stores it in `/etc/apt/keyrings/`, where Debian places keys that
an administrator adds by hand.

### 6. Publishing

`release.yml` gains three steps after the archives are built:

1. Build the two `.deb` files and attach them to the GitHub Release.
2. Fetch the published `Packages` indexes, add the new entries, and
   write and sign `Release` and `InRelease`. The published index is the
   record of every version; no step downloads old packages again.
3. Deploy the site to Cloudflare Pages.

## Consequences

- `apt update` and `apt upgrade` deliver new versions to Debian and
  Ubuntu machines.
- The release workflow depends on Cloudflare and on a second signing
  key. If either is unavailable, the GitHub Release still ships, and the
  repository lags until the site is deployed.
- Losing the signing key means publishing a new public key and asking
  users to fetch it again.
- An RPM repository, if wanted later, can sit beside `apt/` on the same
  host and the same redirect.

## Verification

On 2026-09-29, in `debian:bookworm` (apt 2.6.1) and `ubuntu:24.04`
(apt 2.8.3) containers, against a signed repository in the standard
layout served over HTTPS by a local server:

- `apt update` and `apt install ivicli` succeeded through two chained
  302 redirects from `pool/`, the shape of a GitHub asset URL.
- With `pool/` redirected to the real v0.4.0 release asset,
  `apt-get download` followed `github.com` to
  `objects.githubusercontent.com` and saved a file whose SHA-256 matched
  the asset.
- A package changed behind the redirect was refused, by size when the
  length changed and by hash when one byte was flipped at the same
  length. A `Packages.gz` changed without re-signing was refused by
  `apt update`.

Not yet verified: that the Cloudflare Pages `_redirects` rule with
`:tag` and `:file` placeholders returns the 302 as intended, and the
repository over Cloudflare's own certificate. Both are checked on a
test site before the first publish.
