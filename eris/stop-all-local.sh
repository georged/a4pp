#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"

kill_pattern() {
  local pattern="$1"
  if pkill -f "$pattern" >/dev/null 2>&1; then
    echo "Stopped: $pattern"
  fi
}

echo "Stopping Eris/Hermes local processes..."

kill_pattern "$ROOT_DIR/Eris.AppHost"
kill_pattern "$ROOT_DIR/Eris.Api"
kill_pattern "$ROOT_DIR/Hermes.App"
kill_pattern "dotnet run --project $ROOT_DIR/Eris.AppHost"
kill_pattern "dotnet run --project $ROOT_DIR/Eris.Api"
kill_pattern "dotnet run --project $ROOT_DIR/Hermes.App"
kill_pattern "Aspire.Dashboard"
kill_pattern "dcpctrl"

# Clean up listeners commonly used by this workspace.
for port in 15118 17203 21253 5158 7294 5229 7280; do
  pids="$(lsof -ti tcp:"$port" -sTCP:LISTEN 2>/dev/null || true)"
  if [[ -n "$pids" ]]; then
    echo "Killing listeners on port $port: $pids"
    kill -9 $pids >/dev/null 2>&1 || true
  fi
done

echo "Done."
