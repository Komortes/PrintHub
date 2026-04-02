#!/usr/bin/env bash
set -euo pipefail

print_tail() {
  local label="$1"
  local path="$2"
  local lines="${3:-40}"

  if [[ ! -f "$path" ]]; then
    return
  fi

  echo
  echo "---- $label ($path) ----"
  tail -n "$lines" "$path" || true
}

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

capture_runtime_artifacts() {
  mkdir -p "$VERIFY_ARTIFACTS_DIR"

  if probe_health; then
    curl -fsS "$APP_URL/health" >"$VERIFY_ARTIFACTS_DIR/health.json" || true
    curl -fsS "$APP_URL/printers/diagnostics" >"$VERIFY_ARTIFACTS_DIR/printers-diagnostics.json" || true
    curl -fsS "$APP_URL/diagnostics/report" >"$VERIFY_ARTIFACTS_DIR/diagnostics-report.txt" || true
    curl -fsS "$APP_URL/diagnostics/support-bundle" >"$VERIFY_ARTIFACTS_DIR/support-bundle.zip" || true
  fi
}

print_failure_context() {
  echo
  echo "Release verification failed."
  echo "Verify workspace:  $VERIFY_ROOT"
  echo "Health URL:       $APP_URL/health"
  echo "App runtime home: $PRINTHUB_HOME"

  print_tail "install log" "$INSTALL_LOG"
  print_tail "run log" "$RUN_LOG"
  print_tail "stop log" "$STOP_LOG"
  print_tail "launcher log" "$PRINTHUB_HOME/runtime/launcher.log"
  print_tail "file log" "$PRINTHUB_HOME/data/logs/printhub.log"
}

run_logged_step() {
  local log_path="$1"
  shift

  if "$@" >"$log_path" 2>&1; then
    return 0
  fi

  return 1
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

  if [[ $exit_code -ne 0 ]]; then
    capture_runtime_artifacts || true
    print_failure_context
  fi

  if [[ "${PRINTHUB_VERIFY_KEEP:-false}" == "true" ]] || [[ $exit_code -ne 0 ]]; then
    echo "Verify workspace kept at: $VERIFY_ROOT"
    return
  fi

  rm -rf "$VERIFY_ROOT"
}

run_macos_verification() {
  INSTALL_DIR="$VERIFY_ROOT/Applications/PrintHub.app"
  mkdir -p "$(dirname "$INSTALL_DIR")"
  if ! run_logged_step "$INSTALL_LOG" bash "$SOURCE_DIR/install-printhub.sh" "$INSTALL_DIR"; then
    echo "install-printhub.sh failed." >&2
    exit 1
  fi

  [[ -d "$INSTALL_DIR" ]] || { echo "PrintHub.app was not installed." >&2; exit 1; }
  [[ -f "$VERIFY_ROOT/Applications/Open PrintHub Settings.command" ]] || { echo "Settings launcher was not created." >&2; exit 1; }
  [[ -f "$VERIFY_ROOT/Applications/Open PrintHub Printers.command" ]] || { echo "Printers launcher was not created." >&2; exit 1; }

  APP_RUN_DIR="$INSTALL_DIR/Contents/Resources/app"
}

run_linux_verification() {
  INSTALL_DIR="$VERIFY_ROOT/opt/PrintHub"
  mkdir -p "$(dirname "$INSTALL_DIR")"
  if ! run_logged_step "$INSTALL_LOG" bash "$SOURCE_DIR/install-printhub.sh" "$INSTALL_DIR"; then
    echo "install-printhub.sh failed." >&2
    exit 1
  fi

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
VERIFY_LOG_DIR="$VERIFY_ROOT/verify-logs"
VERIFY_ARTIFACTS_DIR="$VERIFY_ROOT/verify-artifacts"
mkdir -p "$VERIFY_LOG_DIR" "$VERIFY_ARTIFACTS_DIR"
INSTALL_LOG="$VERIFY_LOG_DIR/install.log"
RUN_LOG="$VERIFY_LOG_DIR/run.log"
STOP_LOG="$VERIFY_LOG_DIR/stop.log"
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

if ! run_logged_step "$RUN_LOG" bash "$APP_RUN_DIR/run-printhub.sh"; then
  echo "run-printhub.sh failed." >&2
  exit 1
fi

if ! probe_health; then
  echo "PrintHub did not become healthy at $APP_URL" >&2
  exit 1
fi

capture_runtime_artifacts

if ! run_logged_step "$STOP_LOG" bash "$APP_RUN_DIR/stop-printhub.sh"; then
  echo "stop-printhub.sh failed." >&2
  exit 1
fi

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
Artifacts:     $VERIFY_ARTIFACTS_DIR
EOF
