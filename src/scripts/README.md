# Developer and Agent Scripts

Reusable tooling for building, checking and running the EDD plant demo. Agents (Claude Code, opencode) and people use the same scripts, so a check is never retyped. Every script can be run from anywhere in the repo, prints its usage with `-h`, and returns a non-zero exit code on failure.

The demo's own runtime script, `drill.sh`, is not here. It lives with the runtime in [`src/deployment/scripts/`](../deployment/scripts/).

## Index

| # | Script | What it does | Example |
| --- | --- | --- | --- |
| 1 | [`build/test-backend.sh`](build/test-backend.sh) | Runs the .NET unit tests, integration tests and solution build. `--repeat N` re-runs and names any flaky failure | `src/scripts/build/test-backend.sh unit --repeat 4` |
| 2 | [`build/test-frontend.sh`](build/test-frontend.sh) | Runs `bun test` and the strict `bun run build` | `src/scripts/build/test-frontend.sh` |
| 3 | [`live/api.sh`](live/api.sh) | Calls the running control-room API: overview, proof, alarms, pause/resume, inject a fault, or watch the proof verdict | `src/scripts/live/api.sh watch-proof 60` |
| 4 | [`live/rehearse.sh`](live/rehearse.sh) | Runs the live demo once, end to end (steady state, central down and up, an alarm during the drain, kill a gateway, failover, kill a consumer, proof), checks each step and prints a results table | `src/scripts/live/rehearse.sh 120 /tmp/rehearsal.png` |
| 5 | [`diagrams/check.sh`](diagrams/check.sh) | Runs the diagram-design skill's checks on diagrams, and optionally exports SVG or review PNGs | `src/scripts/diagrams/check.sh 01-plant-to-central --export` |
| 6 | [`diagrams/gen01_plant_to_central.py`](diagrams/gen01_plant_to_central.py) | Regenerates diagram 01 (the detailed architecture) from code | `python3 src/scripts/diagrams/gen01_plant_to_central.py` |
| 7 | [`diagrams/gen_codebase.py`](diagrams/gen_codebase.py) | Regenerates diagrams 09 (backend codebase) and 10 (frontend codebase) from code; fails if a line does not fit its card | `python3 src/scripts/diagrams/gen_codebase.py` |
| 8 | [`diagrams/screenshot.py`](diagrams/screenshot.py) | Screenshots a page or local HTML file with Playwright, optionally cropped, and can run a command (`--run`, `--after`) while the page is open (for diagram or dashboard review) | `python3 src/scripts/diagrams/screenshot.py http://localhost:3000 /tmp/dash.png` |
| 9 | [`progress/progress.py`](progress/progress.py) | Edits `docs/04-build-progress.md`: last-update line, status rows, the "Right now" handover step, the tests line | `python3 src/scripts/progress/progress.py row 16 --status Done` |
| 10 | [`git/scan-private.sh`](git/scan-private.sh) | Scans outgoing commits for e-mail addresses, phone numbers and keys before a push (the repo is public) | `src/scripts/git/scan-private.sh` |
| 11 | [`git/commit-push.sh`](git/commit-push.sh) | Commits the given paths with the project trailer, runs the scan, and pushes the current branch | `src/scripts/git/commit-push.sh "docs: update build progress"` |
| 12 | [`sdd/sdd.sh`](sdd/sdd.sh) | Shortcuts for the task-by-task build of doc 03: extract a task brief, write a review package, append to the ledger | `src/scripts/sdd/sdd.sh brief 16` |
| 13 | [`build/format.sh`](build/format.sh) | Formats the C# codebase with CSharpier 1.4.0 (a local tool pinned in `src/backend/dotnet-tools.json`); `--check` only reports | `src/scripts/build/format.sh --check` |
| 14 | [`git/install-hooks.sh`](git/install-hooks.sh) | Activates the versioned git hooks below (`core.hooksPath`); run it once per clone | `src/scripts/git/install-hooks.sh` |
| 15 | [`git/hooks/pre-commit`](git/hooks/pre-commit) | Formats the staged `.cs` files with CSharpier and re-stages them | (runs on `git commit`) |
| 16 | [`git/hooks/pre-push`](git/hooks/pre-push) | Blocks a push while any C# file is unformatted | (runs on `git push`) |

**Formatting rule:** C# is formatted with CSharpier before every push. After cloning, run `src/scripts/git/install-hooks.sh`; `git/commit-push.sh` installs the hooks itself if they are missing.

## Conventions

- **Location:** tooling lives only here ([doc 00 §12](../../docs/00-edd-demo-game-plan.md#12-solution-layout)), grouped by purpose: `build/`, `live/`, `diagrams/`, `progress/`, `git/`, `sdd/`.
- **Shell:** bash, compatible with macOS's bash 3.2 (no `${var,,}`, no associative arrays), `set -euo pipefail`.
- **Python:** python3 standard library only, except `screenshot.py`, which needs Playwright (`pip install playwright && playwright install chromium`).
- **Self-locating:** each script finds the repo root with `git rev-parse --show-toplevel`, so the working directory does not matter.
- **Documented:** each script starts with a header comment or docstring giving its purpose, usage and examples; `-h` prints it.
- **Safe:** nothing here deletes volumes, rewrites history or force-pushes. `commit-push.sh` refuses to push to `main` or `development`, and refuses when the scan finds private data.
