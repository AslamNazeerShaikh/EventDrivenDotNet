#!/usr/bin/env python3
"""screenshot.py: screenshot a web page or local HTML file with Playwright.

Used to look at diagrams and the dashboard without opening a browser:
render, crop to the part that matters, then view the PNG. Keep PNGs out of
the repo (write them to /tmp or the git-ignored .superpowers/ folder).

Usage:
  python3 src/scripts/diagrams/screenshot.py TARGET OUT.png [options]

  TARGET           a URL (http://localhost:3000) or a path to a .html file
  --width W        viewport width in CSS px (default 1440)
  --height H       viewport height (default 900)
  --scale S        device scale factor (default 1; use 2 for crisp text)
  --full           capture the full page height
  --svg            clip to the first <svg> element (diagram pages)
  --clip X,Y,W,H   clip a rectangle, in CSS px relative to the page, or to
                   the <svg> when --svg is also given
  --wait MS        extra wait after load, for fonts and polling UIs (default 1500)

Requires: pip install playwright && playwright install chromium

Examples:
  python3 src/scripts/diagrams/screenshot.py http://localhost:3000 /tmp/dash.png --width 390 --height 844 --full
  python3 src/scripts/diagrams/screenshot.py docs/diagrams/01-plant-to-central.html /tmp/top.png --svg --clip 0,0,1440,920
"""
import argparse
import pathlib
import sys


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("target")
    p.add_argument("out")
    p.add_argument("--width", type=int, default=1440)
    p.add_argument("--height", type=int, default=900)
    p.add_argument("--scale", type=float, default=1)
    p.add_argument("--full", action="store_true")
    p.add_argument("--svg", action="store_true")
    p.add_argument("--clip")
    p.add_argument("--wait", type=int, default=1500)
    a = p.parse_args()

    from playwright.sync_api import sync_playwright  # imported late so --help works without it

    target = a.target
    if not target.startswith(("http://", "https://", "file://")):
        target = pathlib.Path(target).resolve().as_uri()

    with sync_playwright() as pw:
        browser = pw.chromium.launch()
        page = browser.new_page(viewport={"width": a.width, "height": a.height}, device_scale_factor=a.scale)
        page.goto(target)
        page.wait_for_timeout(a.wait)
        kwargs = {"path": a.out, "full_page": a.full}
        ox = oy = 0.0
        if a.svg:
            box = page.locator("svg").first.bounding_box()
            if box is None:
                print("no <svg> on the page", file=sys.stderr)
                return 1
            ox, oy = box["x"], box["y"]
            kwargs.update(full_page=True, clip={"x": ox, "y": oy, "width": box["width"], "height": box["height"]})
        if a.clip:
            x, y, w, h = (float(v) for v in a.clip.split(","))
            kwargs.update(full_page=True, clip={"x": ox + x, "y": oy + y, "width": w, "height": h})
        page.screenshot(**kwargs)
        browser.close()
    print(f"wrote {a.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
