"""Exercise the showcase on a physical-files-only server at the GitHub Pages path."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import threading

from PIL import Image, ImageChops, ImageStat
from playwright.sync_api import sync_playwright
from build_pages import DEMOS


class StaticHandler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.mjs': 'text/javascript'}

    def translate_path(self, path):
        if not path.startswith('/desktop-wasm/'):
            return str(Path(self.directory) / '__outside_project__')
        return super().translate_path(path[len('/desktop-wasm'):])

    def log_message(self, *_):
        pass


def canvas_click(frame, x, y):
    canvas = frame.locator('#wpfCanvas')
    bounds = canvas.bounding_box()
    scale = min(bounds['width'] / 800, bounds['height'] / 600)
    canvas.click(position={'x': (bounds['width'] - 800 * scale) / 2 + x * scale,
                           'y': (bounds['height'] - 600 * scale) / 2 + y * scale})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path)
    parser.add_argument('--output', type=Path, default=Path('.ci-results/pages'))
    parser.add_argument('--capture-previews', action='store_true')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    temp = args.output.resolve() / 'os-temp'
    temp.mkdir(exist_ok=True)
    os.environ['TEMP'] = os.environ['TMP'] = str(temp)
    errors, bad, framework_requests, cases = [], [], [], []
    with ThreadingHTTPServer(('127.0.0.1', 0), partial(StaticHandler, directory=str(args.root))) as server:
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        url = f'http://127.0.0.1:{server.server_port}/desktop-wasm/'
        try:
            with sync_playwright() as pw:
                browser = pw.chromium.launch(headless=True, args=['--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
                try:
                    page = browser.new_page(viewport={'width': 1440, 'height': 1100})
                    page.on('pageerror', lambda error: errors.append(str(error)))
                    page.on('request', lambda request: framework_requests.append(request.url) if '/_framework/' in request.url else None)
                    page.on('response', lambda response: bad.append(f'{response.status} {response.url}')
                            if response.status >= 400 and not (args.capture_previews and '/assets/' in response.url) else None)
                    assert page.goto(url).status == 200
                    page.wait_for_function("!!document.querySelector('a[data-demo]') && document.querySelector('#demo-dialog') !== null")
                    page.wait_for_timeout(500)
                    assert not framework_requests, 'The landing page eagerly loaded .NET'
                    assert not page.locator('iframe').count()
                    for slug in DEMOS:
                        page.locator(f'.app-copy [data-demo="{slug}"]').click()
                        page.wait_for_function("document.querySelector('#demo-status')?.dataset.state === 'ready'", timeout=120000)
                        frame = next(frame for frame in page.frames if '/demos/?demo=' + slug in frame.url)
                        canvas = frame.locator('#wpfCanvas')
                        before = args.output / (slug + '-before.png')
                        canvas.screenshot(path=str(before))
                        if args.capture_previews:
                            preview = Path(__file__).resolve().parents[2] / 'site/assets' / (slug + '.png')
                            canvas.screenshot(path=str(preview))
                        # Points are in the engine's 800x600 logical surface.
                        # Each check targets a state-changing control from StudioDemos.
                        interactions = {
                            'pocket-calculator': [(257, 269), (533, 409), (349, 269), (441, 455)],
                            'project-planner': [(195, 428)],
                            'color-lab': [(393, 362)],
                            'asset-explorer': [(180, 162)],
                        }
                        for point in interactions[slug]:
                            canvas_click(frame, *point)
                        page.mouse.move(25, 25)
                        page.wait_for_timeout(400)
                        after = args.output / (slug + '-after.png')
                        canvas.screenshot(path=str(after))
                        delta = sum(ImageStat.Stat(ImageChops.difference(Image.open(before).convert('RGB'), Image.open(after).convert('RGB'))).mean) / 3
                        # Check the result region, away from the clicked control
                        # and any focus/hover decoration.
                        regions = {'pocket-calculator': (220, 117, 580, 193),
                                   'project-planner': (20, 105, 300, 140),
                                   'color-lab': (130, 125, 650, 270),
                                   'asset-explorer': (20, 355, 700, 420)}
                        first, second = Image.open(before).convert('RGB'), Image.open(after).convert('RGB')
                        scale = min(first.width / 800, first.height / 600)
                        left, top = (first.width - 800 * scale) / 2, (first.height - 600 * scale) / 2
                        x0, y0, x1, y1 = regions[slug]
                        region = (int(left+x0*scale), int(top+y0*scale), int(left+x1*scale), int(top+y1*scale))
                        result_delta = sum(ImageStat.Stat(ImageChops.difference(first.crop(region), second.crop(region))).mean) / 3
                        assert result_delta > 0.01, f'{slug} did not update its result region'
                        cases.append({'demo': slug, 'ready': True, 'interactionMeanDelta': delta,
                                      'resultMeanDelta': result_delta})
                        page.locator('#close-demo').click()
                        page.locator('#runtime-host iframe').wait_for(state='detached')
                        assert page.locator('iframe').count() == 0, 'Closing a demo left a runtime running'
                    page.screenshot(path=str(args.output / 'desktop.png'), full_page=True)
                    page.set_viewport_size({'width': 390, 'height': 844})
                    page.goto(url)
                    page.wait_for_timeout(200)
                    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Mobile overflow'
                    page.screenshot(path=str(args.output / 'mobile.png'), full_page=True)
                    # A bookmarked runtime URL and a browser refresh must both
                    # work without any catch-all /app or /runtime server rewrite.
                    direct = url + 'demos/?demo=color-lab&renderer=auto'
                    assert page.goto(direct).status == 200
                    page.wait_for_function("window.wasmWpfLoop?.running && !document.querySelector('.stage-overlay')", timeout=120000)
                    assert page.reload().status == 200
                    page.wait_for_function("window.wasmWpfLoop?.running && !document.querySelector('.stage-overlay')", timeout=120000)
                    assert not errors, errors
                    assert not bad, bad
                finally:
                    browser.close()
        finally:
            server.shutdown()
            thread.join(timeout=5)
    report = {'passed': True, 'mount': '/desktop-wasm/', 'landingLoadsRuntime': False,
              'physicalRuntimeRefresh': True, 'closedFramesRemoved': True, 'cases': cases,
              'pageErrors': errors, 'badResponses': bad}
    (args.output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
