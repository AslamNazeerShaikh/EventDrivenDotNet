---
name: working-lean-with-poteto
description: Use when a /poteto-mode prompt says lean or yagni, or a task involves a bug, flake, multi-call-site change, or abstraction and dependency temptation under time pressure
---

# Working Lean With Poteto

## Overview

Poteto routes. Ponytail bounds diff. Superpowers gates quality. One owner per concern, sized to the task. Load each skill by ID at the step it fires, once per session.

Ponytail needs no load: its plugin injects the `full` ruleset into every turn of every session, subagents included. Models come from `.opencode/pstack-models.md`.

## Size the task first

State the tier in your first message. Re-tier when evidence changes it.

| Tier | Signal | Load, at the step | Subagents |
|------|--------|-------------------|-----------|
| T0 chore | run/stop/kill, build, git sync, format, package upgrade, PR/CI status | nothing | one `general` on the `chores` line model; no playbook, todo list, or gates; two-line relay |
| T0 trivial | question, one-line edit | nothing | none; inline, two-line confirm |
| T1 small | one file, cause known, obvious test | `test-driven-development`, then `verification-before-completion` | none |
| T2 medium | 2+ files or call sites, unknown cause, flake, perf | T1 + `systematic-debugging` when cause unknown | investigation recipe (Subagents) when it needs 3+ file reads; you write the code |
| T3 large | the deliverable is a design (new component, protocol, or data flow), whatever its file count; crosses a boundary; 2+ viable designs; multi-PR | T2 + the design recipe (Subagents) for any design deliverable, whichever playbook matched; `brainstorming` (requirements unclear) | design-recipe runners; `poteto-agent` per independent slice, own worktree; `swarm` verify; `interrogate` review |
| Plan | a written plan with tasks | `subagent-driven-development` | one task at a time |

## One owner per concern

| Concern | Owner | Never |
|---------|-------|-------|
| Diff size | ponytail (injected); `deslop` at commit | loading `ponytail` |
| Tests | `test-driven-development`: every behavior change RED first, one watched fail-pass test; config, docs, generated code: no test | pstack `tdd` |
| Unknown-cause bug | `systematic-debugging` | — |
| Done claims | `verification-before-completion` | a `principle-prove-it-works` load |
| Fan-out | `swarm` (coverage, races), `arena` (design bakeoff), `interrogate` (adversarial review) | `dispatching-parallel-agents`: no role models or diverse panels |
| Plans | multi-task plan saved to a file to hand off: `writing-plans`, then `subagent-driven-development`; a plan in your reply: no skill | the multi-phase playbook |
| Review | `no-comments`; `interrogate` at T3 | `requesting-code-review` |

Use `poteto-mode`'s inline principle index; open a `principle-*` leaf only when a step names it.

## Subagents

- Spawn with the `subagent` tool. Calls in one turn run concurrently. Waiting on all of them (investigation, swarm, verification): foreground, so they return together and you wake once. Other work to do meanwhile: `background: true`, then wait for the notices; never message a running child.
- Investigation recipe: (1) list directories, nothing else; (2) one `explore` per area in one turn, on the `how explorer` model, each brief naming its area and the question; (3) answer from the reports, opening a file only to check a line a report cites. When the `how explainer` model is your own model, skip `how`'s explainer spawn and write its sections yourself; a same-model explainer only re-reads the reports.
- Design recipe (T3): (1) load `architect`, then ground with the investigation recipe; (2) one runner subagent per entry on the `architect runners` line, all in one turn, each sketching the whole design from the same brief; (3) compare the sketches in a table, pick or graft, then write the chosen sketch. A prototype or no-implementation request still runs step 2: runners sketch, they never implement.
- After the reports, load only the skill the next step names.
- Workers (explorers, swarm, verification): `explore` read-only or `general`, self-contained brief, no skill files. `poteto-agent` only for T3 code slices: it reads `poteto-mode` (~9k tokens) before working. Its brief carries this file's path and the role model.
- Model: the role line, copied exactly (`provider/model#variant`); omit when missing/`inherit-parent`/`auto`. Lower the `#variant` for routine work.

## Gates

| Step | Gate |
|------|------|
| Feature/Bug/Perf | state data shape first, architect only across boundary |
| Bug/flake | trace to first wrong state, grep callers, fix once shared |
| Prototype fork | sketch decides, never AskQuestion |
| Diff | smallest reuse: helper, stdlib, dep, else minimum; deletion wins |
| Commit/review | deslop, no-comments, verification run |
| Fix loop | Critical/Important/spec-miss: resume (same `sessionID`) 1-3, fresh+bump 4-5, max 5, tests+report+re-review; contested: one bakeoff; unclear: decide+ledger or ask once; Minor: deferred |
| Plan gate | 2+ tasks/vague/shared names: names + Consumes/Produces + test first; one-line skips; coupled sequential |

## Implementation

1. Size, then match playbook; open steps. Sync first: `git fetch origin`; pull --ff-only development/main when clean before use. Conflicts as `Ruling: <decided> — <why>`.
2. One test for non-trivial logic; mark `shortcut: <limit>, <when to upgrade>`.
3. Verify on real harness. End with skipped items and risks.
4. Numbers carry commands: every number comes from a command run this session; remote claims need a fetch plus API read-back.

## Common Mistakes

- Two slashes. Fix: `/poteto-mode` plus `lean`.
- Scope excuse. Fix: scope bounds deliverable, never verification.
- Future-proofing. Fix: duplication first, extract on reuse.
- Tests after. Fix: delete, restart RED.
- Loading every gate upfront. Fix: load at the step, by tier.
- Scouting files, then fanning out over the same areas. Fix: the investigation recipe.

## Rationalizations

| Excuse | Reality |
|--------|---------|
| "Only poteto asked" | Sequence, not waiver. |
| "Demo urgency justifies ship now" | Urgency shrinks change, never skips gates. |
| "Big abstraction is safer" | Speculation. Reuse wins. |
| "Tests after is fine" | Memory, not proof. |
| "Lean means skip architect/arena" | Lean bounds the diff, never T3's design gate. Only a no-edit request skips the sketch and bakeoff, and you say so. |
| "A bakeoff is excess for one file; I'll write both candidates" | One model writing two candidates is one opinion. Runners on their role lines are the point of T3. |

## Red Flags - STOP

- "ship as-is", "clean later", "only poteto was asked", "spirit not letter"
- New dep for lines
