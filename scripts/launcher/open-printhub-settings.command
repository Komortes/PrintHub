#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export PRINTHUB_OPEN_URL_SUFFIX="#settings"
exec "$SCRIPT_DIR/run-printhub.sh" "$@"
