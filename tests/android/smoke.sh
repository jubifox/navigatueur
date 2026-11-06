#!/usr/bin/env bash
# Drives the Android app on an emulator like a user would and screenshots
# each step. Used by .github/workflows/android.yml (Pixel 6 profile,
# 1080x2400); can be run by hand against any emulator or phone with adb.
#
#   bash tests/android/smoke.sh <folder with the APKs> <output folder>
#
# Buttons are found by their accessibility name (what TalkBack reads) in
# the screen's UI tree, rather than at fixed coordinates.
set -u
APK_DIR="${1:-android/app/build/outputs/apk/debug}"
OUT="${2:-captures}"
PKG=com.navigatueur.mobile
UI="$OUT/.ui.xml"
mkdir -p "$OUT"
step=0
failures=()

# The bottom toolbar's row on a Pixel 6 (the address field is on its left).
TOOLBAR_Y=2262

ui_dump() {
  adb shell uiautomator dump /sdcard/ui.xml > /dev/null 2>&1 || return 1
  adb exec-out cat /sdcard/ui.xml > "$UI" 2> /dev/null
}

# Prints "x y" (the center) of the first on-screen element whose accessibility
# name or text is exactly $1, from the last dump; nothing if there is none.
find_node() {
  python3 - "$UI" "$1" <<'PY'
import re, sys, xml.etree.ElementTree as ET
try:
    root = ET.parse(sys.argv[1]).getroot()
except Exception:
    sys.exit(0)
want = sys.argv[2]
def named(value):
    # A clickable row that merges its texts reads "Title, subtitle".
    return value == want or (value or '').startswith(want + ', ')
for node in root.iter('node'):
    if named(node.get('content-desc')) or named(node.get('text')):
        x1, y1, x2, y2 = map(int, re.findall(r'-?\d+', node.get('bounds', '')))
        if x2 > x1 and y2 > y1:
            print((x1 + x2) // 2, (y1 + y2) // 2)
            break
PY
}

# Taps the element named $1 ($2 = "long" for a long press).
tap_ui() {
  ui_dump
  local xy
  xy=$(find_node "$1")
  if [ -z "$xy" ]; then
    echo "  ÉCHEC : élément « $1 » introuvable"
    failures+=("Élément introuvable : $1")
    return 1
  fi
  if [ "${2:-}" = "long" ]; then
    # shellcheck disable=SC2086
    adb shell input swipe $xy $xy 900
  else
    # shellcheck disable=SC2086
    adb shell input tap $xy
  fi
}

# Same, scrolling the screen up (a few times at most) until the element shows.
tap_ui_scrolling() {
  local xy y
  for _ in 1 2 3 4 5 6; do
    ui_dump
    xy=$(find_node "$1")
    y=${xy#* }
    if [ -n "$xy" ] && [ "$y" -gt 250 ] && [ "$y" -lt 2150 ]; then
      # shellcheck disable=SC2086
      adb shell input tap $xy
      return 0
    fi
    adb shell input swipe 540 1700 540 1000 400
    sleep 1
  done
  echo "  ÉCHEC : élément « $1 » introuvable"
  failures+=("Élément introuvable : $1")
  return 1
}

# The emulator's own launcher sometimes stops answering while the engine
# starts: its "isn't responding" dialog would cover everything.
dismiss_system_dialogs() {
  ui_dump || return 0
  if grep -q "isn't responding\|ne répond pas" "$UI" 2> /dev/null; then
    local xy
    xy=$(find_node "Wait")
    [ -z "$xy" ] && xy=$(find_node "Attendre")
    # shellcheck disable=SC2086
    [ -n "$xy" ] && adb shell input tap $xy && echo "  (fenêtre « ne répond pas » du système fermée)" && sleep 1
  fi
}

shot() {
  dismiss_system_dialogs
  step=$((step + 1))
  local file
  file=$(printf "%s/%02d-%s.png" "$OUT" "$step" "$1")
  adb exec-out screencap -p > "$file"
  echo "  capture $(basename "$file")"
}

open_url() {
  adb shell "am start -n $PKG/.MainActivity -a android.intent.action.VIEW -d '$1'" > /dev/null
}

back() {
  adb shell input keyevent 4
  sleep "${1:-1}"
}

alive() {
  if ! adb shell pidof "$PKG" > /dev/null; then
    failures+=("Navigatueur s'est fermé ($1)")
    echo "  ÉCHEC : l'application s'est fermée ($1)"
  fi
}

apk=$(ls "$APK_DIR"/*x86_64*.apk | head -1)
echo "== Installation de $(basename "$apk")"
adb install -r "$apk"
adb shell pm grant $PKG android.permission.POST_NOTIFICATIONS 2> /dev/null || true
adb shell settings put global hide_error_dialogs 1 2> /dev/null || true
adb logcat -c

echo "== Premier démarrage"
adb shell am start -n $PKG/.MainActivity > /dev/null
# GeckoView starts, and the built-in extensions are installed from addons.mozilla.org.
sleep 30
shot accueil
alive "démarrage"

echo "== Une page"
open_url "https://fr.wikipedia.org/wiki/Navigateur_web"
sleep 20
shot page-wikipedia
alive "chargement d'une page"

echo "== Tirer pour actualiser"
adb shell input swipe 540 600 540 1500 700
sleep 0.5
shot tirer-pour-actualiser
sleep 6

echo "== Défilement : la barre s'efface puis revient"
adb shell input swipe 540 1700 540 700 300
sleep 2
shot defilement-barre-masquee
adb shell input swipe 540 900 540 1500 300
sleep 2
shot defilement-barre-revenue

echo "== Saisie d'adresse : suggestions locales et du moteur, touches rapides"
adb shell input tap 400 $TOOLBAR_Y
sleep 2
adb shell input text "meteo"
sleep 4
shot saisie-suggestions
back
back

echo "== Une deuxième page dans le même onglet"
adb shell input tap 400 $TOOLBAR_Y
sleep 2
adb shell input text "example.com"
adb shell input keyevent 66
sleep 10
shot example-meme-onglet
alive "example.com"

echo "== Menu en grille, puis appui long sur Retour : les pages de l'onglet"
tap_ui "Menu du navigateur"
sleep 2
shot menu-grille
tap_ui "Retour" long
sleep 2
shot historique-onglet
back

echo "== Vue des onglets par un geste sur la barre"
adb shell input swipe 400 $TOOLBAR_Y 400 1800 250
sleep 3
shot onglets-geste
tap_ui "Afficher en liste"
sleep 2
shot onglets-liste
tap_ui "Afficher en grille"
sleep 1
back

echo "== Personnaliser"
tap_ui "Menu du navigateur"
sleep 2
tap_ui "Personnaliser"
sleep 3
shot personnaliser
tap_ui "Aurore"
sleep 2
shot theme-aurore
tap_ui_scrolling "Fond dans toute l'application"
sleep 1
shot fond-partout
tap_ui_scrolling "Accueil"
sleep 1
tap_ui_scrolling "Partager"
sleep 1
tap_ui_scrolling "Horloge et salutation"
sleep 1
shot barre-et-accueil-reglages
back 2
shot barre-avec-boutons
alive "personnaliser"

echo "== Page d'accueil personnalisée, par le bouton Accueil ajouté à la barre"
tap_ui "Page d'accueil"
sleep 3
shot accueil-personnalise

echo "== Onglets sur le fond, fermeture par glissement, puis Annuler"
adb shell input swipe 400 $TOOLBAR_Y 400 1800 250
sleep 3
shot onglets-fond
tap_ui "Afficher en liste"
sleep 2
adb shell input swipe 900 340 60 340 250
sleep 1
shot onglet-ferme-annuler
tap_ui "Annuler"
sleep 2
shot onglet-revenu
tap_ui "Afficher en grille"
sleep 1
back

echo "== Paramètres"
tap_ui "Menu du navigateur"
sleep 2
tap_ui "Paramètres"
sleep 3
shot parametres
back 2

alive "fin du scénario"

rm -f "$UI"
if adb logcat -d | grep -A3 "FATAL EXCEPTION" | grep -q "$PKG"; then
  adb logcat -d | grep -A30 "FATAL EXCEPTION" > "$OUT/crash.txt"
  failures+=("Une exception a fait planter l'application (voir crash.txt)")
fi
adb logcat -d > "$OUT/logcat.txt"

if [ ${#failures[@]} -gt 0 ]; then
  for f in "${failures[@]}"; do echo "ÉCHEC : $f"; done
  exit 1
fi
echo "Scénario terminé sans erreur, $step captures."
