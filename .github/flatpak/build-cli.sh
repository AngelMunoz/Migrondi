#!/usr/bin/env bash
# Renders the CLI Flatpak manifest and builds the tool.
#
# Local:  build-cli.sh <tarball>
#   The tarball's name carries the version
#   (migrondi-<version>-linux-x64.tar.gz or
#   migrondi-<version>-linux-arm64.tar.gz); its hash and file://
#   address fill the manifest. Build on a host of the tarball's
#   architecture: flatpak-builder packages for the host arch.
# CI:     build-cli.sh <version> <url> <sha256>
#   The manifest embeds the release URL; flatpak-builder downloads it.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo="${FLATPAK_REPO:-/tmp/migrondi-flatpak-repo}"

case $# in
  1)
    tarball="$(readlink -f "$1")"
    name="$(basename "$tarball")"

    case "$name" in
      migrondiui-*)
        echo "That is the UI tarball, not the CLI tarball: $name" >&2
        exit 1
        ;;
    esac

    version="${name#migrondi-}"
    version="${version%-linux-x64.tar.gz}"
    version="${version%-linux-arm64.tar.gz}"

    if [ -z "$version" ] || [ "$version" = "$name" ]; then
      echo "The tarball name carries no version: $name" >&2
      exit 1
    fi

    url="file://${tarball}"
    hash="$(sha256sum "$tarball" | cut -d' ' -f1)"
    ;;
  3)
    version="$1"
    url="$2"
    hash="$3"
    ;;
  *)
    echo "usage: build-cli.sh <tarball> | build-cli.sh <version> <url> <sha256>" >&2
    exit 1
    ;;
esac

stage="$(mktemp -d /tmp/migrondi-flatpak-cli.XXXXXX)"

mkdir -p "$stage/files"
cp -a "$here/files/." "$stage/files/"

sed \
  -e "s|@URL@|${url}|g" \
  -e "s/@HASH@/${hash}/g" \
  "$here/io.github.angelmunoz.migrondi.json.in" > "$stage/manifest.json"

# The system binary in CI; the flatpak'd one on a host without it. The
# sandboxed builder sees the stage only with the filesystem grants.
if command -v flatpak-builder >/dev/null 2>&1; then
  builder=(flatpak-builder)
else
  builder=(flatpak run --filesystem=host --filesystem=/tmp org.flatpak.Builder)
fi

"${builder[@]}" \
  --force-clean \
  --state-dir="$stage/state" \
  --repo="$repo" \
  --user \
  --install \
  "$stage/build" "$stage/manifest.json"

echo "Installed io.github.angelmunoz.migrondi ${version} (repo: ${repo})."
