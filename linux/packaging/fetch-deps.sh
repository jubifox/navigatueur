#!/bin/sh
# Pulls webkit2gtk's transitive runtime dependencies into the same private
# sysroot. Debian ships these; the Flatpak SDK we build inside does not (or
# ships incompatible sonames, e.g. a different ICU). Needed both to smoke-test
# here and so the AppImage bundler has real files to copy.
set -eu
SYSROOT=${TOOLCHAIN:-/Virtual/jmasse/toolchain}/sysroot
WORK=${TOOLCHAIN:-/Virtual/jmasse/toolchain}/debs
BASE=https://deb.debian.org/debian
mkdir -p "$WORK"; cd "$WORK"

# pool dir : regex matching the wanted binary package
for spec in \
  "i/icu:libicu76_" \
  "libx/libxml2:libxml2_" \
  "f/flite:libflite1_" \
  "libm/libmanette:libmanette-0.2-0_" \
  "e/enchant-2:libenchant-2-2_" \
  "h/hyphen:libhyphen0_" \
  "libw/libwpe:libwpe-1.0-1_" \
  "w/wpebackend-fdo:libwpebackend-fdo-1.0-1_" \
  "libe/libevdev:libevdev2_" \
  "h/hidapi:libhidapi-hidraw0_"
do
  dir=${spec%%:*}; pat=${spec#*:}
  deb=$(curl -fsSL "$BASE/pool/main/$dir/" \
        | grep -oE "href=\"${pat}[^\"]*_amd64\.deb\"" \
        | sed 's/href="//;s/"//' | sort -V | tail -1) || true
  if [ -z "${deb:-}" ]; then echo "!! introuvable: $pat"; continue; fi
  [ -f "$deb" ] || curl -fsSL -o "$deb" "$BASE/pool/main/$dir/$deb"
  rm -rf x && mkdir x && cd x
  ar x "../$deb" && tar -xf data.tar.* -C "$SYSROOT"
  cd ..
  echo "ok  $deb"
done
