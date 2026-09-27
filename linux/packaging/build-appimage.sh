#!/bin/sh
# Builds the portable AppImage.
#
#   . packaging/env.sh && packaging/build-appimage.sh
#
# Two things here are not what `cargo tauri build` does on its own:
#
#  * APPIMAGE_EXTRACT_AND_RUN=1 — linuxdeploy and appimagetool are themselves
#    AppImages and need FUSE to mount. It is absent in most sandboxes and
#    containers, so tell them to unpack to a temp dir instead.
#
#  * The bundled WebKit is removed afterwards. linuxdeploy copies
#    libwebkit2gtk/libjavascriptcore into the AppDir, but NOT the helper
#    binaries WebKit executes (WebKitNetworkProcess, WebKitWebProcess), which
#    it looks up at a path baked in at compile time and no longer overridable
#    (WEBKIT_EXEC_PATH was removed upstream). Keeping the libraries would pair
#    a bundled library with the host's helpers — a version mismatch waiting to
#    crash. Dropping them makes the app use the host's matched set, which is
#    both smaller and more reliable.
#
#    Consequence, stated plainly: the host needs libwebkit2gtk-4.1-0. It is
#    present on essentially every Debian/Ubuntu desktop. See README.
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT/src-tauri"

export APPIMAGE_EXTRACT_AND_RUN=1
export NO_STRIP=1

cargo tauri build --bundles appimage

APPDIR="$ROOT/src-tauri/target/release/bundle/appimage/Navigatueur.AppDir"
OUT="$ROOT/src-tauri/target/release/bundle/appimage"

echo
echo "-- retrait de WebKit du bundle --"
for lib in libwebkit2gtk-4.1.so.0 libjavascriptcoregtk-4.1.so.0; do
    if [ -e "$APPDIR/usr/lib/$lib" ]; then
        rm -f "$APPDIR/usr/lib/$lib"
        echo "   retiré: $lib"
    fi
done

CACHE=${XDG_CACHE_HOME:-$HOME/.cache}/tauri
[ -d "$CACHE" ] || CACHE=$HOME/.var/app/com.visualstudio.code/cache/tauri
TOOL="$CACHE/linuxdeploy-plugin-appimage.AppImage"

echo
echo "-- ré-empaquetage --"
rm -f "$OUT"/Navigatueur_*.AppImage
APPIMAGE_EXTRACT_AND_RUN=1 OUTPUT="Navigatueur_0.15.0_amd64.AppImage" \
    "$TOOL" --appdir "$APPDIR" >/dev/null 2>&1

# The plugin writes into the current directory.
[ -f Navigatueur_0.15.0_amd64.AppImage ] && mv -f Navigatueur_0.15.0_amd64.AppImage "$OUT/"

echo
ls -lh "$OUT"/Navigatueur_*.AppImage
