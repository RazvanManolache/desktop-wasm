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
from build_pages import DEMOS, WINFORMS_DEMOS

# Both untrimmed runtimes must cold-start on shared hosted-runner CPUs.
STARTUP_TIMEOUT_MS = 240000


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
    parser.add_argument('--url', help='Verify the deployed showcase using the same interaction checks')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    temp = args.output.resolve() / 'os-temp'
    temp.mkdir(exist_ok=True)
    os.environ['TEMP'] = os.environ['TMP'] = str(temp)
    errors, bad, framework_requests, cases = [], [], [], []
    with ThreadingHTTPServer(('127.0.0.1', 0), partial(StaticHandler, directory=str(args.root))) as server:
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        url = args.url or f'http://127.0.0.1:{server.server_port}/desktop-wasm/'
        try:
            with sync_playwright() as pw:
                browser = pw.chromium.launch(headless=True, args=['--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
                try:
                    page = browser.new_page(viewport={'width': 1440, 'height': 1100})
                    # Observe the labels actually painted by the native controls.
                    page.add_init_script("""window.paintedLabels = [];
                        const original = CanvasRenderingContext2D.prototype.fillText;
                        CanvasRenderingContext2D.prototype.fillText = function(text, ...args) {
                            window.paintedLabels.push(String(text));
                            if (window.paintedLabels.length > 4000) window.paintedLabels.splice(0, 2000);
                            return original.call(this, text, ...args);
                        };""")
                    console = []
                    page.on('console', lambda message: console.append(message.text))
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
                        print('Opening ' + slug, flush=True)
                        page.locator(f'.app-copy [data-demo="{slug}"]').click()
                        page.wait_for_function("['ready','error'].includes(document.querySelector('#demo-status')?.dataset.state)", timeout=STARTUP_TIMEOUT_MS)
                        assert page.locator('#demo-status').get_attribute('data-state') == 'ready', page.locator('#demo-status').inner_text()
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
                    for slug in WINFORMS_DEMOS:
                        print('Opening ' + slug, flush=True)
                        console.clear()
                        page.locator(f'.app-copy [data-demo="{slug}"]').click()
                        page.wait_for_function("['ready','error'].includes(document.querySelector('#demo-status')?.dataset.state)", timeout=STARTUP_TIMEOUT_MS)
                        assert page.locator('#demo-status').get_attribute('data-state') == 'ready', page.locator('#demo-status').inner_text()
                        frame = next(frame for frame in page.frames if '/winforms/?demo=' + slug in frame.url)
                        canvas = frame.locator('#wfcanvas')
                        original_forms = {'calculator': 'Calculator.Form1', 'todo': 'SimpleDailyToDoList.ToDoListDashBoard_FR',
                                          'tic-tac-toe': 'Tic_Tac_Toe.Form1'}
                        assert any('Form=' + original_forms[slug] + ',' in line for line in console), console[-15:]
                        if args.capture_previews:
                            canvas.screenshot(path=str(Path(__file__).resolve().parents[2] / 'site/assets' / (slug + '.png')))
                        canvas.screenshot(path=str(args.output / (slug + '-before.png')))
                        bounds = canvas.bounding_box()
                        def click_native(dx, dy):
                            canvas.click(position={'x': bounds['width']/2 + dx, 'y': bounds['height']/2 + dy})
                        frame.evaluate('paintedLabels.length = 0')
                        if slug == 'calculator':
                            for point in [(-141, -64), (68, 218), (-72, -64), (-2, 218)]:
                                click_native(*point)
                            frame.wait_for_function("paintedLabels.includes('15')")
                        elif slug == 'todo':
                            click_native(51, -101)
                            canvas.press_sequentially('WinForms on GitHub')
                            frame.evaluate('paintedLabels.length = 0')
                            click_native(271, -67)
                            frame.wait_for_function("paintedLabels.some(s => s.includes('WinForms on GitHub'))")
                        else:
                            for point in [(69, -37), (69, 47), (192, -37), (192, 47), (315, -37)]:
                                click_native(*point)
                            frame.wait_for_function("paintedLabels.includes('1 time(s)') && paintedLabels.includes('Game Over')")
                            click_native(0, 30)  # Dismiss the game's native Game Over dialog.
                            frame.evaluate('paintedLabels.length = 0')
                            click_native(-92, 38)
                            frame.wait_for_function("paintedLabels.includes('In Progress')")
                        page.mouse.move(25, 25)
                        page.wait_for_timeout(300)
                        canvas.screenshot(path=str(args.output / (slug + '-after.png')))
                        first = Image.open(args.output / (slug + '-before.png')).convert('RGB')
                        second = Image.open(args.output / (slug + '-after.png')).convert('RGB')
                        regions = {'calculator': (-174, -205, 171, -117), 'todo': (-329, -23, 336, 68),
                                   'tic-tac-toe': (-374, 77, -64, 140)}
                        x0, y0, x1, y1 = regions[slug]
                        region = (int(first.width/2+x0), int(first.height/2+y0),
                                  int(first.width/2+x1), int(first.height/2+y1))
                        result_delta = sum(ImageStat.Stat(ImageChops.difference(first.crop(region), second.crop(region))).mean) / 3
                        assert result_delta > 0.01, f'{slug} did not update its result region'
                        cases.append({'demo': slug, 'framework': 'winforms', 'ready': True,
                                      'originalForm': original_forms[slug], 'resultMeanDelta': result_delta})
                        page.locator('#close-demo').click()
                        page.locator('#runtime-host iframe').wait_for(state='detached')
                    page.screenshot(path=str(args.output / 'desktop.png'), full_page=True)
                    assert page.locator('.source-grid article').count() == 4
                    for image in page.locator('.source-grid img').all():
                        image.scroll_into_view_if_needed()
                        image.evaluate('(img) => img.decode()')
                        assert image.evaluate('(img) => img.naturalWidth > 0')
                    page.set_viewport_size({'width': 390, 'height': 844})
                    page.goto(url)
                    page.wait_for_timeout(200)
                    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Mobile overflow'
                    page.screenshot(path=str(args.output / 'mobile.png'), full_page=True)
                    # A bookmarked runtime URL and a browser refresh must both
                    # work without any catch-all /app or /runtime server rewrite.
                    direct = url + 'demos/?demo=color-lab&renderer=auto'
                    assert page.goto(direct).status == 200
                    page.wait_for_function("window.wasmWpfLoop?.running && !document.querySelector('.stage-overlay')", timeout=STARTUP_TIMEOUT_MS)
                    assert page.reload().status == 200
                    page.wait_for_function("window.wasmWpfLoop?.running && !document.querySelector('.stage-overlay')", timeout=STARTUP_TIMEOUT_MS)
                    assert page.goto(url + 'winforms/?demo=calculator').status == 200
                    page.wait_for_function("paintedLabels.includes('7') && paintedLabels.includes('DEL')", timeout=STARTUP_TIMEOUT_MS)
                    assert page.reload().status == 200
                    page.wait_for_function("paintedLabels.includes('7') && paintedLabels.includes('DEL')", timeout=STARTUP_TIMEOUT_MS)
                    assert not errors, errors
                    assert not bad, bad
                except Exception:
                    print(json.dumps({'pageUrl': page.url, 'frameUrls': [frame.url for frame in page.frames],
                        'pageErrors': errors, 'badResponses': bad, 'recentConsole': console[-20:]}), flush=True)
                    raise
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
