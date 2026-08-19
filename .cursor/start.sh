#!/usr/bin/env bash
# Per-boot runtime initialization for the RSD Payroll System.
#
# There is no systemd in the Cloud Agent VM, so the SQL Server engine is
# launched directly. This script:
#   1. ensures a usable SQL Server data directory on the *writable* filesystem,
#   2. starts the engine and waits until it accepts connections,
#   3. applies EF Core migrations, and
#   4. runs the web app in the foreground (it stays attached as the start proc).
#
# Why step 1 matters: environment snapshots/builds boot on an overlay
# filesystem. SQL Server 2022 opens its data files with O_DIRECT, which
# overlayfs rejects for files that live in the read-only lower (image) layer.
# A database baked into the snapshot therefore fails to open with
# "Error: 17113 ... Error 87(The parameter is incorrect.)". We detect that with
# an O_DIRECT probe and reinitialize a fresh database on the writable layer;
# the schema is recreated by EF migrations and demo users are re-seeded by the
# app on startup, so no data is lost that cannot be regenerated.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=/dev/null
source "$SCRIPT_DIR/env.sh"

LOG_DIR="/tmp/cursor"
mkdir -p "$LOG_DIR"
SQL_LOG="$LOG_DIR/sqlservr.log"
MSSQL_DATA="/var/opt/mssql/data"
MASTER_MDF="$MSSQL_DATA/master.mdf"

reset_datadir() {
    echo "    Reinitializing SQL Server data directory on the writable layer"
    sudo rm -rf /var/opt/mssql/data /var/opt/mssql/log
    sudo install -d -o mssql -g mssql /var/opt/mssql/data /var/opt/mssql/log
}

launch_engine() {
    sudo -u mssql env \
        ACCEPT_EULA=Y \
        MSSQL_PID=Developer \
        MSSQL_SA_PASSWORD="$MSSQL_SA_PASSWORD" \
        nohup /opt/mssql/bin/sqlservr >"$SQL_LOG" 2>&1 &
    echo "    launched sqlservr (logs: $SQL_LOG)"
}

wait_ready() {
    local limit="$1" i
    for i in $(seq 1 "$limit"); do
        if /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
            -Q "SELECT 1" >/dev/null 2>&1; then
            echo "    SQL Server is ready (after ${i}s)"
            return 0
        fi
        sleep 1
    done
    return 1
}

echo "==> Preparing SQL Server engine"
if pgrep -x sqlservr >/dev/null 2>&1; then
    echo "    sqlservr already running"
else
    # A data directory that was baked into an environment snapshot lands in the
    # read-only overlay lower layer. SQL Server then fails to open its files with
    # O_DIRECT (Error 17113 / 87). Reinitialize on the writable layer when the
    # data dir looks foreign: a master.mdf that fails an O_DIRECT probe, OR a
    # non-empty dir missing master.mdf (a partially-baked first-run state). A
    # truly empty dir is left alone so SQL Server can do a clean first-time setup.
    if [ -d "$MSSQL_DATA" ] && sudo test -n "$(sudo sh -c "ls -A '$MSSQL_DATA' 2>/dev/null")"; then
        if [ ! -f "$MASTER_MDF" ]; then
            echo "    Detected a partial/foreign data directory (no master.mdf)"
            reset_datadir
        elif ! sudo dd if="$MASTER_MDF" iflag=direct bs=4096 count=1 of=/dev/null >/dev/null 2>&1; then
            echo "    Detected unusable (read-only overlay) database files"
            reset_datadir
        fi
    fi

    launch_engine
    if ! wait_ready 60; then
        echo "    SQL Server did not start; resetting data directory and retrying" >&2
        tail -n 20 "$SQL_LOG" >&2 || true
        pkill -x sqlservr 2>/dev/null || true
        sleep 2
        reset_datadir
        launch_engine
        if ! wait_ready 90; then
            echo "ERROR: SQL Server did not become ready in time" >&2
            tail -n 40 "$SQL_LOG" >&2 || true
            exit 1
        fi
    fi
fi

# Make sure the engine is reachable even if it was already running.
wait_ready 30 >/dev/null || true

echo "==> Applying EF Core migrations"
dotnet ef database update --project "$REPO_ROOT/RSDSystem/RSDSystem.csproj"

# Avoid launching a second instance if the app is already serving (e.g. this
# script was run manually while the configured start command is also active).
if curl -s -o /dev/null http://localhost:5114/Account/Login 2>/dev/null; then
    echo "==> Web app already serving on http://localhost:5114; leaving it running"
    exit 0
fi

echo "==> Starting the RSD Payroll web app on $ASPNETCORE_URLS"
echo "    Demo logins: demo / Demo@123 (Admin), payroll / Payroll@123 (Payroll Staff)"
cd "$REPO_ROOT"
exec dotnet run --project RSDSystem/RSDSystem.csproj --no-launch-profile
