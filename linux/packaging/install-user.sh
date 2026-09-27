#!/bin/sh
# Installs Navigatueur for the current user only. No root, no package manager:
# the binary goes to ~/.local/bin and the desktop entry to ~/.local/share,
# both of which are user-owned and already on the standard XDG search paths.
#
#   ./install-user.sh Navigatueur_0.15.0_amd64.AppImage
#
# Uninstall is the reverse and just as simple: see uninstall-user.sh.
set -eu

APPIMAGE=${1:-}
if [ -z "$APPIMAGE" ] || [ ! -f "$APPIMAGE" ]; then
    echo "usage: $0 <chemin/vers/Navigatueur-*.AppImage>" >&2
    exit 1
fi

BIN_DIR=${XDG_BIN_HOME:-$HOME/.local/bin}
APP_DIR=${XDG_DATA_HOME:-$HOME/.local/share}/applications
ICON_DIR=${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps

mkdir -p "$BIN_DIR" "$APP_DIR" "$ICON_DIR"

install -m 755 "$APPIMAGE" "$BIN_DIR/navigatueur"
echo "binaire   -> $BIN_DIR/navigatueur"

# The icon is carried next to this script so the menu entry works even when the
# AppImage has not been extracted.
if [ -f "$(dirname "$0")/navigatueur.png" ]; then
    install -m 644 "$(dirname "$0")/navigatueur.png" "$ICON_DIR/navigatueur.png"
    echo "icône     -> $ICON_DIR/navigatueur.png"
fi

cat > "$APP_DIR/navigatueur.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=Navigatueur
Comment=Navigateur web léger avec blocage de publicités
Exec=$BIN_DIR/navigatueur %U
Icon=navigatueur
Terminal=false
Categories=Network;WebBrowser;
MimeType=text/html;x-scheme-handler/http;x-scheme-handler/https;
StartupWMClass=Navigatueur
DESKTOP
echo "raccourci -> $APP_DIR/navigatueur.desktop"

# Refresh the menu cache if the tool is around; harmless when it is not.
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APP_DIR" || true

case ":$PATH:" in
    *":$BIN_DIR:"*) ;;
    *) echo
       echo "Note: $BIN_DIR n'est pas dans votre PATH."
       echo "Ajoutez ceci à ~/.profile puis reconnectez-vous :"
       echo "    export PATH=\"\$HOME/.local/bin:\$PATH\"" ;;
esac

echo
echo "Installé. Lancez « navigatueur » ou cherchez Navigatueur dans le menu."
