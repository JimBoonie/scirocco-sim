#!/usr/bin/env bash
# @description - Refresh tools/ from the working tree at /workspace/scirocco_sim (source of truth).
# @param {String} arguments - command-line arguments (see the script body)
# @return {Int} - process exit status (0 = success)
# Refresh tools/ from the working tree at /workspace/scirocco_sim (source of truth).
set -euo pipefail
SRC="${1:-/workspace/scirocco_sim}"; DST="$(cd "$(dirname "$0")" && pwd)"
for f in suspension_params.json suspension_data.md tire_thermal_research.md service_manual_findings.md online_research_findings.md proposed_geometry_fix.md; do cp -p "$SRC/$f" "$DST/"; done
cp -p "$SRC/kinematics/solve_kinematics.py" "$SRC/kinematics/export_unity.py" "$DST/kinematics/"
echo "tools/ synced from $SRC"
