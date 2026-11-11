# SYNC-LOG.md

Tracks upstream syncs of the project-local pstack in `.opencode/`.
Source of truth: https://github.com/backnotprop/pstack (standalone mirror of `cursor/plugins/pstack`).
The OpenCode V2 adaptation follows the one in the PlaywrightDotNet repo, minus that repo's own packs (dotnet, testing, UI, graphify, ECC).
Future syncs are manual three-way merges against the mirror, not clean pulls, because this tree rewrites Cursor names for OpenCode.

## 2026-11-11 — OpenCode V2 port, mirror 0854796 (0.15.36)

- Mirror `backnotprop/pstack` @ `08547969e1dffaa6457ea65141cc62c1d9a2eef1`. Since the last adapted sync (`a6342de`), upstream changed only `SYNC-LOG.md` and sponsor SVGs. The vendored skills and agents equaled the mirror byte for byte before this port.
- `agents/poteto-agent.md`, `agents/comment-sicko.md`: added `mode: subagent` and dropped `name` and `is_background`, which V2 ignores. Before this, both resolved as `primary` agents, and V2 refuses to spawn a primary agent ("cannot run as a subagent"). `poteto-agent` now reads `.opencode/skills/poteto-mode/SKILL.md` instead of searching `~/.cursor/plugins/`. `comment-sicko` gains a harness note: it runs `/how` and `/why` itself, because a subagent cannot spawn subagents.
- `skills/reflect/references/*-reviewer.md`: a skill now counts as used after a `read` under `.opencode/skills/` or a `subagent` prompt naming its path.
- Skills rewritten for V2 names (`subagent` tool with `agent`, `.opencode/pstack-models.md`, `.opencode/skills/`, `opencode session list`/`export`): architect, arena, automate-me, create-verification-skill, how, maintain-verification-skill, no-comments, poteto-help, poteto-mode, recall, reflect, setup-pstack, swarm, why. `statechart/scripts/package.json` renamed to `@pstack/statechart`. The PlaywrightDotNet graphify seeding in `how` and `why` was not carried over.
- `skills/poteto-mode/SKILL.md` Harness: parallel fan-out uses `background: true` on every worker in one turn; role lines are explicit model choices; `/loop` maps to the built-in `/goal` and to `schedule_create`; verification uses `dotnet test` plus Playwright or a CDP browser.
- `docs/harness/opencode.md`: the Cursor-to-OpenCode adapter. Verified on 2.2.7 against the installed binary: subagent params (`agent`, `description`, `prompt`, `model`, `background`, `sessionID`), the primary-agent refusal, the depth limit of 1, and the `experimental.todo` and `experimental.schedules` gates.
- `docs/guide/`, `docs/harness/claude-code.md`, `README.md`: upstream docs. The README's benny automation paragraph is replaced with an OpenCode note.
- `skills/deslop/`: vendored from `cursor/plugins` `cursor-team-kit` 1.2.0 (MIT) @ `5866df5`, byte-identical. poteto-mode and `working-lean-with-poteto` gate commits on it.
- `commands/`: one wrapper per non-`principle-*` skill plus `deslop` (35), generated from each skill's `description`. V2 does not expose skills as slash commands, so before this `/poteto-mode`, `/swarm`, and the rest did not exist here.
- `pstack-models.md`: per-role models as exact `provider/model#variant` IDs from the `opencode` and `opencode-go` providers, verified with `opencode models` and `opencode api GET /api/model`.
- `opencode.json`: `experimental.todo` (enables `todowrite`), `experimental.schedules` (enables `schedule_*`), and `instructions` loading `pstack-models.md`.
- `skills/working-lean-with-poteto/SKILL.md` (local skill, not upstream): short model names replaced with exact IDs, the `subagent` spawn and background fan-out spelled out, and `ponytail` named as a plugin skill.
