#!/usr/bin/env bash
set -euo pipefail

# Dev helper for MedipielControlPrecios:
# - DB + API via docker compose
# - Angular frontend via "npm start" on port 4300 (local, not docker)

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/docker-compose.yml"
FRONTEND_DIR="$ROOT_DIR/frontend"
PID_DIR="$ROOT_DIR/.pids"
LOG_DIR="$ROOT_DIR/.logs"
FRONTEND_PID_FILE="$PID_DIR/frontend-4300.pid"
FRONTEND_LOG_FILE="$LOG_DIR/frontend-4300.log"
FRONTEND_SCREEN_SESSION="medipiel-frontend-4300"

FRONTEND_PORT="4300"
API_URL="http://127.0.0.1:5000/health"
FRONTEND_URL="http://127.0.0.1:${FRONTEND_PORT}/"

usage() {
  cat <<EOF
Usage:
  $(basename "$0") up [--build]
  $(basename "$0") down
  $(basename "$0") status
  $(basename "$0") logs

Notes:
  - Frontend runs locally (not docker) on ${FRONTEND_URL}
  - API runs in docker on http://127.0.0.1:5000/
EOF
}

require_cmd() {
  local cmd="$1"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "ERROR: '$cmd' is required but not found in PATH." >&2
    exit 1
  fi
}

port_listen_pid() {
  local port="$1"
  # Extract PID from ss output like: users:(("ng serve",pid=35172,fd=32))
  ss -ltnp 2>/dev/null \
    | grep -E ":${port}\\b" \
    | sed -n 's/.*pid=\\([0-9][0-9]*\\),.*/\\1/p' \
    | head -n 1 \
    || true
}

wait_http_200() {
  local url="$1"
  local name="$2"
  local timeout_s="${3:-60}"

  local start
  start="$(date +%s)"
  while true; do
    local code="000"
    code="$(curl -s -o /dev/null -w "%{http_code}" "$url" || true)"
    if [[ "$code" == "200" ]]; then
      echo "OK: $name is up ($url)"
      return 0
    fi
    if (( "$(date +%s)" - start >= timeout_s )); then
      echo "ERROR: $name did not become ready within ${timeout_s}s (last HTTP $code): $url" >&2
      return 1
    fi
    sleep 1
  done
}

compose_up() {
  local build="${1:-false}"

  require_cmd docker
  require_cmd curl

  if [[ ! -f "$COMPOSE_FILE" ]]; then
    echo "ERROR: docker compose file not found: $COMPOSE_FILE" >&2
    exit 1
  fi

  local extra=()
  if [[ "$build" == "true" ]]; then
    extra+=(--build)
  fi

  echo "Starting docker services (db, backend)..."
  (cd "$ROOT_DIR" && docker compose -f "$COMPOSE_FILE" up -d "${extra[@]}" db backend)

  wait_http_200 "$API_URL" "API" 90
}

frontend_up() {
  require_cmd npm
  require_cmd ss
  require_cmd curl
  require_cmd screen

  mkdir -p "$PID_DIR" "$LOG_DIR"

  local existing_pid=""
  existing_pid="$(port_listen_pid "$FRONTEND_PORT")"
  if [[ -n "$existing_pid" ]]; then
    echo "Frontend already listening on port ${FRONTEND_PORT} (pid ${existing_pid})."
    echo "URL: $FRONTEND_URL"
    return 0
  fi

  # Clean stale pid file if present
  if [[ -f "$FRONTEND_PID_FILE" ]]; then
    local pid
    pid="$(cat "$FRONTEND_PID_FILE" || true)"
    if [[ -n "${pid:-}" ]] && kill -0 "$pid" >/dev/null 2>&1; then
      echo "Frontend appears running (pid $pid) but port ${FRONTEND_PORT} is not listening yet. Leaving it."
      return 0
    fi
    rm -f "$FRONTEND_PID_FILE"
  fi

  if [[ ! -d "$FRONTEND_DIR" ]]; then
    echo "ERROR: frontend directory not found: $FRONTEND_DIR" >&2
    exit 1
  fi

  echo "Starting frontend (Angular) on port ${FRONTEND_PORT}..."
  # Run detached in a screen session so it survives terminal closes.
  screen -dmS "$FRONTEND_SCREEN_SESSION" bash -lc \
    "cd \"$FRONTEND_DIR\" && npm start -- --port \"$FRONTEND_PORT\" --host 0.0.0.0 >\"$FRONTEND_LOG_FILE\" 2>&1"

  wait_http_200 "$FRONTEND_URL" "Frontend" 120
  echo "Frontend logs: $FRONTEND_LOG_FILE"
  echo "Reattach (optional): screen -r $FRONTEND_SCREEN_SESSION"
}

frontend_down() {
  require_cmd ss
  require_cmd screen

  local pid=""
  if [[ -f "$FRONTEND_PID_FILE" ]]; then
    pid="$(cat "$FRONTEND_PID_FILE" || true)"
  fi

  if screen -ls 2>/dev/null | grep -qE "\\.${FRONTEND_SCREEN_SESSION}[[:space:]]"; then
    echo "Stopping frontend screen session: $FRONTEND_SCREEN_SESSION"
    screen -S "$FRONTEND_SCREEN_SESSION" -X quit || true
    sleep 1
  fi

  if [[ -z "${pid:-}" ]]; then
    pid="$(port_listen_pid "$FRONTEND_PORT")"
  fi

  if [[ -n "${pid:-}" ]] && kill -0 "$pid" >/dev/null 2>&1; then
    echo "Stopping frontend (pid $pid)..."
    kill "$pid" || true
    # Give it a moment; then force kill if needed.
    sleep 2
    if kill -0 "$pid" >/dev/null 2>&1; then
      kill -9 "$pid" || true
    fi
    rm -f "$FRONTEND_PID_FILE" || true
  else
    echo "Frontend is not running (no pid found)."
  fi
}

compose_down() {
  require_cmd docker

  if [[ -f "$COMPOSE_FILE" ]]; then
    echo "Stopping docker services..."
    (cd "$ROOT_DIR" && docker compose -f "$COMPOSE_FILE" down)
  fi
}

status() {
  require_cmd ss
  require_cmd docker

  echo "Ports:"
  ss -ltnp 2>/dev/null | awk '/:4300|:5000|:1433/ {print}'
  echo
  echo "Docker:"
  (cd "$ROOT_DIR" && docker compose -f "$COMPOSE_FILE" ps)
  echo
  echo "URLs:"
  echo "  Frontend: $FRONTEND_URL"
  echo "  API:      http://127.0.0.1:5000/"
}

logs() {
  if [[ -f "$FRONTEND_LOG_FILE" ]]; then
    tail -n 200 "$FRONTEND_LOG_FILE"
  else
    echo "No frontend log found at: $FRONTEND_LOG_FILE"
  fi
}

cmd="${1:-up}"
shift || true

case "$cmd" in
  up)
    build="false"
    if [[ "${1:-}" == "--build" ]]; then
      build="true"
    fi
    compose_up "$build"
    frontend_up
    ;;
  down)
    frontend_down
    compose_down
    ;;
  status)
    status
    ;;
  logs)
    logs
    ;;
  -h|--help|help)
    usage
    ;;
  *)
    echo "ERROR: Unknown command: $cmd" >&2
    usage
    exit 1
    ;;
esac
