#!/usr/bin/env bash
# Drives Navigatueur for Linux on a virtual X screen like a user would and
# screenshots each step. Used by .github/workflows/linux.yml.
#
#   bash tests/linux/smoke.sh <navigatueur executable> <output folder>
set -u
EXE="$1"
OUT="${2:-captures}"
mkdir -p "$OUT"
OUT=$(cd "$OUT" && pwd)
export DISPLAY=:99
DATA=$(mktemp -d)
step=0
failures=()

Xvfb :99 -screen 0 1440x900x24 > /dev/null 2>&1 &
XVFB=$!
sleep 2

shot() {
  step=$((step + 1))
  local file
  file=$(printf "%s/%02d-%s.png" "$OUT" "$step" "$1")
  import -window root "$file"
  echo "  capture $(basename "$file")"
}

alive() {
  if ! kill -0 "$APP" 2> /dev/null; then
    failures+=("Navigatueur s'est fermé ($1)")
    echo "  ÉCHEC : l'application s'est fermée ($1)"
    return 1
  fi
}

move() { xdotool mousemove "$1" "$2"; sleep "${3:-1}"; }
key() { xdotool key --clearmodifiers "$@"; sleep 1; }

echo "== Démarrage"
NAVIGATUEUR_USER_DATA="$DATA" "$EXE" --no-sandbox > "$OUT/app.log" 2>&1 &
APP=$!
sleep 15
WIN=$(xdotool search --name "Navigatueur" | head -1)
if [ -n "$WIN" ]; then xdotool windowactivate "$WIN" 2> /dev/null; xdotool windowsize "$WIN" 1440 900; xdotool windowmove "$WIN" 0 0; fi
sleep 2
move 720 450 1
shot immersif-demarrage
alive "démarrage" || true

echo "== Bords de l'écran"
move 720 1 1.5
shot bord-haut-barre-outils
move 720 450 2
move 10 450 1.5
shot bord-gauche-colonne-onglets
move 720 450 2

echo "== Une page"
key ctrl+l
xdotool type --delay 40 "fr.wikipedia.org/wiki/Navigateur_web"
sleep 1
shot saisie-adresse-suggestions
key Return
sleep 8
shot page-wikipedia
alive "chargement d'une page" || true

echo "== Onglets"
key ctrl+t
sleep 3
shot nouvel-onglet
key Escape
move 10 450 1.5
shot colonne-deux-onglets
move 120 120 1.5
shot survol-onglet-apercu
move 720 450 2

echo "== Paramètres"
key ctrl+comma
sleep 4
shot parametres
alive "paramètres" || true

echo "== Mode classique"
NAVIGATUEUR_USER_DATA="$DATA" "$EXE" --no-sandbox "https://example.com/" > /dev/null 2>&1 &
sleep 4
shot deuxieme-instance-ouvre-un-onglet

kill "$APP" 2> /dev/null
sleep 2
kill "$XVFB" 2> /dev/null

if grep -qiE "Uncaught|TypeError|ReferenceError" "$OUT/app.log"; then
  failures+=("Erreurs JavaScript dans le journal (voir app.log)")
  grep -iE "Uncaught|TypeError|ReferenceError" "$OUT/app.log" | head -20
fi

if [ ${#failures[@]} -gt 0 ]; then
  for f in "${failures[@]}"; do echo "ÉCHEC : $f"; done
  exit 1
fi
echo "Scénario terminé sans erreur, $step captures."
