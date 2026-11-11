# AGENTS.md

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
