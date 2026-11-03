"""Headless-Chromium oracle for interop slice-3.

Replays REAL session payloads (exported by BrowserOracleExportTests) through
the REAL production bridge entry points (wfwasmTexDef / wfwasmTextRaster from
src/WfWasm.CanvasHost/wwwroot/canvas-bridge.js) and records hashes, console
errors and a screenshot. Harness compositing (blitting the retained layer
onto the visible stage) is driver-only; the bridge functions under test are
unmodified production files served from their canonical path.

Usage:
  python run-oracle.py --stamp <stamp> [--port 18923]
"""
import argparse
import base64
import hashlib
import http.server
import json
import os
import socketserver
import sys
import threading

WS = "E:/AI/Code/DesktopWasm"
BRIDGE = os.path.join(WS, "src/WfWasm.CanvasHost/wwwroot/canvas-bridge.js")
ART = os.path.join(WS, "artifacts/interop-oracle")

PAGE = """<!doctype html><html><head><meta charset="utf-8"></head><body>
<canvas id="stage"></canvas>
<script src="/canvas-bridge.js"></script>
</body></html>
"""


class Handler(http.server.BaseHTTPRequestHandler):
    frames_path = ""

    def log_message(self, *args):
        pass

    def _send(self, body, ctype):
        data = body if isinstance(body, bytes) else body.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path == "/":
            return self._send(PAGE, "text/html")
        if self.path == "/canvas-bridge.js":
            with open(BRIDGE, "rb") as f:
                return self._send(f.read(), "text/javascript")
        if self.path == "/frames.json":
            with open(self.frames_path, "rb") as f:
                return self._send(f.read(), "application/json")
        self.send_response(404)
        self.end_headers()


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        h.update(f.read())
    return h.hexdigest()


def _playwright_base():
    # EXPORT-ONLY portable repair (PUBPROGRESS-20261103-REVIEW-FIXES): derive
    # the Playwright browser dir from the environment instead of a hardcoded
    # operator profile path. Live tree intentionally keeps its local path.
    local = os.environ.get("LOCALAPPDATA")
    if local:
        return os.path.join(local, "ms-playwright")
    home = os.path.expanduser("~")
    return os.path.join(home, "AppData", "Local", "ms-playwright")


def find_chromium():
    base = _playwright_base()
    if not os.path.isdir(base):
        return None
    best = None
    for name in sorted(os.listdir(base)):
        if name.startswith("chromium-"):
            exe = os.path.join(base, name, "chrome-win", "chrome.exe")
            if os.path.isfile(exe):
                best = exe
    return best


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stamp", required=True)
    ap.add_argument("--port", type=int, default=18923)
    args = ap.parse_args()

    stamp_dir = os.path.join(ART, args.stamp)
    frames_path = os.path.join(stamp_dir, "frames.json")
    if not os.path.isfile(frames_path):
        print(f"frames.json missing: {frames_path}")
        return 2
    with open(frames_path) as f:
        frames = json.load(f)

    Handler.frames_path = frames_path
    httpd = socketserver.TCPServer(("127.0.0.1", args.port), Handler)
    httpd.allow_reuse_address = True
    t = threading.Thread(target=httpd.serve_forever, daemon=True)
    t.start()

    from playwright.sync_api import sync_playwright

    console, pageerrors = [], []
    try:
        with sync_playwright() as p:
            try:
                browser = p.chromium.launch(headless=True)
            except Exception:
                exe = find_chromium()
                if not exe:
                    raise
                browser = p.chromium.launch(headless=True, executable_path=exe)
            page = browser.new_page(viewport={"width": 1000, "height": 700})
            page.on("console", lambda m: console.append(f"{m.type}: {m.text}"))
            page.on("pageerror", lambda e: pageerrors.append(str(e)))
            page.goto(f"http://127.0.0.1:{args.port}/")
            page.wait_for_function("() => !!window.wfwasmTextRaster")

            caps = page.evaluate("() => window.wfwasmTextCaps()")
            assert caps == "ok:1", f"text caps: {caps}"

            wpf = frames["wpf"]
            font_px = int(wpf["fontCss"].split("px")[0])
            raster = page.evaluate(
                """([text, css, px]) => {
                    const out = window.wfwasmTextRaster(text, css, px, 800);
                    return out ? Array.from(out) : null;
                }""",
                [wpf["text"], wpf["fontCss"], font_px],
            )
            assert raster, "text raster returned null"
            rw = raster[0] | (raster[1] << 8) | (raster[2] << 16) | (raster[3] << 24)
            rh = raster[4] | (raster[5] << 8) | (raster[6] << 16) | (raster[7] << 24)
            assert rw > 0 and rh > 0, f"raster size {rw}x{rh}"
            ink = sum(1 for i in range(8, len(raster), 4) if raster[i + 3] > 0)
            assert ink > 0, "raster has no ink pixels"
            raster_sha = hashlib.sha256(bytes(raster)).hexdigest()

            wf = frames["winforms"]
            rgba = base64.b64decode(wf["rgbaBase64"])
            assert len(rgba) == wf["width"] * wf["height"] * 4
            texdef = page.evaluate(
                """([w, h, b64]) => {
                    const bin = atob(b64);
                    const u8 = new Uint8Array(bin.length);
                    for (let i = 0; i < bin.length; i++) u8[i] = bin.charCodeAt(i);
                    return window.wfwasmTexDef('wf', u8, w, h);
                }""",
                [wf["width"], wf["height"], wf["rgbaBase64"]],
            )
            assert texdef == "ok", f"texdef: {texdef}"

            stage = page.evaluate(
                """([w, h]) => {
                    const layer = window.wfwasm._tex.get('wf');
                    if (!layer) return 'nolayer';
                    const stage = document.getElementById('stage');
                    stage.width = w; stage.height = h;
                    stage.getContext('2d').drawImage(layer, 0, 0);
                    const d = stage.getContext('2d').getImageData(0, 0, w, h).data;
                    let red = 0;
                    for (let i = 0; i < d.length; i += 4)
                        if (d[i] > 200 && d[i+1] < 80 && d[i+2] < 80 && d[i+3] > 200) red++;
                    return { w, h, red, total: w * h };
                }""",
                [wf["width"], wf["height"]],
            )
            assert isinstance(stage, dict), f"stage: {stage}"
            assert stage["w"] == 40 * 2 and stage["h"] == 30 * 2, f"dpi size: {stage}"
            assert stage["red"] > stage["total"] // 2, f"red pixels: {stage}"

            shot = os.path.join(stamp_dir, "screenshot.png")
            page.screenshot(path=shot)
            browser.close()
    finally:
        httpd.shutdown()

    receipt = {
        "stamp": args.stamp,
        "caps": caps,
        "textRaster": {"w": rw, "h": rh, "inkPixels": ink, "sha256": raster_sha},
        "texDef": texdef,
        "stage": stage,
        "console": console,
        "pageerrors": pageerrors,
        "hashes": {
            "frames.json": sha256_file(frames_path),
            "canvas-bridge.js": sha256_file(BRIDGE),
            "screenshot.png": sha256_file(shot),
        },
    }
    with open(os.path.join(stamp_dir, "receipt.json"), "w") as f:
        json.dump(receipt, f, indent=2)

    bad_console = [c for c in console if c.startswith("error") or "Uncaught" in c]
    print(json.dumps(receipt, indent=2))
    if bad_console or pageerrors:
        print("ORACLE FAIL: console/page errors present")
        return 1
    print("ORACLE PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
