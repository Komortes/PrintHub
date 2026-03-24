#!/usr/bin/env bash
set -euo pipefail

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

uninstall_macos() {
  local install_dir="${1:-${INSTALL_ROOT:-$HOME/Applications/PrintHub.app}}"
  local launchers_dir
  launchers_dir="$(cd "$(dirname "$install_dir")" && pwd)"

  rm -rf "$install_dir"
  rm -f "$launchers_dir/Stop PrintHub.command"
  rm -f "$launchers_dir/Uninstall PrintHub.command"

  echo "PrintHub was removed from:"
  echo "  $install_dir"
}

uninstall_linux() {
  local install_dir="${1:-${INSTALL_ROOT:-$HOME/.local/opt/PrintHub}}"
  local applications_dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"

  rm -rf "$install_dir"
  rm -f "$applications_dir/printhub.desktop" "$applications_dir/printhub-stop.desktop"

  echo "PrintHub was removed from:"
  echo "  $install_dir"
}

platform="$(detect_platform)"

case "$platform" in
  macos)
    uninstall_macos "${1:-}"
    ;;
  linux)
    uninstall_linux "${1:-}"
    ;;
esac
