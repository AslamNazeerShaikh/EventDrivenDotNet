---
name: working-lean-with-poteto
description: Use when a /poteto-mode prompt says lean or yagni, or a task involves a bug, flake, multi-call-site change, or abstraction and dependency temptation under time pressure
---

# Working Lean With Poteto

## Overview

Poteto routes. Ponytail bounds diff. Superpowers gates quality. Gates are superpowers-plugin skills; `ponytail` repo-local. Agent loads by ID.

**REQUIRED:** poteto-mode; `ponytail` full first, each fired gate by ID; TDD every change; model from `pstack-models.md`.

## When to Use

- `/poteto-mode` plus bug, flake, or timing.
- 2+ call sites, or tempts abstraction/dependency.
- Demo/deadline pressure.
- Prompt says `lean`, `yagni`, or debugging.

Not: read-only investigation, one-line edit.

## Core Pattern

- `lean` means invoke `ponytail` full first.
- Trivial no-code ops (git moves, deletes, status, one-shot shell): skip playbooks; run inline, no subagent; two-line confirm.
- Bug/flake: invoke `systematic-debugging` first.
- Feature/plan: invoke `brainstorming`, smallest only.
- Every change: RED first per `test-driven-development`, one watched fail-pass test.
- Done: invoke `verification-before-completion` on artifact.
- Spawn poteto-agent delegates, general for MCP, explore read-only. Briefs carry this file + role model. Model is role line `provider/model#variant`; omit when missing/`inherit-parent`/`auto`.

## Quick Reference

| Poteto step | Required gate |
|-------------|---------------|
| Feature/Bug/Perf | state data shape first, architect only across boundary |
| Bug/flake | trace to first wrong state, fix once shared |
| Prototype fork | sketch decides, never AskQuestion |
| Diff | smallest reuse: helper, stdlib, dep, else minimum |
| Commit/review | deslop, no-comments, verification run |
| Models (delegated) | hardest/deepseek#max; feature, bug-fix, perf-issue/inkling#max; chores/beam-free#max; missing omits model |
| Fix loop | Critical/Important/spec-miss: resume 1-3, fresh+bump 4-5, max 5, tests+report+re-review; contested: one bakeoff; unclear: decide+ledger or ask once; Minor: deferred |
| Plan gate | 2+ tasks/vague/shared names: names + Consumes/Produces + test first; one-line skips; coupled sequential |

## Implementation

1. Match playbook; open steps. Sync first: `git fetch origin`; pull --ff-only development/main when clean before use. Plan gate: names + tests first; conflicts as `Ruling: <decided> — <why>`.
2. Bug/flake: trace to first wrong state, grep callers.
3. Smallest reuse-first diff; deletion wins.
4. One test for non-trivial logic; mark `shortcut: <limit>, <when to upgrade>`.
5. Verify on real harness. End with skipped items and risks.
6. Numbers carry commands: every number comes from a command run this session; remote claims need a fetch plus API read-back.

## Common Mistakes

- Two slashes. Fix: `/poteto-mode` plus `lean`.
- Scope excuse. Fix: scope bounds deliverable, never verification.
- Future-proofing. Fix: duplication first, extract on reuse.
- Tests after. Fix: delete, restart RED.

## Rationalizations

| Excuse | Reality |
|--------|---------|
| "Only poteto asked" | Sequence, not waiver. |
| "Demo urgency justifies ship now" | Urgency shrinks change, never skips gates. |
| "Big abstraction is safer" | Speculation. Reuse wins. |
| "Tests after is fine" | Memory, not proof. |

## Red Flags - STOP

- "ship as-is", "clean later", "only poteto was asked", "spirit not letter"
- New dep for lines
