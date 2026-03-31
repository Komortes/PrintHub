#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
RELEASE_DIR="${1:-$ROOT_DIR/output/release/osx-arm64/PrintHub-osx-arm64}"
ARTIFACT_PATH="${2:-$ROOT_DIR/output/release/osx-arm64/PrintHub-osx-arm64.zip}"
APP_DIR="$RELEASE_DIR/Applications/PrintHub.app"
TRAY_APP_DIR="$RELEASE_DIR/Applications/PrintHub Tray.app"
PROFILE="${PRINTHUB_NOTARY_PROFILE:-}"
TEAM_ID="${PRINTHUB_NOTARY_TEAM_ID:-}"

if [[ -z "$PROFILE" ]]; then
  echo "Set PRINTHUB_NOTARY_PROFILE before notarization." >&2
  exit 1
fi

if [[ ! -f "$ARTIFACT_PATH" ]]; then
  echo "Signed artifact was not found: $ARTIFACT_PATH" >&2
  exit 1
fi

SUBMIT_ARGS=(submit "$ARTIFACT_PATH" --keychain-profile "$PROFILE" --wait)
if [[ -n "$TEAM_ID" ]]; then
  SUBMIT_ARGS+=(--team-id "$TEAM_ID")
fi

xcrun notarytool "${SUBMIT_ARGS[@]}"
xcrun stapler staple "$APP_DIR"

if [[ -d "$TRAY_APP_DIR" ]]; then
  xcrun stapler staple "$TRAY_APP_DIR"
fi

spctl -a -vv "$APP_DIR"

echo "macOS release notarized."
echo "  App:      $APP_DIR"
echo "  Artifact: $ARTIFACT_PATH"
