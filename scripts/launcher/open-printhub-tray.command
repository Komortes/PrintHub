#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TRAY_APP="$SCRIPT_DIR/PrintHub Tray.app"
MAIN_APP="$SCRIPT_DIR/PrintHub.app"

if [[ -d "$TRAY_APP" ]]; then
  open "$TRAY_APP"
  exit 0
fi

if [[ -d "$MAIN_APP" ]]; then
  open "$MAIN_APP"
  exit 0
fi

exec "$SCRIPT_DIR/run-printhub.command" "$@"
