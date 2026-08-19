#!/usr/bin/env bash
# Shared environment for the RSD Payroll System Cloud Agent setup.
# Sourced by install.sh, start.sh, and the web terminal.
#
# NOTE: MSSQL_SA_PASSWORD guards only the throwaway, VM-local SQL Server
# instance (it listens on localhost and is never exposed publicly). The value
# below is a local development default; override it by adding an
# MSSQL_SA_PASSWORD environment secret if you prefer.

export MSSQL_SA_PASSWORD="${MSSQL_SA_PASSWORD:-Dev_Str0ng!Passw0rd}"

# EF Core global tool and dotnet SDK live on PATH.
export PATH="$PATH:$HOME/.dotnet/tools:/opt/mssql/bin:/opt/mssql-tools18/bin"

# ASP.NET Core runtime configuration for local development.
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:5114}"

# Point the app + EF tooling at the local SQL Server instance.
export ConnectionStrings__DefaultConnection="Server=localhost;Database=RSDPayrollDB;User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True;Encrypt=False;"
