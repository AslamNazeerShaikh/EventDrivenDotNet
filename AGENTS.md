## Architecture

- No CQRS command/query buses, no MediatR/mediator, no AutoMapper, and no generic repositories.
- Use explicit constructor-injected use-case handlers; call them directly from CLI commands and API endpoints.
- Use the project patterns: `Result<T>` for expected failures, validated Options at the composition root, Specifications for reusable query criteria, and RFC 9457 Problem Details at the API edge.
- Read [docs/14-application-patterns.md](docs/14-application-patterns.md) before adding an application abstraction or error contract.
- Runtime rules: Serilog message templates and redaction, global exception handling, `IHttpClientFactory` + Polly for HTTP, `CancellationToken` on every I/O boundary, `using`/`await using`, bounded queues, disk checks. See [docs/15-runtime-hardening.md](docs/15-runtime-hardening.md).
- Library rule: use each package’s documented, recommended configuration. Do not remove or restrict a library feature because of AOT/trimming. Serilog, DataAnnotations, ClosedXML, Scalar, EF Core, and Playwright are used as designed. See [docs/16-library-usage-policy.md](docs/16-library-usage-policy.md).

## Commits

Follow [Conventional Commits v1.0.0](https://www.conventionalcommits.org/en/v1.0.0/#specification); the project policy is [docs/13-conventional-commits.md](docs/13-conventional-commits.md).

- Format: `<type>(<scope>)!: <description>`; one logical change per commit.
- Allowed types: `build`, `chore`, `ci`, `docs`, `feat`, `fix`, `perf`, `refactor`, `revert`, `style`, `test`.
- Imperative, lowercase description with no trailing period; target 72 characters, hard maximum 100.
- Put the why in the body after one blank line; reference issues in footers (`Refs: #42`).
- Enable the local validator once per clone: `git config core.hooksPath .githooks`.
- Never bypass the hook to avoid fixing a message; fix the message or the hook.
- PR titles follow the same format because squash merges use them as the commit header.

## UI design

- When designing or developing UI in `src/frontend/`, consult the ui-skills catalog first: `list_skills` filtered by topic/intent, then `get_skill` for the smallest useful set. Start with the `ibelick/ui-skills-root` router skill when unsure which applies.
- Project-local UI skill pack also installed (`.opencode/skills/ui-ux-pro-max/`, plus `design`, `design-system`, `ui-styling`, `slides`, `banner-design`, `brand`): use `/ui-ux-pro-max` as orchestrator, or the sibling directly. Data lookups run via `python3 .opencode/skills/<skill>/scripts/search.py` (stdlib only). Image-generation steps in `banner-design`/`design` need the author's sibling skills (`frontend-design`, `ai-artist`) + API keys and are skipped without them. See [docs/18-harness-packs.md](docs/18-harness-packs.md).
- Design baseline is 1920×1080 (16:9, ~141 PPI); fluid type/layout bottoms out below and ceilings at the baseline. See `src/frontend/README.md` § Design baseline.
- Reference: [UI Skills](https://www.ui-skills.com/skills) · [MCP docs](https://www.ui-skills.com/mcp/docs).

## Agent workflow (pstack)

- pstack is installed project-locally (`.opencode/skills/`, `.opencode/agents/`, `.opencode/commands/`). See [docs/17-pstack-opencode.md](docs/17-pstack-opencode.md).
- For any non-trivial task, start with `/poteto-mode`: it matches the request to a playbook and routes to the other skills as steps need them.
- Role-to-model mapping lives in `.opencode/pstack-models.md` (auto-loaded). A step needing a Cursor-only feature (Bugbot, Automations, cloud agents) is skipped with a one-line `Skipped in OpenCode:` note per `.opencode/docs/harness/opencode.md`.
- Choose the subagent model from `.opencode/pstack-models.md` by role; a role with no line runs on the parent chat model, and the brief says so explicitly with `inherit-parent` — no brief leaves it blank.
- Execution defaults (binding): plan spec-first with `writing-plans` (investigation → spec → caveats → task list with one test per task; or the lean plan gate in `working-lean-with-poteto`); then execute with SDD — `subagent-driven-development` — when tasks are independent (`executing-plans` inline when they are coupled); implement with TDD — `test-driven-development` — inside each task brief, RED-first per behavior change; state the role and its model — or `inherit-parent` — in every brief; read-only reviews and next-task setup may overlap, but implementers run one at a time per plan. Full ceremony only where it earns its place; trivial edits go direct.
- Pre-dispatch check (binding — answer before every execution; record in the brief, or in reply for direct work): role and model from `.opencode/pstack-models.md`, or explicit `inherit-parent`. Never dispatch on an unnamed default.
- SDD task with brief, or trivial edit going direct — and if direct, one-line why. No third path: another planning flow (`superpowers` brainstorming/plans, ad-hoc planning) does not substitute for `writing-plans` plus a task brief.
- For dispatched work: name what runs in parallel with this step and what waits on it.
- Any of the three missing — role/model, execution route, parallel plan — stop and resolve it before executing.
- `deslop` ships from `cursor-team-kit` (`.opencode/skills/deslop/`, `/deslop` command).

## ECC (secondary workflow pack)

- ECC commands live in `.opencode/commands/` (`/plan`, `/code-review`, `/security`, `/build-fix`, `/e2e`, `/refactor-clean`, `/orchestrate`, `/learn`, `/checkpoint`, `/verify`, `/eval`, `/update-docs`, `/test-coverage`, `/harness-audit`, `/quality-gate`, `/model-route`, `/loop-start`, `/loop-status`, `/skill-create`) backed by 8 agents in `.opencode/opencode.json` (`planner`, `code-reviewer`, `security-reviewer`, `tdd-guide`, `build-error-resolver`, `e2e-runner`, `doc-updater`, `refactor-cleaner`) and consolidated rules in `.opencode/instructions/INSTRUCTIONS.md` (auto-loaded). No plugin hooks/tools (not built); instinct system, language-specific agents/reviewers (go/rust/java/kotlin/cpp/php/ruby), `setup-pm`, `security-scan` (needs AgentShield), and ECC `/tdd` (name owned by pstack) are skipped. See [docs/18-harness-packs.md](docs/18-harness-packs.md).
- `/tdd` is the pstack skill; ECC's TDD agent is reachable via `/test-coverage` and the `tdd-guide` subagent.

## OpenCode config scope

- OpenCode configuration for this repo is project-local only: `.opencode/` and this file. There is no global `~/.config/opencode/` config (no global skills, agents, commands, plugins, or instructions), so sessions here see only what the repo ships.
- Repo rule: responses stay terse (answer first, no ceremony); code, comments, commits, PRs, and docs stay normal prose.

## Include tables in responses

- When a reply carries comparable numbers, options, or findings a reader would line up by hand, use one or more detailed tables with multiple columns when the table fits.
- Number every row with an index column so each item stays referenceable in the surrounding prose.
- When the content is a single fact, a narrative, or a judgment with no peers, keep prose. Never force content into a table to decorate it.
- Note this only adds or guides how to include tables in responses and it does not change any skill's or agent's own response format. Means I need skill's or agent's original response plus this "add tables instruction" in responses/outputs but smartly and not in every time.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- When graphify-out/graph.json exists, run `graphify query "<question>"` before your first grep, search or source-file read for a codebase question, and put this rule in every subagent prompt that explores code. The graph also indexes the full `.opencode/` corpus (every skill, playbook, reference, command, agent, and the harness docs), so skill discovery ("which skill/command achieves X") goes through `graphify query` too, then read the located `SKILL.md` in full before invoking it. Read raw files only for lines the graph or an earlier read already located. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
- A committed graphify-out/graph.json is rewritten whole on every build, so a large diff to it in a pull request is expected. Do not flag it as churn or suggest skipping the graph rebuild or its hooks.
