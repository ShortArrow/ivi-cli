#!/bin/sh
# Installs the ivicli packages with apt in fresh Debian and Ubuntu
# containers and runs `ivicli --version` (ADR 0052 §4). The packages are
# served from a throwaway repository signed with a key made for this run,
# beside a stand-in shortarrow-archive-keyring package that holds that
# key, so apt checks the signature as it does against the registry.
#
# Usage: install-test.sh <deb dir> <version> <amd64|arm64>
# Needs docker, gpg, dpkg-deb and apt-ftparchive (apt-utils).
set -eu

debs="${1:?usage: install-test.sh <deb dir> <version> <amd64|arm64>}"
version="${2:?version}"
arch="${3:?arch}"
keyring_name=shortarrow-archive-keyring

work="$(mktemp -d)"
containers=''
cleanup() {
  for c in $containers; do docker rm -f "$c" >/dev/null 2>&1 || true; done
  rm -rf "$work"
}
trap cleanup EXIT

export GNUPGHOME="$work/gnupg"
mkdir -m 0700 "$GNUPGHOME"
gpg --batch --quiet --passphrase '' --quick-gen-key 'ivicli install test <test@invalid>' ed25519 sign 1d

repo="$work/repo/apt"
mkdir -p "$repo/pool/test" "$repo/dists/stable/main/binary-$arch"
gpg --batch --export >"$repo/$keyring_name.gpg"

# The stand-in keyring package, so the real package's Depends is met the
# way it is on a machine that uses the registry.
kr="$work/keyring"
mkdir -p "$kr/DEBIAN" "$kr/usr/share/keyrings"
cp "$repo/$keyring_name.gpg" "$kr/usr/share/keyrings/"
cat >"$kr/DEBIAN/control" <<EOF
Package: $keyring_name
Version: 0.0.0~test
Architecture: all
Maintainer: ivicli install test <test@invalid>
Description: throwaway keyring for the ivicli install test
EOF
dpkg-deb --root-owner-group --build "$kr" "$repo/pool/test/${keyring_name}_0.0.0~test_all.deb" >/dev/null
cp "$debs"/ivicli_*_"$arch".deb "$repo/pool/test/"

(
  cd "$repo"
  apt-ftparchive --arch "$arch" packages pool >"dists/stable/main/binary-$arch/Packages"
  gzip -k9 "dists/stable/main/binary-$arch/Packages"
  apt-ftparchive \
    -o APT::FTPArchive::Release::Suite=stable \
    -o APT::FTPArchive::Release::Codename=stable \
    -o APT::FTPArchive::Release::Components=main \
    -o "APT::FTPArchive::Release::Architectures=$arch" \
    release dists/stable >"$work/Release"
  mv "$work/Release" dists/stable/Release
  gpg --batch --yes --clearsign -o dists/stable/InRelease dists/stable/Release
)

failed=0
for image in debian:bookworm ubuntu:24.04; do
  name="ivicli-install-test-$$-$(echo "$image" | tr ':.' '--')"
  containers="$containers $name"
  docker create --name "$name" "$image" sh -c "
    set -eu
    cp /repo/apt/$keyring_name.gpg /usr/share/keyrings/
    echo 'deb [signed-by=/usr/share/keyrings/$keyring_name.gpg] file:/repo/apt stable main' \
      >/etc/apt/sources.list.d/ivicli.list
    apt-get update -qq
    DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends $keyring_name ivicli >/dev/null
    out=\$(ivicli --version)
    echo \"ivicli --version -> \$out\"
    case \"\$out\" in
      '$version'*) ;;
      *) echo 'expected version $version'; exit 1 ;;
    esac
  " >/dev/null
  docker cp "$work/repo" "$name:/repo"
  echo "== $image ($arch)"
  if docker start -a "$name"; then
    echo "install test passed on $image"
  else
    echo "install test FAILED on $image"
    failed=1
  fi
done
exit "$failed"
