#!/usr/bin/env bash
# Run all non-interactive tests (skips tests requiring a game installation).
# This is what CI runs.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test src --filter "Category!=Interactive"
