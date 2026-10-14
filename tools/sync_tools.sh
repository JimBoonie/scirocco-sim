#!/usr/bin/env bash
# Refresh tools/ from the working tree at /workspace/scirocco_sim (source of truth).
set -euo pipefail
SRC="${1:-/workspace/scirocco_sim}"; DST="$(cd "$(dirname "$0")" && pwd)"
for f in suspension_params.json suspension_data.md tire_thermal_research.md service_manual_findings.md online_research_findings.md proposed_geometry_fix.md; do cp -p "$SRC/$f" "$DST/"; done
cp -p "$SRC/kinematics/solve_kinematics.py" "$SRC/kinematics/export_unity.py" "$DST/kinematics/"
rm -rf "$DST/patches"; cp -rp "$SRC/patches" "$DST/patches"; rm -rf "$DST/patches/__pycache__"
echo "tools/ synced from $SRC"
