#!/usr/bin/env bash
# Run all ReplaySketch tests: headless Core/logic tests and roadmap stubs.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test test/Panopticon.Tools.ReplaySketch.Tests
dotnet test test/Panopticon.Simulation.Tests --filter "FullyQualifiedName~ReplaySketch"
