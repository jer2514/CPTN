#!/usr/bin/env bash
# Idempotent repository bootstrap for the RSD Payroll System.
# Safe to run repeatedly. System packages (the .NET 8 SDK and SQL Server 2022)
# are normally baked into the environment base image / snapshot; the guarded
# blocks below re-install them only if they are missing so the script also
# works on a plain Ubuntu 24.04 base.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=/dev/null
source "$SCRIPT_DIR/env.sh"

echo "==> Ensuring system dependencies (.NET 8 SDK + SQL Server 2022)"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "    Installing .NET 8 SDK"
    sudo apt-get update -y
    sudo apt-get install -y dotnet-sdk-8.0
fi

if ! command -v /opt/mssql/bin/sqlservr >/dev/null 2>&1; then
    echo "    Installing SQL Server 2022 + tools"
    sudo apt-get update -y
    sudo apt-get install -y curl gnupg apt-transport-https ca-certificates
    curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
        | sudo gpg --batch --yes --dearmor -o /usr/share/keyrings/microsoft-prod.gpg
    curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2022.list \
        | sed 's#deb \[#deb [signed-by=/usr/share/keyrings/microsoft-prod.gpg #' \
        | sudo tee /etc/apt/sources.list.d/mssql-server-2022.list >/dev/null
    curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/prod.list \
        | sed 's#deb \[#deb [signed-by=/usr/share/keyrings/microsoft-prod.gpg #' \
        | sudo tee /etc/apt/sources.list.d/msprod.list >/dev/null
    # SQL Server 2022 needs OpenLDAP 2.5, which Ubuntu 24.04 no longer ships.
    if ! dpkg -s libldap-2.5-0 >/dev/null 2>&1; then
        LDAP_DEB="libldap-2.5-0_2.5.20+dfsg-0ubuntu0.22.04.1_amd64.deb"
        curl -fsSL -o "/tmp/${LDAP_DEB}" \
            "http://archive.ubuntu.com/ubuntu/pool/main/o/openldap/${LDAP_DEB}"
        sudo dpkg -i "/tmp/${LDAP_DEB}"
    fi
    sudo apt-get update -y
    sudo apt-get install -y mssql-server
    sudo ACCEPT_EULA=Y apt-get install -y mssql-tools18 unixodbc-dev
    sudo /opt/mssql/bin/mssql-conf set sqlagent.enabled false || true
fi

echo "==> dotnet-ef global tool"
if ! dotnet tool list --global 2>/dev/null | grep -qi dotnet-ef; then
    dotnet tool install --global dotnet-ef --version "8.*"
fi

echo "==> Restoring and building the solution"
dotnet build "$REPO_ROOT/RSDSystem.sln" -c Debug

echo "==> Install complete"
