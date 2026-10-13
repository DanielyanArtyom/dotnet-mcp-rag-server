#!/usr/bin/env bash
# Starts a separate Lumen API for recording the demo media: port 5219, database lumen_demo,
# Azure providers from the user-secrets of src/Lumen.Api. The regular `lumen` database is never touched.
set -euo pipefail
cd "$(dirname "$0")/../.."

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:5219
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=lumen_demo;Username=postgres;Password=postgres"
export Demo__FilingsPath="$PWD/samples/edgar"
export DocumentStorage__RootPath="$PWD/src/Lumen.Api/App_Data/DemoDocuments"

exec dotnet run --project src/Lumen.Api --no-launch-profile
