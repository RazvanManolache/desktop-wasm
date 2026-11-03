"""Browser regression for the catalog and both mounted demo studios.

Run against the real deployment, not mocked HTTP responses. Screenshots and
console/network failures go to --output. Requires the existing Playwright install.
"""
import argparse
import json
import time
from io import BytesIO
from pathlib import Path

from playwright.sync_api import sync_playwright
from PIL import Image, ImageChops


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default="http://127.0.0.1:5220")
    parser.add_argument("--output", default=".artifacts/demo-repair/browser")
    parser.add_argument("--cases", default="catalog,winforms,wpf,paint,hello-real")
    args = parser.parse_args()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    results = []
    with sync_playwright() as pw:
        browser = pw.chromium.launch(headless=True, args=["--enable-unsafe-swiftshader"])
        cases = {"catalog": "/", "winforms": "/app/smoke", "wpf": "/demo/input",
                 "paint": "/app/paint", "hello-real": "/app/hello-real",
                 "autocomplete": "/launch/syncfusion-auto-complete"}
        for name in args.cases.split(","):
            route = cases[name]
            context = browser.new_context(viewport={"width": 1440, "height": 1000})
            page = context.new_page()
            failures, errors = [], []
            page.on("response", lambda r: failures.append({"url": r.url, "status": r.status}) if r.status >= 400 else None)
            page.on("pageerror", lambda e: errors.append(str(e)))
            page.on("console", lambda m: print(name, "CONSOLE", m.type, m.text[:240], flush=True) if m.type == "error" else None)
            started = time.monotonic()
            print("START", name, route, flush=True)
            if name in ("winforms", "wpf"):
                page.goto(args.base + "/", wait_until="domcontentloaded", timeout=60000)
                page.get_by_role("link", name="Windows Forms Studio" if name == "winforms" else "WPF Studio", exact=True).click(timeout=30000)
                page.wait_for_selector("#shell-phase" if name == "winforms" else "#status", timeout=30000)
                if name == "wpf":
                    page.goto(args.base + route, wait_until="domcontentloaded", timeout=60000)
            else:
                page.goto(args.base + route, wait_until="domcontentloaded", timeout=60000)
            if name == "autocomplete":
                page.get_by_text("Ready to open", exact=True).wait_for(timeout=60000)
                if page.locator('[role="alert"]').count():
                    errors.append("Launch page showed an unavailable alert for a ready host")
                page.screenshot(path=str(output / "autocomplete-launch.png"))
                page.get_by_role("link", name="Open app", exact=True).click()
            ready = False
            while time.monotonic() - started < 150:
                if name == "catalog":
                    ready = page.locator(".catalog li").count() > 0
                else:
                    ready = any(frame.locator("canvas").count() > 0 for frame in page.frames)
                    # A canvas is allocated before its first rendered frame.
                    phase = page.locator("#shell-phase" if name in ("winforms", "paint", "autocomplete") else "#status")
                    text = phase.inner_text(timeout=3000) if phase.count() else ""
                    print(name, "PHASE", round(time.monotonic() - started), repr(text), "canvas", ready, flush=True)
                    ready = ready and ("ready" in text.lower() or "running" in text.lower() or "live" in text.lower())
                if ready or errors:
                    break
                page.wait_for_timeout(5000)
            page.screenshot(path=str(output / (name + ".png")), timeout=60000)
            interaction = None
            if name in ("winforms", "wpf") and ready:
                for frame in page.frames:
                    if frame.locator("canvas").count():
                        box = frame.locator("canvas").first.bounding_box()
                        # Fixed 1440x1000 regression viewport; inspect only the
                        # counter pixels, excluding the hovered button/metrics.
                        dx, dy, cx, cy = (368, 415, 440, 402) if name == "winforms" else (545, 70, 95, 20)
                        clip = {"x": box["x"] + cx, "y": box["y"] + cy, "width": 130, "height": 35}
                        before = page.screenshot(clip=clip)
                        page.mouse.click(box["x"] + dx, box["y"] + dy)
                        page.wait_for_timeout(750)
                        after = page.screenshot(clip=clip)
                        interaction = ImageChops.difference(Image.open(BytesIO(before)).convert("RGB"), Image.open(BytesIO(after)).convert("RGB")).getbbox() is not None
                        page.screenshot(path=str(output / (name + "-clicked.png")))
                        break
            if name == "paint" and ready:
                clip = {"x": 700, "y": 450, "width": 300, "height": 160}
                before = page.screenshot(clip=clip)
                page.mouse.move(720, 470)
                page.mouse.down()
                page.mouse.move(970, 580, steps=25)
                page.mouse.up()
                page.wait_for_timeout(1000)
                drawn = page.screenshot(clip=clip)
                page.screenshot(path=str(output / "paint-drawn.png"))
                page.keyboard.press("Control+z")
                page.wait_for_timeout(1000)
                undone = page.screenshot(clip=clip)
                page.screenshot(path=str(output / "paint-undone.png"))
                def changed(a, b):
                    return ImageChops.difference(Image.open(BytesIO(a)).convert("RGB"), Image.open(BytesIO(b)).convert("RGB")).getbbox() is not None
                interaction = {"stroke_changes_pixels": changed(before, drawn), "undo_restores_pixels": not changed(before, undone)}
            if name == "autocomplete" and ready:
                clip = {"x": 610, "y": 455, "width": 220, "height": 160}
                before = page.screenshot(clip=clip)
                page.mouse.click(700, 470)
                page.keyboard.type("Uni", delay=100)
                page.wait_for_timeout(1000)
                after = page.screenshot(clip=clip)
                interaction = ImageChops.difference(Image.open(BytesIO(before)).convert("RGB"), Image.open(BytesIO(after)).convert("RGB")).getbbox() is not None
                page.screenshot(path=str(output / "autocomplete-typed.png"))
            result = {"name": name, "url": page.url, "ready": ready,
                      "seconds": round(time.monotonic() - started, 1), "errors": errors,
                      "interaction": interaction,
                      "http_failures": failures, "text": page.locator("body").inner_text()[:3500],
                      "frames": [{"url": f.url, "canvases": f.locator("canvas").count()} for f in page.frames]}
            results.append(result)
            print("RESULT", json.dumps({k: v for k, v in result.items() if k not in ("text", "frames")}), flush=True)
            context.close()
        browser.close()
    (output / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
    return 0 if all(r["ready"] and not r["errors"] and not r["http_failures"] and
                    r["interaction"] is not False and
                    (not isinstance(r["interaction"], dict) or all(r["interaction"].values())) for r in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
