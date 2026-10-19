#!/bin/sh
# Builds the ivicli Debian package from a self-contained publish (ADR 0052
# §2): the binary under /usr/lib/ivicli, a /usr/bin/ivicli symlink, and
# the native libraries the self-contained runtime loads as Depends.
#
# Usage: build-deb.sh <self-contained dir> <version> <amd64|arm64> <out dir>
# Writes <out dir>/ivicli_<version>-1_<arch>.deb. Needs dpkg-deb.
set -eu

src="${1:?usage: build-deb.sh <self-contained dir> <version> <amd64|arm64> <out dir>}"
version="${2:?version}"
arch="${3:?arch}"
out="${4:?out dir}"

case "$version" in
  *-*)
    echo "build-deb: $version is a pre-release, which gets no package (ADR 0052 §2)" >&2
    exit 1
    ;;
esac
case "$arch" in
  amd64 | arm64) ;;
  *)
    echo "build-deb: unknown architecture $arch" >&2
    exit 1
    ;;
esac
[ -x "$src/ivicli" ] || {
  echo "build-deb: $src/ivicli is not an executable" >&2
  exit 1
}

root="$(mktemp -d)"
trap 'rm -rf "$root"' EXIT

chmod 0755 "$root"
mkdir -p "$root/usr/lib/ivicli" "$root/usr/bin" "$root/DEBIAN"
# XML doc comments mean nothing to someone running the CLI, as in the
# release archives.
(cd "$src" && find . -type f ! -name '*.xml' -exec cp --parents {} "$root/usr/lib/ivicli/" \;)
chmod 0755 "$root/usr/lib/ivicli/ivicli"
ln -s ../lib/ivicli/ivicli "$root/usr/bin/ivicli"

installed_size="$(du -sk "$root/usr" | cut -f1)"
cat >"$root/DEBIAN/control" <<EOF
Package: ivicli
Version: ${version}-1
Architecture: ${arch}
Maintainer: ShortArrow <bamboogeneral@shortarrow.jp>
Installed-Size: ${installed_size}
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, ca-certificates, libssl3 | libssl3t64, shortarrow-archive-keyring
Section: utils
Priority: optional
Homepage: https://github.com/ShortArrow/ivi-cli
Description: CLI for managing, diagnosing and operating VISA/IVI instruments
 ivicli talks to instruments over VISA resources (TCPIP, USB, GPIB and
 ASRL), serves them through HiSLIP, VXI-11 and SOCKET gateways, and runs
 a scenario-driven mock instrument. This package carries the
 self-contained build, so it needs no .NET runtime.
EOF

mkdir -p "$out"
deb="$out/ivicli_${version}-1_${arch}.deb"
dpkg-deb --root-owner-group -Zxz --build "$root" "$deb" >/dev/null
echo "$deb"
