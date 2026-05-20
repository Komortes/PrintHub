#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_EXE="$SCRIPT_DIR/PrintHub.Api"
APP_DLL="$SCRIPT_DIR/PrintHub.Api.dll"
DEFAULT_PORT="5051"
DEFAULT_BIND_HOST="127.0.0.1"
START_TIMEOUT_SECONDS="${PRINTHUB_START_TIMEOUT_SECONDS:-30}"
OPEN_BROWSER="${PRINTHUB_OPEN_BROWSER:-true}"
OPEN_URL_SUFFIX="${PRINTHUB_OPEN_URL_SUFFIX:-}"

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

ensure_home() {
  export PRINTHUB_HOME="${PRINTHUB_HOME:-$(resolve_default_home)}"
  mkdir -p "$PRINTHUB_HOME/runtime"
}

read_settings_string() {
  local key="$1"
  local settings_file="$PRINTHUB_HOME/data/settings.json"
  local line

  if [[ ! -f "$settings_file" ]]; then
    return
  fi

  line="$(grep -E "\"$key\"" "$settings_file" | head -n 1 || true)"
  if [[ "$line" =~ \"$key\"[[:space:]]*:[[:space:]]*\"([^\"]*)\" ]]; then
    printf '%s\n' "${BASH_REMATCH[1]}"
  fi
}

resolve_port() {
  if [[ -n "${PRINTHUB_PORT:-}" ]]; then
    printf '%s\n' "$PRINTHUB_PORT"
    return
  fi

  local settings_file="$PRINTHUB_HOME/data/settings.json"
  local line

  if [[ -f "$settings_file" ]]; then
    line="$(grep -E '"port"' "$settings_file" | head -n 1 || true)"
    if [[ "$line" =~ ([0-9]{1,5}) ]]; then
      printf '%s\n' "${BASH_REMATCH[1]}"
      return
    fi
  fi

  printf '%s\n' "$DEFAULT_PORT"
}

resolve_bind_host() {
  if [[ -n "${PRINTHUB_HOST:-}" ]]; then
    printf '%s\n' "$PRINTHUB_HOST"
    return
  fi

  local saved_bind_host
  saved_bind_host="$(read_settings_string "bindHost")"
  if [[ -n "$saved_bind_host" ]]; then
    printf '%s\n' "$saved_bind_host"
    return
  fi

  printf '%s\n' "$DEFAULT_BIND_HOST"
}

format_host_for_url() {
  local host="$1"

  if [[ "$host" == \[*\] ]]; then
    printf '%s\n' "$host"
    return
  fi

  if [[ "$host" == *:* && "$host" != "*" && "$host" != "+" && "$host" != "localhost" ]]; then
    printf '[%s]\n' "$host"
    return
  fi

  printf '%s\n' "$host"
}

resolve_listen_url() {
  if [[ -n "${PRINTHUB_URL:-}" ]]; then
    printf '%s\n' "$PRINTHUB_URL"
    return
  fi

  local host port
  host="$(resolve_bind_host)"
  port="$(resolve_port)"
  printf 'http://%s:%s\n' "$(format_host_for_url "$host")" "$port"
}

resolve_access_host() {
  local bind_host="$1"

  case "$bind_host" in
    0.0.0.0|"*"|"+")
      printf '127.0.0.1\n'
      ;;
    ::|[::])
      printf '::1\n'
      ;;
    *)
      printf '%s\n' "$bind_host"
      ;;
  esac
}

resolve_access_url() {
  if [[ -n "${PRINTHUB_ACCESS_URL:-}" ]]; then
    printf '%s\n' "$PRINTHUB_ACCESS_URL"
    return
  fi

  if [[ -n "${PRINTHUB_URL:-}" ]]; then
    printf '%s\n' "$PRINTHUB_URL"
    return
  fi

  local host port
  host="$(resolve_access_host "$(resolve_bind_host)")"
  port="$(resolve_port)"
  printf 'http://%s:%s\n' "$(format_host_for_url "$host")" "$port"
}

resolve_port_from_url() {
  local url="$1"

  if [[ "$url" =~ :([0-9]{1,5})$ ]]; then
    printf '%s\n' "${BASH_REMATCH[1]}"
    return
  fi

  printf '%s\n' "$DEFAULT_PORT"
}

find_listening_pid() {
  if ! command -v lsof >/dev/null 2>&1; then
    return 1
  fi

  lsof -tiTCP:"$APP_PORT" -sTCP:LISTEN 2>/dev/null | head -n 1
}

refresh_pid_file() {
  local listening_pid
  listening_pid="$(find_listening_pid || true)"

  if [[ -n "$listening_pid" ]]; then
    echo "$listening_pid" > "$PID_FILE"
    return 0
  fi

  return 1
}

can_probe_health() {
  command -v curl >/dev/null 2>&1
}

is_healthy() {
  if ! can_probe_health; then
    return 1
  fi

  curl -fsS "$ACCESS_URL/health" >/dev/null 2>&1
}

wait_for_health() {
  if ! can_probe_health; then
    sleep 2
    return 0
  fi

  local attempt_count
  attempt_count=$(( START_TIMEOUT_SECONDS * 2 ))

  for ((attempt = 1; attempt <= attempt_count; attempt++)); do
    if is_healthy; then
      return 0
    fi

    sleep 0.5
  done

  return 1
}

open_browser() {
  if [[ "$OPEN_BROWSER" == "false" ]]; then
    return
  fi

  local target_url="$ACCESS_URL$OPEN_URL_SUFFIX"

  if command -v open >/dev/null 2>&1; then
    open "$target_url" >/dev/null 2>&1 &
    return
  fi

  if command -v xdg-open >/dev/null 2>&1; then
    xdg-open "$target_url" >/dev/null 2>&1 &
  fi
}

start_process() {
  local launcher_log="$PRINTHUB_HOME/runtime/launcher.log"
  local env_name="${ASPNETCORE_ENVIRONMENT:-Production}"

  if [[ -x "$APP_EXE" ]]; then
    ASPNETCORE_URLS="$LISTEN_URL" ASPNETCORE_ENVIRONMENT="$env_name" PRINTHUB_HOME="$PRINTHUB_HOME" \
      nohup "$APP_EXE" >>"$launcher_log" 2>&1 &
  elif [[ -f "$APP_DLL" ]]; then
    ASPNETCORE_URLS="$LISTEN_URL" ASPNETCORE_ENVIRONMENT="$env_name" PRINTHUB_HOME="$PRINTHUB_HOME" \
      nohup dotnet "$APP_DLL" >>"$launcher_log" 2>&1 &
  else
    echo "PrintHub executable was not found in $SCRIPT_DIR" >&2
    exit 1
  fi

  echo "$!" > "$PID_FILE"
}

ensure_home
LISTEN_URL="$(resolve_listen_url)"
ACCESS_URL="$(resolve_access_url)"
APP_PORT="$(resolve_port_from_url "$LISTEN_URL")"
PID_FILE="$PRINTHUB_HOME/runtime/printhub.pid"

if is_healthy; then
  echo "PrintHub is already running at $ACCESS_URL"
  if [[ "$LISTEN_URL" != "$ACCESS_URL" ]]; then
    echo "Listening on $LISTEN_URL"
  fi
  open_browser
  exit 0
fi

if [[ -f "$PID_FILE" ]]; then
  existing_pid="$(cat "$PID_FILE" 2>/dev/null || true)"
  if [[ -n "$existing_pid" ]] && kill -0 "$existing_pid" >/dev/null 2>&1; then
    echo "Existing PrintHub process found ($existing_pid). Waiting for health endpoint..."
    if wait_for_health; then
      echo "PrintHub is available at $ACCESS_URL"
      if [[ "$LISTEN_URL" != "$ACCESS_URL" ]]; then
        echo "Listening on $LISTEN_URL"
      fi
      open_browser
      exit 0
    fi
  fi

  rm -f "$PID_FILE"
fi

start_process

if ! wait_for_health; then
  echo "PrintHub started but did not become healthy at $ACCESS_URL within ${START_TIMEOUT_SECONDS}s." >&2
  echo "Check launcher log: $PRINTHUB_HOME/runtime/launcher.log" >&2
  exit 1
fi

refresh_pid_file || true

echo "PrintHub is running at $ACCESS_URL"
if [[ "$LISTEN_URL" != "$ACCESS_URL" ]]; then
  echo "Listening on $LISTEN_URL"
fi
echo "PID file: $PID_FILE"
echo "Runtime home: $PRINTHUB_HOME"

open_browser
