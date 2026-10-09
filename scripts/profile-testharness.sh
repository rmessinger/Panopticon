#!/usr/bin/env bash
# Usage: scripts/profile-testharness.sh [TestHarness arguments...]
# Overrides: TRACE_DURATION (dd:hh:mm:ss), CONFIGURATION, TRACE_OUTPUT_DIR.
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
project="$repo_root/src/Panopticon.Tools.TestHarness/Panopticon.Tools.TestHarness.csproj"
configuration="${CONFIGURATION:-Release}"
duration="${TRACE_DURATION:-00:00:02:00}"
output_dir="${TRACE_OUTPUT_DIR:-${TMPDIR:-/tmp}/panopticon-traces/$(date +%Y%m%d-%H%M%S)-$$}"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "Missing .NET SDK: install dotnet before running this script." >&2
    exit 1
fi

if command -v dotnet-trace >/dev/null 2>&1; then
    trace_tool="$(command -v dotnet-trace)"
elif [[ -x "$HOME/.dotnet/tools/dotnet-trace" ]]; then
    trace_tool="$HOME/.dotnet/tools/dotnet-trace"
else
    echo "Missing dotnet-trace. Install: dotnet tool install --global dotnet-trace" >&2
    exit 1
fi

profiles="$("$trace_tool" list-profiles)"
if [[ "$profiles" == *dotnet-sampled-thread-time* ]]; then
    profile="dotnet-sampled-thread-time,dotnet-common"
else
    profile="cpu-sampling"
fi

mkdir -p "$output_dir"
output_dir="$(cd "$output_dir" && pwd)"
trace_path="$output_dir/testharness.nettrace"
report_path="$output_dir/testharness.speedscope.json"

cd "$repo_root"
dotnet build "$project" --configuration "$configuration"
target_path="$(dotnet msbuild "$project" -nologo \
    -property:Configuration="$configuration" -getProperty:TargetPath)"
if [[ ! -f "$target_path" ]]; then
    echo "Could not locate the built TestHarness: $target_path" >&2
    exit 1
fi

echo "Tracing TestHarness for up to $duration (startup included)."
echo "An early scenario exit ends collection early. Trace finalization can take longer."

# Launch the DLL directly so tracing targets the harness, not the dotnet build/run CLI.
# dotnet-trace finalizes the trace and terminates its child when the duration expires.
collect_status=0
"$trace_tool" collect --profile "$profile" --duration "$duration" \
    --output "$trace_path" -- dotnet exec "$target_path" "$@" || collect_status=$?

# Preserve a useful report even if the harness exits with an acceptance-check failure.
if [[ ! -s "$trace_path" ]]; then
    echo "No trace generated. Collection exit code: $collect_status" >&2
    exit 1
fi
"$trace_tool" convert "$trace_path" --format Speedscope
if [[ ! -s "$report_path" ]]; then
    echo "Speedscope conversion did not produce $report_path" >&2
    exit 1
fi

echo "Trace: $trace_path"
echo "Speedscope: $report_path"
echo "Open the report at https://www.speedscope.app/"
exit "$collect_status"
