#!/usr/bin/env bash
# Per-boot runtime initialization for the RSD Payroll System.
# Starts the local SQL Server engine (there is no systemd in the Cloud Agent
# VM, so sqlservr is launched directly) and applies EF Core migrations.
# Idempotent: it will not start a second engine and migrations are a no-op when
# the schema is already current.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=/dev/null
source "$SCRIPT_DIR/env.sh"

LOG_DIR="/tmp/cursor"
mkdir -p "$LOG_DIR"
SQL_LOG="$LOG_DIR/sqlservr.log"

echo "==> Starting SQL Server engine"
if pgrep -x sqlservr >/dev/null 2>&1; then
    echo "    sqlservr already running"
else
    # First boot initializes the system databases; later boots recover them.
    sudo -u mssql env \
        ACCEPT_EULA=Y \
        MSSQL_PID=Developer \
        MSSQL_SA_PASSWORD="$MSSQL_SA_PASSWORD" \
        nohup /opt/mssql/bin/sqlservr >"$SQL_LOG" 2>&1 &
    echo "    launched sqlservr (logs: $SQL_LOG)"
fi

echo "==> Waiting for SQL Server to accept connections"
ready=0
for i in $(seq 1 60); do
    if /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
        -Q "SELECT 1" >/dev/null 2>&1; then
        ready=1
        echo "    SQL Server is ready (after ${i}s)"
        break
    fi
    sleep 1
done

if [ "$ready" -ne 1 ]; then
    echo "ERROR: SQL Server did not become ready in time" >&2
    tail -n 40 "$SQL_LOG" >&2 || true
    exit 1
fi

echo "==> Applying EF Core migrations"
dotnet ef database update --project "$REPO_ROOT/RSDSystem/RSDSystem.csproj"

echo "==> Start complete. Launch the web app with:"
echo "    source .cursor/env.sh && dotnet run --project RSDSystem/RSDSystem.csproj --no-launch-profile"
