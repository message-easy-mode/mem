#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
SRC_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
cd "$SRC_ROOT"

echo "=== MEM full backend release gate ==="
echo "This is intentionally the slow gate. Run it for release/RC checkpoints and cross-cutting changes."
echo

dotnet build MemInstaller.sln
dotnet test MemInstaller.sln --no-build --no-restore
