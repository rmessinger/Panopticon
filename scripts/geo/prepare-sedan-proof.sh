#!/usr/bin/env bash
# Entry point: sedan_prepare_orchestrator.py resolves the root (positional > PANOPTICON_GEO_PROOF_DIR > default).
set -euo pipefail

export PYTHONPYCACHEPREFIX=/tmp/opencode/sedan-inventory/pycache-runtime
export PYTHONDONTWRITEBYTECODE=1

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "${script_dir}/sedan_prepare_orchestrator.py" "$@"
