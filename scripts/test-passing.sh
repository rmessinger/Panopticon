#!/usr/bin/env bash
# Run only tests expected to pass — excludes unimplemented roadmap stubs and
# tests requiring a game installation.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test src --filter "Category!=Interactive&Category!=Roadmap"
