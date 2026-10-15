# Experimental brakes build progress (updated PT 2026-10-15 16:25)
Worktree /workspace/iso/brakes_build, branch experimental-brakes, off origin/main 99064bc. Diag dir /workspace/iso/brakes_diag.
- [x] model code, engine hook, settings chain, tab rows, HUD tag (local commit, NOT pushed yet)
- [x] docs/BrakeSources.md; docs/HandbrakeModelingNote.md (uncommitted yet)
- [x] tests: full EditMode suite 201/201 pass (171 old + 30 new) -> brakes_diag/full1.xml
- [x] baseline (99064bc) 15 scenarios one at a time: brakes_diag/base_run (15 hashes, reproducible)
- [ ] new tree 15 scenarios one at a time: brakes_diag/new_run (running) then ./cmp.sh base_run new_run
- [ ] scenario `brake`/`brakethr` with experimental on (overrides file) for report
- [ ] plots (tools/plot_brakes.py -> diagnostics/brakes), handbrake note commit, audit + spelling, push (normal push, never force)
