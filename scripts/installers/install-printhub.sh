#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

detect_platform() {
  case "$(uname -s)" in
    Darwin)
      printf '%s\n' "macos"
      ;;
    Linux)
      printf '%s\n' "linux"
      ;;
    *)
      echo "Unsupported host OS." >&2
      exit 1
      ;;
  esac
}

copy_directory() {
  local source_dir="$1"
  local target_dir="$2"

  rm -rf "$target_dir"
  mkdir -p "$target_dir"

  if command -v rsync >/dev/null 2>&1; then
    rsync -a --delete "$source_dir"/ "$target_dir"/
    return
  fi

  cp -R "$source_dir"/. "$target_dir"/
}

write_executable_file() {
  local target_file="$1"
  local script_body="$2"

  printf '%s\n' "$script_body" > "$target_file"
  chmod +x "$target_file"
}

install_macos() {
  local install_dir="${1:-${INSTALL_ROOT:-$HOME/Applications/PrintHub.app}}"
  local bundle_name
  bundle_name="$(basename "$install_dir")"
  local contents_dir="$install_dir/Contents"
  local resources_dir="$contents_dir/Resources"
  local macos_dir="$contents_dir/MacOS"
  local app_payload_dir="$resources_dir/app"
  local launchers_dir
  launchers_dir="$(cd "$(dirname "$install_dir")" && pwd)"
  local tray_install_dir="$launchers_dir/PrintHub Tray.app"

  rm -rf "$install_dir"
  mkdir -p "$resources_dir" "$macos_dir"
  copy_directory "$SCRIPT_DIR" "$app_payload_dir"

  if [[ -d "$SCRIPT_DIR/PrintHub Tray.app" ]]; then
    copy_directory "$SCRIPT_DIR/PrintHub Tray.app" "$tray_install_dir"
  fi

  cat > "$contents_dir/Info.plist" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>PrintHub</string>
  <key>CFBundleIdentifier</key>
  <string>local.printhub.app</string>
  <key>CFBundleName</key>
  <string>PrintHub</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
</dict>
</plist>
EOF

  write_executable_file "$macos_dir/PrintHub" '#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="$SCRIPT_DIR/../Resources/app"
exec "$APP_DIR/run-printhub.sh" "$@"'

  write_executable_file "$launchers_dir/Stop PrintHub.command" '#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="$SCRIPT_DIR/'"$bundle_name"'/Contents/Resources/app"
exec "$APP_DIR/stop-printhub.sh" "$@"'

  if [[ -d "$tray_install_dir" ]]; then
    write_executable_file "$launchers_dir/Open PrintHub Tray.command" '#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
open "$SCRIPT_DIR/PrintHub Tray.app"'
  fi

  write_executable_file "$launchers_dir/Uninstall PrintHub.command" '#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="$SCRIPT_DIR/'"$bundle_name"'/Contents/Resources/app"
exec "$APP_DIR/uninstall-printhub.sh" "$SCRIPT_DIR/'"$bundle_name"'"'

  echo "PrintHub was installed for the current user."
  echo "  App bundle:   $install_dir"
  if [[ -d "$tray_install_dir" ]]; then
    echo "  Tray app:     $tray_install_dir"
    echo "  Tray opener:  $launchers_dir/Open PrintHub Tray.command"
  fi
  echo "  Stop script:  $launchers_dir/Stop PrintHub.command"
  echo "  Uninstall:    $launchers_dir/Uninstall PrintHub.command"
  echo ""
  echo "Open PrintHub by double-clicking $bundle_name."
}

install_linux() {
  local install_dir="${1:-${INSTALL_ROOT:-$HOME/.local/opt/PrintHub}}"
  local applications_dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
  local launcher_path="$install_dir/PrintHub"
  local stop_launcher_path="$install_dir/PrintHub-stop"

  copy_directory "$SCRIPT_DIR" "$install_dir"

  write_executable_file "$launcher_path" '#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$SCRIPT_DIR/run-printhub.sh" "$@"'

  write_executable_file "$stop_launcher_path" '#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$SCRIPT_DIR/stop-printhub.sh" "$@"'

  mkdir -p "$applications_dir"

  cat > "$applications_dir/printhub.desktop" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=PrintHub
Comment=Local print gateway
Exec=$launcher_path
Terminal=false
Categories=Office;Utility;
EOF

  cat > "$applications_dir/printhub-stop.desktop" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=Stop PrintHub
Comment=Stop local PrintHub service
Exec=$stop_launcher_path
Terminal=false
Categories=Office;Utility;
EOF

  echo "PrintHub was installed for the current user."
  echo "  App files:      $install_dir"
  echo "  Desktop entry:  $applications_dir/printhub.desktop"
}

platform="$(detect_platform)"

case "$platform" in
  macos)
    install_macos "${1:-}"
    ;;
  linux)
    install_linux "${1:-}"
    ;;
esac
