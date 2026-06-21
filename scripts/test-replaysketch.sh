#!/usr/bin/env bash
# Run all ReplaySketch tests: headless Core/logic tests and roadmap stubs.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test src/OpenSage.Tools.ReplaySketch.Tests
dotnet test src/OpenSage.Game.Tests --filter "FullyQualifiedName~ReplaySketch"
