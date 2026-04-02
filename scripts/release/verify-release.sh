#!/usr/bin/env bash
set -euo pipefail

detect_platform() {
  case "$(uname -s)" in
    Darwin) printf '%s\n' "macos" ;;
    Linux) printf '%s\n' "linux" ;;
    *)
      echo "Unsupported host OS." >&2
      exit 1
      ;;
  esac
}

resolve_source_dir() {
  local base_dir="$1"

  if [[ -x "$base_dir/install-printhub.sh" ]]; then
    printf '%s\n' "$base_dir"
    return
  fi

  if [[ -x "$base_dir/payload/install-printhub.sh" ]]; then
    printf '%s\n' "$base_dir/payload"
    return
  fi

  echo "Could not find install-printhub.sh under: $base_dir" >&2
  exit 1
}

probe_health() {
  curl -fsS "$APP_URL/health" >/dev/null 2>&1
}

wait_for_down() {
  for ((attempt = 1; attempt <= 20; attempt++)); do
    if ! probe_health; then
      return 0
    fi

    sleep 0.5
  done

  return 1
}

cleanup() {
  local exit_code=$?

  if [[ "${PRINTHUB_VERIFY_KEEP:-false}" == "true" ]] || [[ $exit_code -ne 0 ]]; then
    echo "Verify workspace kept at: $VERIFY_ROOT"
    return
  fi

  rm -rf "$VERIFY_ROOT"
}

run_macos_verification() {
  INSTALL_DIR="$VERIFY_ROOT/Applications/PrintHub.app"
  mkdir -p "$(dirname "$INSTALL_DIR")"
  bash "$SOURCE_DIR/install-printhub.sh" "$INSTALL_DIR" >/tmp/printhub-verify-install.log

  [[ -d "$INSTALL_DIR" ]] || { echo "PrintHub.app was not installed." >&2; exit 1; }
  [[ -f "$VERIFY_ROOT/Applications/Open PrintHub Settings.command" ]] || { echo "Settings launcher was not created." >&2; exit 1; }
  [[ -f "$VERIFY_ROOT/Applications/Open PrintHub Printers.command" ]] || { echo "Printers launcher was not created." >&2; exit 1; }

  APP_RUN_DIR="$INSTALL_DIR/Contents/Resources/app"
}

run_linux_verification() {
  INSTALL_DIR="$VERIFY_ROOT/opt/PrintHub"
  mkdir -p "$(dirname "$INSTALL_DIR")"
  bash "$SOURCE_DIR/install-printhub.sh" "$INSTALL_DIR" >/tmp/printhub-verify-install.log

  [[ -d "$INSTALL_DIR" ]] || { echo "PrintHub install directory was not created." >&2; exit 1; }
  [[ -f "$VERIFY_ROOT/share/applications/printhub-settings.desktop" ]] || { echo "Settings desktop entry was not created." >&2; exit 1; }
  [[ -f "$VERIFY_ROOT/share/applications/printhub-printers.desktop" ]] || { echo "Printers desktop entry was not created." >&2; exit 1; }

  APP_RUN_DIR="$INSTALL_DIR"
}

PLATFORM="$(detect_platform)"
SOURCE_INPUT="${1:-}"

if [[ -z "$SOURCE_INPUT" ]]; then
  echo "Usage: $0 <publish-dir-or-release-stage-dir>" >&2
  exit 1
fi

SOURCE_DIR="$(resolve_source_dir "$SOURCE_INPUT")"
VERIFY_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/printhub-release-verify.XXXXXX")"
trap cleanup EXIT

export PRINTHUB_HOME="$VERIFY_ROOT/home"
export XDG_DATA_HOME="$VERIFY_ROOT/share"
export PRINTHUB_PORT="${PRINTHUB_PORT:-$((5400 + RANDOM % 400))}"
export PRINTHUB_OPEN_BROWSER=false
APP_URL="http://127.0.0.1:$PRINTHUB_PORT"

case "$PLATFORM" in
  macos)
    run_macos_verification
    ;;
  linux)
    run_linux_verification
    ;;
esac

bash "$APP_RUN_DIR/run-printhub.sh" >/tmp/printhub-verify-run.log

if ! probe_health; then
  echo "PrintHub did not become healthy at $APP_URL" >&2
  exit 1
fi

bash "$APP_RUN_DIR/stop-printhub.sh" >/tmp/printhub-verify-stop.log

if ! wait_for_down; then
  echo "PrintHub did not stop cleanly at $APP_URL" >&2
  exit 1
fi

cat <<EOF
Release verification succeeded.

Source:        $SOURCE_DIR
Platform:      $PLATFORM
Install root:  $VERIFY_ROOT
Health URL:    $APP_URL/health
EOF
