#!/bin/bash
# @description - Compares tools/ with the dev-box working tree (default /workspace/scirocco_sim) and, only with --apply, copies the files that differ. A file is refused (never copied) when the dev-box copy still contains an old pre-rename identifier, so stale copies cannot overwrite the renamed repo files.
# Usage: sync_tools.sh [--apply] [source_directory]
# @param {String} --apply - optional first argument: really copy (default is a dry run that only lists differences)
# @param {String} source_directory - optional dev-box tree, default /workspace/scirocco_sim
# @return {Int} - 0 on success, 1 when the source tree is missing, 2 when --apply was given and at least one file was refused
# Files synced: suspension_params.json, kinematics/{export_unity,solve_kinematics,params_io}.py, visco_*.py, the research notes (*.md).
# Blocks a copy: old identifiers (see docs/RENAMES.md: rb*/fc*/kp* SuspensionTables field names, Kkin, us_grad, steer_geom, Fx_brake) or a python file without the #!/usr/bin/python3 shebang and # @description header.
set -euo pipefail

SYNCED_FILES="suspension_params.json kinematics/export_unity.py kinematics/solve_kinematics.py kinematics/params_io.py visco_fit.py visco_metrics.py visco_plots.py visco_smoke.py suspension_data.md tire_thermal_research.md service_manual_findings.md online_research_findings.md proposed_geometry_fix.md viscoelastic_tyre_research.md"
LEGACY_PATTERN='\b(rb|fc|kp)[A-Z][A-Za-z0-9]*\b|\bKkin\b|\bus_grad\b|\bsteer_geom\b|\bFx_brake\b|\bMuOfT\b|\bTCross\b|\bMuRoad\b|\bMuBench\b'

# @description - Tells whether a file still uses an old pre-rename identifier.
# @param {String} path - file to scan
# @return {Int} - 0 when an old identifier is present, 1 when the file is clean
contains_legacy_identifier() {
    local path=$1
    grep -Eq "$LEGACY_PATTERN" "$path"
}

# @description - Tells whether a script meets the readability standard well enough to be copied: python files need the #!/usr/bin/python3 shebang and a "# @description" header; other file types are always acceptable.
# @param {String} path - file to check
# @return {Int} - 0 when acceptable, 1 when a python file lacks the shebang or header
meets_script_standard() {
    local path=$1
    case "$path" in
        *.py) head -1 "$path" | grep -q '^#!/usr/bin/python3$' && grep -q '^# @description' "$path" ;;
        *) return 0 ;;
    esac
}

# @description - Handles one file: reports a difference and copies it when allowed.
# @param {String} source_directory - dev-box tree
# @param {String} destination_directory - the tools/ directory of this repository
# @param {String} relative_path - file path relative to both directories
# @param {Int} apply - 1 to copy, 0 for a dry run
# @return {Int} - 0 normally, 1 when the file was refused because of an old identifier
sync_file() {
    local source_directory=$1 destination_directory=$2 relative_path=$3 apply=$4
    local source_path="$source_directory/$relative_path" destination_path="$destination_directory/$relative_path"
    [ -f "$source_path" ] || return 0
    cmp -s "$source_path" "$destination_path" && return 0
    if contains_legacy_identifier "$source_path" || ! meets_script_standard "$source_path"; then
        echo "REFUSED (dev-box copy uses old identifiers or lacks the shebang/header, update it first): $relative_path"
        return 1
    fi
    echo "differs: $relative_path"
    if [ "$apply" = 1 ]; then
        cp -p "$source_path" "$destination_path"
        echo "  copied"
    fi
    return 0
}

# @description - Syncs every listed file and prints a summary.
# @param {List} arguments - command-line arguments: optional --apply, then optional source directory
# @return {Int} - see the header
main() {
    local apply=0 refused=0
    if [ "${1:-}" = "--apply" ]; then apply=1; shift; fi
    local source_directory="${1:-/workspace/scirocco_sim}"
    local destination_directory
    destination_directory="$(cd "$(dirname "$0")" && pwd)"
    [ -d "$source_directory" ] || { echo "source tree $source_directory not found" >&2; return 1; }
    for relative_path in $SYNCED_FILES; do
        sync_file "$source_directory" "$destination_directory" "$relative_path" "$apply" || refused=1
    done
    if [ $apply = 1 ]; then echo "tools/ synced from $source_directory"; else echo "dry run only; pass --apply to copy"; fi
    if [ $apply = 1 ] && [ $refused = 1 ]; then return 2; fi
    return 0
}

main "$@"
