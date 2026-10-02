#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="${1:-.}"
DRY_RUN="${DRY_RUN:-false}"

find "$ROOT_DIR" -type f -name "*.csproj" -print0 |
while IFS= read -r -d '' project_file; do
  project_dir="$(dirname "$project_file")"

  for build_dir in "$project_dir/bin" "$project_dir/obj"; do
    if [ -d "$build_dir" ]; then
      if [ "$DRY_RUN" = "true" ]; then
        echo "[DRY RUN] Would delete: $build_dir"
      else
        echo "Deleting: $build_dir"
        rm -rf -- "$build_dir"
      fi
    fi
  done
done
