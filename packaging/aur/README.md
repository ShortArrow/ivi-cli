# AUR packaging

`ivi-cli-bin/` holds the PKGBUILD published to the AUR as
[`ivi-cli-bin`](https://aur.archlinux.org/packages/ivi-cli-bin). It repackages
the `ivicli-<version>-linux-x64-selfcontained.zip` asset from the matching
GitHub Release, so the package installs a working `ivicli` on a machine with no
.NET at all.

This directory is the source of truth. The AUR repository is a separate git
repo containing only `PKGBUILD` and `.SRCINFO`; keeping the canonical copy here
means the packaging travels with the code it packages.

## Bumping to a new release

1. Edit `ivi-cli-bin/PKGBUILD`: set `pkgver` to the new version and reset
   `pkgrel=1`. (`pkgrel` only moves when the packaging changes and the upstream
   version does not.)
2. Replace `sha256sums`. Every release publishes a `SHA256SUMS` asset, so the
   hash can be read from there instead of downloading the zip:

   ```sh
   version=0.3.2
   curl -sL "https://github.com/ShortArrow/ivi-cli/releases/download/v${version}/SHA256SUMS" \
     | grep "ivicli-${version}-linux-x64-selfcontained.zip"
   ```

   `updpkgsums` works too, and downloads the asset to hash it itself.
3. Rebuild and reinstall in a throwaway Arch container — an unbuilt PKGBUILD
   has not been checked. `makepkg` refuses to run as root, hence the build
   user:

   ```sh
   docker run --rm -v "$PWD/ivi-cli-bin:/pkg" archlinux:base-devel bash -c '
     pacman -Syu --noconfirm --needed base-devel namcap &&
     useradd -m build && cp /pkg/PKGBUILD /home/build/ && chown -R build /home/build &&
     su build -c "cd ~ && makepkg -f" &&
     namcap /home/build/*.pkg.tar.zst'
   ```

   Five `namcap` warnings are expected and can be ignored. The unused
   `libdl` / `librt` / `libpthread` links are glibc compatibility stubs the .NET
   host still records. `icu` and `openssl` look unneeded to `namcap` because it
   reads the ELF and .NET loads both through `dlopen`; both are in fact
   mandatory, and the way to check that claim is to remove the package and run
   the CLI rather than to trust either tool:

   ```sh
   pacman -Rdd icu     && ivicli --version    # Couldn't find a valid ICU package
   pacman -Rdd openssl && ivicli visa scan    # No usable version of libssl was found
   ```

   Anything `namcap` reports beyond those five is new and worth reading.
4. Regenerate `.SRCINFO` from the edited PKGBUILD and commit both:

   ```sh
   cd ivi-cli-bin && makepkg --printsrcinfo > .SRCINFO
   ```

   The two files must agree — the AUR rejects a push whose `.SRCINFO` does not
   match, and `namcap` on the PKGBUILD will not catch the mismatch for you.

## ivi-cli (built from source)

`ivi-cli/PKGBUILD` builds the framework-dependent publish from the release
tag's source tarball and installs it to `/usr/lib/ivi-cli` with
`/usr/bin/ivicli` pointing at the apphost. It depends on `aspnet-runtime`
and takes `dotnet-sdk` as a makedepend.

Two accommodations in it are for Arch's SDK packaging rather than for this
project, and both fail the build loudly if removed:

- `prepare()` deletes `global.json`. It pins the SDK to 10.0.204 with
  `rollForward=latestFeature`, which cannot roll down to the 10.0.1xx band
  Arch ships, so the build stops with "A compatible .NET SDK was not
  found". The pin is there so upstream CI builds on one known SDK; a
  distribution package builds on the distribution's SDK.
- `-p:AllowMissingPrunePackageData=true`, because Arch's `dotnet-sdk` ships
  without the `PrunePackageData` folder and the ASP.NET Core reference
  otherwise fails with `NETSDK1226`. The SDK names this flag itself in the
  error text.

Bumping it needs the tag tarball's `sha256sum` rather than a line from the
release `SHA256SUMS`, which covers the published archives and not the
GitHub-generated source tarball:

```sh
curl -fsSL "https://github.com/ShortArrow/ivi-cli/archive/refs/tags/vX.Y.Z.tar.gz" | sha256sum
```

Expected `namcap` output is four warnings: `libm` unused (a glibc stub the
apphost records), `glibc` / `libstdc++` / `libgcc` "implicitly satisfied"
(they arrive through `aspnet-runtime`, so the package does not declare
them), and `aspnet-runtime` "may not be needed", which is `namcap` reading
the ELF while the requirement lives in `ivicli.runtimeconfig.json`.

## The two packages conflict

`ivi-cli-bin` declares `provides=('ivi-cli')` and `conflicts=('ivi-cli')`;
`ivi-cli` declares `conflicts=('ivi-cli-bin')`. Both own `/usr/bin/ivicli`,
so this is deliberate — a user picks one.

## Publishing to the AUR

Pushing needs an AUR account with a registered SSH public key, so it is a
maintainer action and cannot run from CI — the release workflow deliberately
stops at uploading assets.

First time, clone the (possibly empty) AUR repo:

```sh
git clone ssh://aur@aur.archlinux.org/ivi-cli-bin.git aur-ivi-cli-bin
```

Then, for each release, copy the two files across and push:

```sh
cp packaging/aur/ivi-cli-bin/{PKGBUILD,.SRCINFO} aur-ivi-cli-bin/
cd aur-ivi-cli-bin
git add PKGBUILD .SRCINFO
git commit -m "upgpkg: ivi-cli-bin 0.3.2-1"
git push
```

The AUR runs a server-side hook that parses `.SRCINFO`; a push that fails there
is rejected outright rather than landing half-applied.
