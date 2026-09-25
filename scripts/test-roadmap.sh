#!/usr/bin/env bash
# Run only the roadmap stub tests (all expected to fail until implemented).
# Useful for checking which phases still need work.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test test/Panopticon.Simulation.Tests --filter "Category=Roadmap"
