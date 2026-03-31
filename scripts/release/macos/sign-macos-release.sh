#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
RELEASE_DIR="${1:-$ROOT_DIR/output/release/osx-arm64/PrintHub-osx-arm64}"
ARTIFACT_PATH="${2:-$ROOT_DIR/output/release/osx-arm64/PrintHub-osx-arm64.zip}"
APP_DIR="$RELEASE_DIR/Applications/PrintHub.app"
TRAY_APP_DIR="$RELEASE_DIR/Applications/PrintHub Tray.app"
IDENTITY="${PRINTHUB_CODESIGN_IDENTITY:-}"
ENTITLEMENTS="${PRINTHUB_CODESIGN_ENTITLEMENTS:-$ROOT_DIR/scripts/release/macos/PrintHub.entitlements}"

if [[ -z "$IDENTITY" ]]; then
  echo "Set PRINTHUB_CODESIGN_IDENTITY before signing." >&2
  exit 1
fi

if [[ ! -d "$APP_DIR" ]]; then
  echo "App bundle was not found: $APP_DIR" >&2
  exit 1
fi

codesign --force --deep --timestamp --options runtime --entitlements "$ENTITLEMENTS" --sign "$IDENTITY" "$APP_DIR"

if [[ -d "$TRAY_APP_DIR" ]]; then
  codesign --force --deep --timestamp --options runtime --sign "$IDENTITY" "$TRAY_APP_DIR"
fi

if command -v ditto >/dev/null 2>&1; then
  rm -f "$ARTIFACT_PATH"
  ditto -c -k --sequesterRsrc --keepParent "$RELEASE_DIR" "$ARTIFACT_PATH"
  shasum -a 256 "$ARTIFACT_PATH" > "$ARTIFACT_PATH.sha256"
fi

spctl -a -vv "$APP_DIR"

echo "macOS release signed."
echo "  App:      $APP_DIR"
if [[ -d "$TRAY_APP_DIR" ]]; then
  echo "  Tray:     $TRAY_APP_DIR"
fi
if [[ -f "$ARTIFACT_PATH" ]]; then
  echo "  Artifact: $ARTIFACT_PATH"
fi
