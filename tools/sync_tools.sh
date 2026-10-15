#!/usr/bin/env bash
# @description - Compares tools/ with the dev-box working tree (/workspace/scirocco_sim) and, only with --apply, copies the data files that carry no Unity field names. Never overwrites kinematics/export_unity.py (it writes the renamed SuspensionTables fields) or the research notes edited for the rename.
# @param {String} --apply - optional first argument: really copy (default is a dry run that only lists differences)
# @param {String} source - optional source tree, default /workspace/scirocco_sim
# @return {Int} - 0 on success, 1 if the source tree is missing
# Safety: the dev-box copies of export_unity.py, solve_kinematics.py and the *.md notes still use the OLD field names
# (rbCamOwn, fcToeFy, kpU, p.<carParamsField>...). They must be updated there (see docs/RENAMES.md) before they may be synced back.
set -euo pipefail
APPLY=0; if [ "${1:-}" = "--apply" ]; then APPLY=1; shift; fi
SRC="${1:-/workspace/scirocco_sim}"; DST="$(cd "$(dirname "$0")" && pwd)"
[ -d "$SRC" ] || { echo "source tree $SRC not found" >&2; exit 1; }
SAFE_FILES="suspension_params.json"                                     # pure input data, no Unity identifiers
GUARDED_FILES="suspension_data.md tire_thermal_research.md service_manual_findings.md online_research_findings.md proposed_geometry_fix.md kinematics/solve_kinematics.py kinematics/export_unity.py"
for f in $SAFE_FILES; do
  if ! cmp -s "$SRC/$f" "$DST/$f"; then echo "differs: $f"; [ $APPLY = 1 ] && cp -p "$SRC/$f" "$DST/$f" && echo "  copied"; fi
done
for f in $GUARDED_FILES; do
  if [ -f "$SRC/$f" ] && ! cmp -s "$SRC/$f" "$DST/$f"; then echo "GUARDED (not copied, update the dev-box copy to the renamed identifiers first): $f"; fi
done
[ $APPLY = 1 ] && echo "tools/ data files synced from $SRC" || echo "dry run only; pass --apply to copy the safe files"
