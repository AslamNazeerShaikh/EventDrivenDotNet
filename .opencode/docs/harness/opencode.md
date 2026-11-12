# pstack in OpenCode

pstack skills use Cursor names. In OpenCode, read this file once per session, then map each Cursor name in a skill with the tables below. `<pstack>` means the folder that holds `skills/` and `docs/` — in this project, `.opencode/`. If a Cursor field has no row, drop it and say so. Never pass a field or value the tool schema rejects.

These mappings target OpenCode V2 (verified on 2.2.7). V1 names such as the `task` tool, `subagent_type`, and the `bash` tool do not exist in V2.

## Tools

| Cursor | OpenCode |
| --- | --- |
| `Task` (spawn a subagent) | `subagent` tool with `agent` (the agent ID), `description` (3-5 words), and `prompt`. Optional: `model`, `background`, `sessionID`. See Subagent fields |
| `AskQuestion` | `question` tool. If the session has no `question` tool, ask in plain text. Subagents built on `general` cannot ask |
| Open a todolist | `todowrite` (enabled by `experimental.todo` in `.opencode/opencode.json`). Superpowers' startup text says OpenCode v2 has no todo tool; that is outdated here, so use `todowrite`. Without it, keep the list in your reply and update it at each phase |
| Run another pstack skill (`/name` or "the **name** skill") | `skill` tool with `id: "<name>"`. Most pstack skills set `disable-model-invocation: true`, so they are missing from the advertised skill list, but the tool still loads them by ID. If a call is refused, read `.opencode/skills/<name>/SKILL.md` instead. When you tell the user to type it, write `/<name>` (slash commands exist for every non-`principle-*` skill) |
| `Grep`, `Glob`, `Read` | `grep`, `glob`, `read` |
| `Shell`, `Bash` | `shell` |

## Subagent fields

| Cursor | OpenCode |
| --- | --- |
| `subagent_type: generalPurpose` | `agent: "general"` when the worker edits or needs MCP servers. `agent: "explore"` when it only reads |
| `subagent_type: "poteto-agent"`, `"comment-sicko"` | `agent: "poteto-agent"`, `agent: "comment-sicko"`. Both are registered subagents from `.opencode/agents/` |
| `readonly: true` | `agent: "explore"`. It can read, search, fetch, and run shell commands, but cannot edit, load skills, or use MCP tools. Its shell can still write, so the brief says "do not edit files" and you check `git status` afterward |
| `readonly: false` | `general` or `poteto-agent`. Both keep the session's MCP servers |
| `model: <slug>` | `model: "<provider/model#variant>"` from the role's line in `.opencode/pstack-models.md`, copied exactly. The role lines are the user's explicit model choices, so pass them even though the tool says to set `model` only on request. A short name such as `deepseek#max` fails with `Invalid model`. Omit `model` for `inherit-parent` or `auto`, and the subagent uses its own configured model or the parent's |
| `run_in_background: true` | `background: true`. The call returns at once and the parent is notified when the child finishes. Without it, the call waits for the result |
| `resume` / message / queue a follow-up on a running agent | Spawn a fresh subagent with consolidated scope (original brief, later directives, prior report and branch), per poteto-mode's Subagents section. Only when the work needs state that is costly to move (local checkout, uncommitted changes, a still-running process), continue that child by passing its returned `sessionID` |
| `environment: "cloud"` | Omit. Run locally; name the branch or commit the worker checks out |
| `cloud_base_branch: <b>` | Tell the worker to check out `<b>` first |
| `file_attachments` | List the paths in the brief |

Subagents cannot spawn subagents: nesting depth is 1 (`experimental.subagent_depth`), and `general` denies the `subagent` tool. A skill step that has a worker fan out again runs that fan-out from the parent session.

Parallel fan-out means every worker's `subagent` call in the same turn: calls in one turn run concurrently (verified on 2.2.7: three 15-second foreground workers started and finished together). When you wait on all of them, as in swarm, arena, interrogate, architect, how, why, and reflect, leave `background` off: the turn returns once with every report. Use `background: true` only when you have other work to do meanwhile; then each child sends a completion notice and wakes you. Do not sleep or poll. Foreground calls in separate turns run one after another. Only an agent whose mode is `subagent` or `all` can be spawned. A `primary` agent fails with "cannot run as a subagent".

## Models

| Cursor | OpenCode |
| --- | --- |
| `claude-<family>-<version>-<effort>[-fast]`, e.g. `claude-opus-5-5-xhigh` | `provider/model#variant` (see `opencode models`). The variant carries the effort; drop `-fast`. Variant names come from the model's catalog entry and differ per model, so use one the model lists. An unknown variant fails, and the error names the valid ones |
| A `grok-*`, `gpt-*`, or other non-Claude slug | The role's line in `.opencode/pstack-models.md`, or omit `model` when the line is missing |
| `auto` or `inherit-parent` | Omit `model` so the subagent runs on its configured model or the parent model |
| The `pstack-models.mdc` rule, `~/.cursor/rules/pstack-models.mdc` | `.opencode/pstack-models.md`, auto-loaded via `instructions` in `.opencode/opencode.json`. Read a role's line for its model; a missing file or line means `inherit-parent`. Never put role lines in `AGENTS.md` |
| A role value `<model> <effort>`, e.g. `opus high` | `provider/model#variant`, e.g. `opencode-go/deepseek-v4.1-flash#max` |

Raising or lowering a role's effort per poteto-mode's "Match the model to the job" changes only the `#variant`, never the model. The root `model` in `opencode.json` drops any `#variant`; select a variant for the session, an agent, a command, or a subagent call instead.

Budget ladder for `/setup-pstack` re-runs: `unlimited`, `large`, `medium`, and `small` target the `max`, `xhigh`, `high`, and `medium` variant of each real ID. When a model lacks the target, use its highest listed variant at or below it. Keep panel lists diverse (distinct models per entry); an `inherit-parent` entry still counts toward fan-out but never judges.

## setup-pstack

`.opencode/pstack-models.md` is auto-loaded into each session via `instructions`, so its role lines are already in context. If they are not, read the file. If the file or the role's line is missing, use `inherit-parent`. Follow the skill's steps with these changes:

- Detect. The detected models are the IDs listed by `opencode models` (plus the current session model). Each model's variants are in `opencode api GET /api/model` (field `variants`). `inherit-parent` and `auto` are always valid even though they are not model IDs.
- Defaults. Start from this file's role lines. There are no Cursor slugs to translate.
- Write. Overwrite `.opencode/pstack-models.md` wholesale: comment lines, the `# budget` line, one line per role. Never write role lines to `AGENTS.md`.
- Verify. Every real model ID written must appear in `opencode models`, and every `#variant` must be one that model lists. `inherit-parent` and `auto` always pass.

## Paths

| Cursor | OpenCode (this project) |
| --- | --- |
| `.cursor/skills/`, `~/.cursor/skills/` | `.opencode/skills/`. The superpowers and ponytail plugins add their own skills, which also load by ID |
| `~/.cursor/plugins/`, `.cursor-plugin/plugin.json` | `.opencode/` (project-local install; no versioned plugin registry, so the "highest version" tiebreak becomes "prefer the project-local copy") |
| `.cursor/rules/*.mdc`, always-applied rules | `.opencode/opencode.json` `instructions` (auto-loaded, e.g. `pstack-models.md`), plus `AGENTS.md` |
| `https://github.com/cursor/plugins/blob/main/pstack/<path>` | `.opencode/<path>` when that file exists (`skills/`, `docs/guide/`, `README.md`). Read the local copy, never fetch it. Give the user the URL when they need a link |
| Transcripts in `~/.cursor/projects/<slug>/agent-transcripts/` | OpenCode sessions: `opencode session list` (current project, newest first) and `opencode session export <id>`. Backed by the server database (see `opencode debug paths`). Read only this workspace's sessions |

## Cursor-only features

OpenCode has no Cursor Automations, Bugbot, Cursor cloud agents, Custom Modes, built-in `create-skill`, or the `control-cli` and `control-ui` skills from `cursor-team-kit`. `/loop` maps to `/goal` and `schedule_create`, as in the table. When a step needs one, put one line in your reply, `Skipped in OpenCode: <step>. <reason>.`, then go on to the next step. Do not stop, ask, or retry. When a later step needs what the skipped step made, such as a URL or a key, leave a placeholder and name it in the line.

| Cursor | OpenCode |
| --- | --- |
| Cursor Automations: routines, `update_state` routine calls, `api2.cursor.sh/automations` webhooks, routine wakes, and `SendToUser` secret requests | Skip with the line |
| Bugbot | Bugbot comments on a GitHub PR are ordinary review threads, so triage them with the forge command the step gives (`gh`). Skip a step that starts, waits for, or configures Bugbot when the PR has no Bugbot check or comment |
| Cursor cloud agents, `environment: "cloud"`, cloud dashboard status | Worktree/branch mapping, as in Subagent fields. Skip a step that needs the cloud agent itself, such as its dashboard status or its own machine |
| A built-in PR tool | Use the forge command the step gives, such as `gh pr create`. That is not a skip, so write no line |
| `/loop` (run until a predicate holds) | The user types the built-in `/goal <objective>`, which keeps the session working until the objective is met (`/goal pause`, `resume`, `clear`). Inside a run, wait on a predicate with `schedule_create`: it wakes this session on `every` or `cron`, or once when an `until` shell check (`{ run: "<cmd>" }`) passes, polling without model calls. Schedules need `experimental.schedules`, which `.opencode/opencode.json` turns on. Manage them with `schedule_list`, `schedule_pause`, `schedule_resume`, and `schedule_delete` |
| `make-bot-ui` (whole skill) | Cursor Automations-only (webhook routines, `update_state`, `api2.cursor.sh`). Skip entirely on OpenCode |
| Cursor's built-in `/babysit` (referenced by poteto-help as the non-poteto-mode route) | No equivalent installed. Route "babysit this PR" requests to `/poteto-mode`, which owns the Babysit playbook |
| Custom Modes (Option+Enter keeps `/poteto-mode` on) | No equivalent. Start each task with `/poteto-mode` |
| `/deslop` before commit (cursor-team-kit) | Installed at `.opencode/skills/deslop/` from upstream `cursor-team-kit`. Run `/deslop` as the playbooks prescribe |
| `control-cli` / `control-ui` for shipping UI/IDE/CLI | Skip with the line. Verify with this repo's own harness instead: `dotnet test` for the backend, Playwright or a CDP browser for the React dashboard, tmux or a PTY for CLIs |
| Cursor's built-in `create-skill` / `/create-skill` | Author `SKILL.md` by hand: folder name = skill name, frontmatter `name` + `description`, per the Agent Skills format. `automate-me` output skills go under `.opencode/skills/` |
