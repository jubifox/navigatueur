#!/bin/sh
# Removes what install-user.sh put in place. User data under
# ~/.config/navigatueur and ~/.local/share/navigatueur is deliberately kept,
# matching the Windows uninstaller's behaviour — reinstalling keeps settings
# and history. Pass --purge to drop those too.
set -eu

BIN_DIR=${XDG_BIN_HOME:-$HOME/.local/bin}
APP_DIR=${XDG_DATA_HOME:-$HOME/.local/share}/applications
ICON_DIR=${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps

rm -fv "$BIN_DIR/navigatueur" "$APP_DIR/navigatueur.desktop" "$ICON_DIR/navigatueur.png" || true
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APP_DIR" || true

if [ "${1:-}" = "--purge" ]; then
    rm -rfv "${XDG_CONFIG_HOME:-$HOME/.config}/navigatueur" \
            "${XDG_DATA_HOME:-$HOME/.local/share}/navigatueur"
    echo "Données utilisateur supprimées."
else
    echo "Données utilisateur conservées (--purge pour les supprimer)."
fi
