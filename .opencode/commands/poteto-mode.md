---
description: "poteto's agent style for concise, detailed responses, deliberate subagents, unslopped prose, simple code, and verified work. Use for poteto, /poteto-mode, or requests to work in this style."
---

First decide whether the user request below is a chore: start, stop, or restart a server; kill a process; build; git fetch, pull, push, or status; format or lint; upgrade packages or refresh a lockfile; a one-shot PR or CI status check; or another one-shot shell task with no code change to design.

If it is a chore, skip the rest of this command and load no skill. Spawn one `general` subagent with the `subagent` tool and wait for its result. Set `model` to the value of the `chores:` line in `.opencode/pstack-models.md`, exactly as written. That line is the user's explicit model choice, so always pass it; omit `model` only when the line is missing. Give it a self-contained brief: the goal, the repository path, and these rules. Run only what the chore needs. Do nothing destructive beyond the request: no force push, no `reset --hard`, and kill only the process the request names. Start long-running servers with the shell tool's `background` option and report the port or PID. Report each command with its result. Then relay the result in at most two lines. If the chore fails, report the failure and stop; a failure that needs a code change is a new task.

Otherwise, load the `poteto-mode` skill with the skill tool and follow it exactly. If the request says `lean` or `yagni`, also load `working-lean-with-poteto` right after it. Before following them, read `.opencode/docs/harness/opencode.md` once per session for the Cursor to OpenCode mappings (tools, subagents, models, paths, skipped Cursor-only steps).

User request: $ARGUMENTS
