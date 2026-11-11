# pstack model configuration. One line per role. Delete a line to fall back to inherit-parent (the parent chat model).
# These role lines are the user's explicit model choices. When a skill spawns a subagent for a role, pass the value exactly as the subagent `model`.
# `inherit-parent` or `auto` as a value: the role runs on the parent chat model (omit the subagent `model`). Alias entries in a panel list still count toward its fan-out.
# Values are `provider/model#variant` (see `opencode models`; variants per model in `opencode api GET /api/model`). The variant carries the effort. Cursor slugs (claude-opus-5-5-xhigh, grok-4.7-xhigh-fast) and short names (deepseek#max, inkling#max) are not valid here.
# budget: large (xhigh)
# chores (kill, clean, build, relaunch, shell checks; dotnet and bun formatting; package upgrades, outdated checkups, lockfile refresh; git pull, git push): opencode/beam-free#max
# thinking: muse-spark runs #high, inkling-2-small-free runs #max, deepseek-v4.1-flash runs #max. DeepSeek carries hardest tasks, why synthesizer, architect runners, arena cross-judge pool, and interrogate reviewers.
# parent session: run the interactive session on opencode-go/muse-spark-1.3-contributor#high; role lines below only affect subagents.
feature, refactoring: opencode/inkling-2-small-free#max
bug-fix: opencode/inkling-2-small-free#max
perf-issue: opencode/inkling-2-small-free#max
hillclimb: opencode/inkling-2-small-free#max
judgment and prose: opencode-go/muse-spark-1.3-contributor#high
hardest tasks: opencode-go/deepseek-v4.1-flash#max
how explorer: opencode/inkling-2-small-free#max
how explainer: opencode-go/muse-spark-1.3-contributor#high
why investigators: opencode/inkling-2-small-free#max
why synthesizer: opencode-go/deepseek-v4.1-flash#max
reflect tooling: opencode/inkling-2-small-free#max
reflect judgment, divergent, synthesizer: opencode-go/muse-spark-1.3-contributor#high
arena runners: opencode/inkling-2-small-free#max, opencode-go/muse-spark-1.3-contributor#high
arena cross-judge pool: opencode-go/muse-spark-1.3-contributor#high, opencode-go/deepseek-v4.1-flash#max
swarm workers: opencode/inkling-2-small-free#max
architect runners: opencode-go/deepseek-v4.1-flash#max, opencode-go/muse-spark-1.3-contributor#high
interrogate reviewers: opencode-go/deepseek-v4.1-flash#max, opencode-go/muse-spark-1.3-contributor#high
