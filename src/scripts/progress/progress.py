#!/usr/bin/env python3
"""progress.py: edit docs/04-build-progress.md without hand-written regex.

The progress doc is the handover point between agents (Claude Code,
opencode). It is updated after every build step, so these edits are scripted.

Usage:
  python3 src/scripts/progress/progress.py updated "<text>"
      Set the text after "Last update: " in the header quote.

  python3 src/scripts/progress/progress.py row <ID> [--name N] [--status S] [--commits C] [--review R] [--after ID2]
      Update the status-table row whose first cell is <ID> (e.g. 13, 7b, R1).
      If the row does not exist, insert it after row ID2 (required then).
      Commits: pass e.g. "`a1b2c3d`, `e4f5a6b`" (Markdown code spans).

  python3 src/scripts/progress/progress.py now "<text>"
      Replace handover step 3 ("**Right now:** ...") with "<text>".

  python3 src/scripts/progress/progress.py tests "<text>"
      Replace the line that starts "Tests on the branch:".

  python3 src/scripts/progress/progress.py self-test
      Run the built-in checks on a temporary copy (the real file is untouched).

Options:
  --file PATH   edit another file (default: <repo>/docs/04-build-progress.md)

Examples:
  python3 src/scripts/progress/progress.py updated "13 November 2026, Task 16 in progress."
  python3 src/scripts/progress/progress.py row 16 --status "**In progress**"
  python3 src/scripts/progress/progress.py row 13b --name "Fix: verdict flicker" --status Done --commits "\\`bbb47ca\\`" --review Clean --after 13
  python3 src/scripts/progress/progress.py now "Task 16 is being built from task-16-brief.md."
"""
from __future__ import annotations

import argparse
import pathlib
import re
import subprocess
import sys
import tempfile


def repo_root() -> pathlib.Path:
    here = pathlib.Path(__file__).resolve().parent
    out = subprocess.run(["git", "-C", str(here), "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=True)
    return pathlib.Path(out.stdout.strip())


def set_updated(text: str, value: str) -> str:
    new, n = re.subn(r"(Last update: )[^\n]*", lambda m: m.group(1) + value, text, count=1)
    if n != 1:
        raise SystemExit("progress: no 'Last update:' line found")
    return new


def set_row(text: str, row_id: str, name=None, status=None, commits=None, review=None, after=None) -> str:
    lines = text.split("\n")
    prefix = f"| {row_id} |"
    idx = next((i for i, l in enumerate(lines) if l.startswith(prefix)), None)
    if idx is None:
        if after is None or name is None:
            raise SystemExit(f"progress: row '{row_id}' not found (to insert it, pass --name and --after)")
        anchor = next((i for i, l in enumerate(lines) if l.startswith(f"| {after} |")), None)
        if anchor is None:
            raise SystemExit(f"progress: anchor row '{after}' not found")
        cells = [row_id, name, status or "To do", commits or "—", review or "—"]
        lines.insert(anchor + 1, "| " + " | ".join(cells) + " |")
        return "\n".join(lines)
    # Split on " | " boundaries of a 5-column row: | id | name | status | commits | review |
    inner = lines[idx].strip()[1:-1]
    cells = [c.strip() for c in re.split(r"\s\|\s", " " + inner + " ")]
    cells = [c for c in cells if c != ""] if len(cells) > 5 else cells
    if len(cells) != 5:
        raise SystemExit(f"progress: row '{row_id}' does not have 5 cells: {lines[idx]}")
    for i, v in ((1, name), (2, status), (3, commits), (4, review)):
        if v is not None:
            cells[i] = v
    lines[idx] = "| " + " | ".join(cells) + " |"
    return "\n".join(lines)


def set_now(text: str, value: str) -> str:
    new, n = re.subn(r"(3\. \*\*Right now:\*\* )[^\n]*", lambda m: m.group(1) + value, text, count=1)
    if n != 1:
        raise SystemExit("progress: no '3. **Right now:**' step found")
    return new


def set_tests(text: str, value: str) -> str:
    new, n = re.subn(r"^Tests on the branch:[^\n]*", lambda m: "Tests on the branch: " + value, text, count=1, flags=re.M)
    if n != 1:
        raise SystemExit("progress: no 'Tests on the branch:' line found")
    return new


SAMPLE = """> Updated after every step. Last update: old text.

| # | Task | Status | Commits | Review |
| --- | --- | --- | --- | --- |
| 1 | Contracts | Done | `dc39237` | Clean |
| 2 | Compose | To do | — | — |

Tests on the branch: 1 passing.

3. **Right now:** old step.
4. Next.
"""


def self_test() -> int:
    t = set_updated(SAMPLE, "new text.")
    assert "Last update: new text." in t
    t = set_row(t, "2", status="Done", commits="`abc1234`", review="Clean")
    assert "| 2 | Compose | Done | `abc1234` | Clean |" in t
    t = set_row(t, "2b", name="Fix: x | y-safe", status="**In progress**", after="2")
    assert t.index("| 2 | Compose") < t.index("| 2b | Fix: x | y-safe | **In progress** | — | — |")
    t = set_now(t, "new step.")
    assert "3. **Right now:** new step.\n4. Next." in t
    t = set_tests(t, "5 passing.")
    assert "Tests on the branch: 5 passing." in t
    try:
        set_row(t, "99", status="x")
        raise AssertionError("expected missing-row error")
    except SystemExit:
        pass
    with tempfile.NamedTemporaryFile("w", suffix=".md", delete=False) as f:
        f.write(SAMPLE)
    pathlib.Path(f.name).unlink()
    print("progress self-test: ok")
    return 0


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("command", choices=["updated", "row", "now", "tests", "self-test"])
    p.add_argument("args", nargs="*")
    p.add_argument("--file")
    p.add_argument("--name"); p.add_argument("--status"); p.add_argument("--commits"); p.add_argument("--review"); p.add_argument("--after")
    a = p.parse_args()
    if a.command == "self-test":
        return self_test()

    path = pathlib.Path(a.file) if a.file else repo_root() / "docs" / "04-build-progress.md"
    text = path.read_text()
    if a.command == "updated":
        text = set_updated(text, " ".join(a.args))
    elif a.command == "row":
        if not a.args:
            raise SystemExit("progress row: missing <ID>")
        text = set_row(text, a.args[0], a.name, a.status, a.commits, a.review, a.after)
    elif a.command == "now":
        text = set_now(text, " ".join(a.args))
    elif a.command == "tests":
        text = set_tests(text, " ".join(a.args))
    path.write_text(text)
    print(f"progress: {a.command} applied to {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
