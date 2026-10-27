#!/usr/bin/env bash
# Renders documentation screenshots of the widget with demo data (fictional people) in the real
# XENEON EDGE slot sizes, using headless Microsoft Edge. Output: docs/images/<lang>/*.png
#
# Usage: bash tools/screenshots.sh
set -euo pipefail
cd "$(dirname "$0")/.."

EDGE="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
PORT=5511
PROFILE="$(mktemp -d)"
python -m http.server "$PORT" --bind 127.0.0.1 --directory widget >/dev/null 2>&1 &
SERVER=$!
trap 'kill $SERVER 2>/dev/null; rm -rf "$PROFILE"' EXIT
sleep 1

shot() { # name width height query
  local lang=$1 name=$2 w=$3 h=$4 q=$5
  mkdir -p "docs/images/$lang"
  "$EDGE" --headless=new --disable-gpu --hide-scrollbars --no-first-run \
    --user-data-dir="$PROFILE" --force-device-scale-factor=1 \
    --default-background-color=00000000 --force-prefers-reduced-motion --virtual-time-budget=15000 \
    --window-size="$w,$h" --screenshot="$(pwd -W)/docs/images/$lang/$name.png" \
    "http://127.0.0.1:$PORT/index.html?demo&lang=$lang$q" >/dev/null 2>&1
  echo "docs/images/$lang/$name.png"
}

for lang in en de; do
  shot "$lang" xl-overview      2536 696 "&tab=chat"
  shot "$lang" l-voice-chat     1688 696 "&tab=chat"
  shot "$lang" l-light-channels 1688 696 "&tab=channels&theme=light"
  shot "$lang" m-voice          840  696 ""
  shot "$lang" m-volume         840  696 "&popup=Robin"
  shot "$lang" m-direct         840  696 "&tab=channels&guild=dms"
  shot "$lang" m-private        840  696 "&tab=chat&private=1"
done
