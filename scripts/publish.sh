#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="$ROOT_DIR/src/PrintHub.Api/PrintHub.Api.csproj"

install_launchers() {
  cp "$ROOT_DIR/scripts/launcher/run-printhub.sh" "$OUTPUT_DIR/run-printhub.sh"
  cp "$ROOT_DIR/scripts/launcher/stop-printhub.sh" "$OUTPUT_DIR/stop-printhub.sh"
  cp "$ROOT_DIR/scripts/launcher/run-printhub.ps1" "$OUTPUT_DIR/run-printhub.ps1"
  cp "$ROOT_DIR/scripts/launcher/stop-printhub.ps1" "$OUTPUT_DIR/stop-printhub.ps1"
  chmod +x "$OUTPUT_DIR/run-printhub.sh" "$OUTPUT_DIR/stop-printhub.sh"
}

detect_runtime() {
  local os arch
  os="$(uname -s)"
  arch="$(uname -m)"

  case "$os" in
    Darwin) os="osx" ;;
    Linux) os="linux" ;;
    *)
      echo "Unsupported host OS: $os" >&2
      exit 1
      ;;
  esac

  case "$arch" in
    arm64|aarch64) arch="arm64" ;;
    x86_64) arch="x64" ;;
    *)
      echo "Unsupported host architecture: $arch" >&2
      exit 1
      ;;
  esac

  printf '%s-%s\n' "$os" "$arch"
}

CONFIGURATION="${CONFIGURATION:-Release}"
RUNTIME="${1:-$(detect_runtime)}"
OUTPUT_DIR="${2:-$ROOT_DIR/output/publish/$RUNTIME}"
SELF_CONTAINED="${SELF_CONTAINED:-true}"

echo "Publishing PrintHub"
echo "  Runtime:        $RUNTIME"
echo "  Configuration:  $CONFIGURATION"
echo "  Self-contained: $SELF_CONTAINED"
echo "  Output:         $OUTPUT_DIR"

dotnet publish "$PROJECT_PATH" \
  -c "$CONFIGURATION" \
  -r "$RUNTIME" \
  --self-contained "$SELF_CONTAINED" \
  -o "$OUTPUT_DIR"

install_launchers

cat <<EOF

Publish completed.

This publish is self-contained by default, so the target machine does not need
the .NET runtime installed.

Default runtime data root:
  macOS:   ~/Library/Application Support/PrintHub
  Linux:   ~/.local/share/PrintHub
  Windows: %LOCALAPPDATA%\\PrintHub

Override this location with:
  PRINTHUB_HOME=/absolute/path

Run the published app with:
  $OUTPUT_DIR/run-printhub.sh

Stop the background service with:
  $OUTPUT_DIR/stop-printhub.sh
EOF
