#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="$ROOT_DIR/src/PrintHub.Api/PrintHub.Api.csproj"

read_version() {
  if [[ -n "${PRINTHUB_VERSION:-}" ]]; then
    printf '%s\n' "$PRINTHUB_VERSION"
    return
  fi

  if [[ -f "$ROOT_DIR/VERSION" ]]; then
    tr -d '\r' < "$ROOT_DIR/VERSION" | head -n 1
    return
  fi

  printf '%s\n' "0.1.0"
}

install_support_scripts() {
  cp "$ROOT_DIR/VERSION" "$OUTPUT_DIR/VERSION"
  cp "$ROOT_DIR/scripts/launcher/run-printhub.sh" "$OUTPUT_DIR/run-printhub.sh"
  cp "$ROOT_DIR/scripts/launcher/stop-printhub.sh" "$OUTPUT_DIR/stop-printhub.sh"
  cp "$ROOT_DIR/scripts/launcher/open-printhub-settings.sh" "$OUTPUT_DIR/open-printhub-settings.sh"
  cp "$ROOT_DIR/scripts/launcher/open-printhub-printers.sh" "$OUTPUT_DIR/open-printhub-printers.sh"
  cp "$ROOT_DIR/scripts/launcher/run-printhub.ps1" "$OUTPUT_DIR/run-printhub.ps1"
  cp "$ROOT_DIR/scripts/launcher/stop-printhub.ps1" "$OUTPUT_DIR/stop-printhub.ps1"
  cp "$ROOT_DIR/scripts/launcher/open-printhub-settings.ps1" "$OUTPUT_DIR/open-printhub-settings.ps1"
  cp "$ROOT_DIR/scripts/launcher/open-printhub-printers.ps1" "$OUTPUT_DIR/open-printhub-printers.ps1"
  cp "$ROOT_DIR/scripts/launcher/run-printhub.command" "$OUTPUT_DIR/run-printhub.command"
  cp "$ROOT_DIR/scripts/launcher/stop-printhub.command" "$OUTPUT_DIR/stop-printhub.command"
  cp "$ROOT_DIR/scripts/launcher/open-printhub-tray.command" "$OUTPUT_DIR/open-printhub-tray.command"
  cp "$ROOT_DIR/scripts/launcher/open-printhub-settings.command" "$OUTPUT_DIR/open-printhub-settings.command"
  cp "$ROOT_DIR/scripts/launcher/open-printhub-printers.command" "$OUTPUT_DIR/open-printhub-printers.command"
  cp "$ROOT_DIR/scripts/installers/install-printhub.sh" "$OUTPUT_DIR/install-printhub.sh"
  cp "$ROOT_DIR/scripts/installers/uninstall-printhub.sh" "$OUTPUT_DIR/uninstall-printhub.sh"
  cp "$ROOT_DIR/scripts/installers/install-printhub.ps1" "$OUTPUT_DIR/install-printhub.ps1"
  cp "$ROOT_DIR/scripts/installers/uninstall-printhub.ps1" "$OUTPUT_DIR/uninstall-printhub.ps1"
  cp "$ROOT_DIR/scripts/installers/install-printhub.command" "$OUTPUT_DIR/install-printhub.command"
  cp "$ROOT_DIR/scripts/installers/uninstall-printhub.command" "$OUTPUT_DIR/uninstall-printhub.command"
  chmod +x \
    "$OUTPUT_DIR/run-printhub.sh" \
    "$OUTPUT_DIR/stop-printhub.sh" \
    "$OUTPUT_DIR/open-printhub-settings.sh" \
    "$OUTPUT_DIR/open-printhub-printers.sh" \
    "$OUTPUT_DIR/run-printhub.command" \
    "$OUTPUT_DIR/stop-printhub.command" \
    "$OUTPUT_DIR/open-printhub-tray.command" \
    "$OUTPUT_DIR/open-printhub-settings.command" \
    "$OUTPUT_DIR/open-printhub-printers.command" \
    "$OUTPUT_DIR/install-printhub.sh" \
    "$OUTPUT_DIR/uninstall-printhub.sh" \
    "$OUTPUT_DIR/install-printhub.command" \
    "$OUTPUT_DIR/uninstall-printhub.command"
}

build_macos_tray_if_available() {
  case "$RUNTIME" in
    osx-*)
      ;;
    *)
      return
      ;;
  esac

  if ! command -v swiftc >/dev/null 2>&1; then
    echo "Skipping macOS tray build: swiftc is not available."
    return
  fi

  PRINTHUB_VERSION="$APP_VERSION" bash "$ROOT_DIR/scripts/tray/macos/build-printhub-tray.sh" "$OUTPUT_DIR"
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
APP_VERSION="$(read_version)"

echo "Publishing PrintHub"
echo "  Version:        $APP_VERSION"
echo "  Runtime:        $RUNTIME"
echo "  Configuration:  $CONFIGURATION"
echo "  Self-contained: $SELF_CONTAINED"
echo "  Output:         $OUTPUT_DIR"

dotnet publish "$PROJECT_PATH" \
  -c "$CONFIGURATION" \
  -r "$RUNTIME" \
  --self-contained "$SELF_CONTAINED" \
  -o "$OUTPUT_DIR"

install_support_scripts
build_macos_tray_if_available

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

Install for the current user with:
  $OUTPUT_DIR/install-printhub.sh

On macOS you can also double-click:
  $OUTPUT_DIR/install-printhub.command

Direct panel launchers:
  $OUTPUT_DIR/open-printhub-settings.sh
  $OUTPUT_DIR/open-printhub-printers.sh
  $OUTPUT_DIR/open-printhub-settings.command
  $OUTPUT_DIR/open-printhub-printers.command

If the tray helper was built, the publish folder also contains:
  $OUTPUT_DIR/PrintHub Tray.app

Open the tray helper directly with:
  $OUTPUT_DIR/open-printhub-tray.command

Build a distributable release package with:
  $ROOT_DIR/scripts/release/build-release.sh $RUNTIME
EOF
