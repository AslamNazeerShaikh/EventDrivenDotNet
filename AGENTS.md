# AGENTS.md

## Include tables in responses

- When a reply carries comparable numbers, options, or findings a reader would line up by hand, use one or more detailed tables with multiple columns when the table fits.
- Number every row with an index column so each item stays referenceable in the surrounding prose.
- When the content is a single fact, a narrative, or a judgment with no peers, keep prose. Never force content into a table to decorate it.
- Note this only adds or guides how to include tables in responses and it does not change any skill's or agent's own response format. Means I need skill's or agent's original response plus this "add tables instruction" in responses/outputs but smartly and not in every time.

## Subagents

- A loaded skill that prescribes subagents, delegation, or parallel workers is an explicit request for them. This covers the pstack skills (such as `poteto-mode`, `swarm`, `arena`, `interrogate`, `architect`, `how`, `why`, `reflect`, `no-comments`) and the superpowers skills (such as `dispatching-parallel-agents`, `subagent-driven-development`). Spawn the workers the skill names.
- Launch independent workers in one turn so they run in parallel. When you wait on all of them, leave OpenCode's `background` off so they return together; use `background: true` only when you have other work to do meanwhile. Collect every report before acting on them.
- Per-role subagent models in `.opencode/pstack-models.md` are the user's explicit choices. Pass the role's value exactly as written.

## Commits

Follow [Conventional Commits v1.0.0](https://www.conventionalcommits.org/en/v1.0.0/).

- Format: `<type>[optional scope][!]: <description>`, blank line, then optional body and footers.
- `feat`: new feature (→ MINOR). `fix`: bug fix (→ PATCH).
- Other types allowed: `build`, `chore`, `ci`, `docs`, `style`, `refactor`, `perf`, `test`, `revert`.
- Breaking change (→ MAJOR): `!` before the colon, e.g. `feat(api)!:`, and/or a `BREAKING CHANGE:` footer. Footer token stays uppercase.
- Scope is a noun in parens, e.g. `docs(diagrams):`.
- Description is imperative, lowercase, no trailing period — a short summary of the change.
- Body explains what and why; footers use trailer format, e.g. `Refs: #42`.
- One logical change per commit; case-insensitive except `BREAKING CHANGE`.
