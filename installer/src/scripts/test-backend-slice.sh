#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
SRC_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"
cd "$SRC_ROOT"

echo "=== MEM backend slice gate ==="
echo "Building once, then running the fast non-API regression projects."
echo

dotnet build MemInstaller.sln

projects=(
  "./HostAgent.Tests/HostAgent.Tests/HostAgent.Tests.csproj"
  "./Shared/Mem.Localization.Tests/Mem.Localization.Tests.csproj"
  "../../cli/src/Mem.Cli.Tests/Mem.Cli.Tests/Mem.Cli.Tests.csproj"
  "../../migrate/tests/Mem.Migrate.UnitTests/Mem.Migrate.UnitTests.csproj"
  "../../migrate/tests/Mem.Migrate.ArchiveTests/Mem.Migrate.ArchiveTests.csproj"
  "../../migrate/tests/Mem.Migrate.IntegrationTests/Mem.Migrate.IntegrationTests.csproj"
)

for project in "${projects[@]}"; do
  echo
  echo "=== $project ==="
  dotnet test "$project" --no-build --no-restore
 done

if (( $# == 0 )); then
  echo
  echo "No API integration filter supplied."
  echo "Api.IntegrationTests was intentionally not run by the fast slice gate."
  echo "Pass one or more class/namespace fragments when the slice changes API behavior, for example:"
  echo "  bash ./scripts/test-backend-slice.sh OperatorDomainCertificateReadContractTests DomainActiveCertificateSelectionTests"
  exit 0
fi

filter=""
for fragment in "$@"; do
  if [[ -n "$filter" ]]; then
    filter+="|"
  fi
  filter+="FullyQualifiedName~${fragment}"
done

echo
echo "=== Focused API integration gate ==="
echo "Filter: $filter"
dotnet test \
  ./Api.IntegrationTests/Api.IntegrationTests.csproj \
  --no-build \
  --no-restore \
  --filter "$filter"
