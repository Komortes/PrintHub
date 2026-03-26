#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
OUTPUT_DIR="${1:?Usage: build-printhub-tray.sh <publish-output-dir>}"
APP_DIR="$OUTPUT_DIR/PrintHub Tray.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
SRC_FILE="$ROOT_DIR/scripts/tray/macos/PrintHubTray.swift"
MODULE_CACHE_DIR="${TMPDIR:-/tmp}/printhub-tray-module-cache"

rm -rf "$APP_DIR"
mkdir -p "$MACOS_DIR"
mkdir -p "$MODULE_CACHE_DIR"

cat > "$CONTENTS_DIR/Info.plist" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>PrintHubTray</string>
  <key>CFBundleIdentifier</key>
  <string>local.printhub.tray</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>PrintHub Tray</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>LSUIElement</key>
  <true/>
</dict>
</plist>
EOF

swiftc "$SRC_FILE" \
  -o "$MACOS_DIR/PrintHubTray" \
  -parse-as-library \
  -module-cache-path "$MODULE_CACHE_DIR" \
  -framework Cocoa

chmod +x "$MACOS_DIR/PrintHubTray"

echo "Built macOS tray helper:"
echo "  $APP_DIR"
