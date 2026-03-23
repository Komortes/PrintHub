#!/usr/bin/env bash
set -euo pipefail

resolve_default_home() {
  case "$(uname -s)" in
    Darwin)
      printf '%s\n' "$HOME/Library/Application Support/PrintHub"
      ;;
    Linux)
      printf '%s\n' "${XDG_DATA_HOME:-$HOME/.local/share}/PrintHub"
      ;;
    *)
      printf '%s\n' "${PRINTHUB_HOME:-$HOME/.printhub}"
      ;;
  esac
}

export PRINTHUB_HOME="${PRINTHUB_HOME:-$(resolve_default_home)}"
PID_FILE="$PRINTHUB_HOME/runtime/printhub.pid"
DEFAULT_PORT="5051"

resolve_port() {
  if [[ -n "${PRINTHUB_PORT:-}" ]]; then
    printf '%s\n' "$PRINTHUB_PORT"
    return
  fi

  local settings_file="$PRINTHUB_HOME/data/settings.json"
  if [[ -f "$settings_file" ]]; then
    local line
    line="$(grep -E '"port"' "$settings_file" | head -n 1 || true)"
    if [[ "$line" =~ ([0-9]{1,5}) ]]; then
      printf '%s\n' "${BASH_REMATCH[1]}"
      return
    fi
  fi

  printf '%s\n' "$DEFAULT_PORT"
}

find_listening_pid() {
  if ! command -v lsof >/dev/null 2>&1; then
    return 1
  fi

  local port
  port="$(resolve_port)"
  lsof -tiTCP:"$port" -sTCP:LISTEN 2>/dev/null | head -n 1
}

if [[ ! -f "$PID_FILE" ]]; then
  echo "No PrintHub PID file was found at $PID_FILE"
  exit 0
fi

pid="$(cat "$PID_FILE" 2>/dev/null || true)"

if [[ -z "$pid" ]]; then
  rm -f "$PID_FILE"
  echo "Removed empty PID file."
  exit 0
fi

if ! kill -0 "$pid" >/dev/null 2>&1; then
  listening_pid="$(find_listening_pid || true)"

  if [[ -n "$listening_pid" ]]; then
    pid="$listening_pid"
    echo "$pid" > "$PID_FILE"
  else
    rm -f "$PID_FILE"
    echo "Removed stale PID file for process $pid."
    exit 0
  fi
fi

if ! kill -0 "$pid" >/dev/null 2>&1; then
  rm -f "$PID_FILE"
  echo "Removed stale PID file for process $pid."
  exit 0
fi

kill "$pid" >/dev/null 2>&1 || true

for ((attempt = 1; attempt <= 10; attempt++)); do
  if ! kill -0 "$pid" >/dev/null 2>&1; then
    rm -f "$PID_FILE"
    echo "Stopped PrintHub process $pid."
    exit 0
  fi

  sleep 0.5
done

kill -9 "$pid" >/dev/null 2>&1 || true
rm -f "$PID_FILE"
echo "Force-stopped PrintHub process $pid."
