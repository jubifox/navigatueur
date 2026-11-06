#!/usr/bin/env bash
# Drives the Android app on an emulator like a user would and screenshots
# each step. Used by .github/workflows/android.yml (Pixel 6 profile,
# 1080x2400); can be run by hand against any emulator or phone with adb.
#
#   bash tests/android/smoke.sh <folder with the APKs> <output folder>
set -u
APK_DIR="${1:-android/app/build/outputs/apk/debug}"
OUT="${2:-captures}"
PKG=com.navigatueur.mobile
mkdir -p "$OUT"
step=0
failures=()

shot() {
  step=$((step + 1))
  local file
  file=$(printf "%s/%02d-%s.png" "$OUT" "$step" "$1")
  adb exec-out screencap -p > "$file"
  echo "  capture $(basename "$file")"
}

tap() { adb shell input tap "$1" "$2"; }

open_url() {
  adb shell "am start -n $PKG/.MainActivity -a android.intent.action.VIEW -d '$1'" > /dev/null
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

adb shell input swipe 540 1700 540 700 300
sleep 2
shot defilement-barre-masquee
adb shell input swipe 540 900 540 1500 300
sleep 2
shot defilement-barre-revenue

echo "== Saisie d'adresse"
tap 400 2262
sleep 2
adb shell input text "wikip"
sleep 2
shot saisie-suggestions
adb shell input keyevent 4
sleep 1
adb shell input keyevent 4
sleep 1

echo "== Vue des onglets"
tap 884 2262
sleep 3
shot onglets
adb shell input keyevent 4
sleep 1

echo "== Menu"
tap 1005 2262
sleep 3
shot menu
adb shell input keyevent 4
sleep 1

echo "== Mode sombre des sites (Dark Reader)"
open_url "https://example.com/"
sleep 12
shot example-clair
alive "example.com"

echo "== Extensions"
adb shell am start -n $PKG/.MainActivity -a com.navigatueur.mobile.NEW_TAB > /dev/null
sleep 3
shot nouvel-onglet-saisie
adb shell input keyevent 4
sleep 2
shot nouvel-onglet-accueil

alive "fin du scénario"

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
