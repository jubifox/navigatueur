#!/bin/sh
# Installs the webkit2gtk-4.1 headers + shared objects that Tauri/wry link
# against, without root: Debian packages are unpacked into a private sysroot
# and surfaced to pkg-config via PKG_CONFIG_SYSROOT_DIR.
set -eu
SYSROOT=${TOOLCHAIN:-/Virtual/jmasse/toolchain}/sysroot
POOL=https://deb.debian.org/debian/pool/main
WORK=${TOOLCHAIN:-/Virtual/jmasse/toolchain}/debs
VER='2.52.6-1~deb13u1'

mkdir -p "$WORK" "$SYSROOT"
cd "$WORK"

for pkg in \
  "w/webkit2gtk/libwebkit2gtk-4.1-0_${VER}_amd64.deb" \
  "w/webkit2gtk/libwebkit2gtk-4.1-dev_${VER}_amd64.deb" \
  "w/webkit2gtk/libjavascriptcoregtk-4.1-0_${VER}_amd64.deb" \
  "w/webkit2gtk/libjavascriptcoregtk-4.1-dev_${VER}_amd64.deb"
do
  file=$(basename "$pkg")
  [ -f "$file" ] || curl -fsSL -o "$file" "$POOL/$pkg"
  echo "-> $file"
  rm -rf x && mkdir x && cd x
  ar x "../$file"
  tar -xf data.tar.* -C "$SYSROOT"
  cd ..
done

echo "=== sysroot .pc ==="
find "$SYSROOT" -name "*.pc" | sed "s|$SYSROOT||"
