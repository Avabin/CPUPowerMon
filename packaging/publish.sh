#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname -- "$SCRIPT_DIR")"

dotnet publish "$REPO_ROOT/src/CpuMon.Service/CpuMon.Service.csproj" \
    -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true \
    -o "$REPO_ROOT/artifacts/service"

dotnet publish "$REPO_ROOT/src/CpuMon.Client/CpuMon.Client.csproj" \
    -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true \
    -o "$REPO_ROOT/artifacts/client"

echo "Published:"
echo "  $REPO_ROOT/artifacts/service/cpumond"
echo "  $REPO_ROOT/artifacts/client/cpumonctl"
