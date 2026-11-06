"""Check the packaged launcher and a rendered first-party calculator demo."""
import argparse
import json
import threading
from pathlib import Path
from playwright.sync_api import sync_playwright
from serve_demo import create_server

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('root', type=Path)
parser.add_argument('--output', type=Path, default=Path('.ci-results/browser'))
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
errors = []
with create_server(args.root) as server:
    worker = threading.Thread(target=server.serve_forever, daemon=True)
    worker.start()
    try:
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True, args=['--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
            page = browser.new_page(viewport={'width': 1280, 'height': 900})
            page.on('pageerror', lambda error: errors.append(str(error)))
            page.goto(f'http://127.0.0.1:{server.server_port}/app/pocket-calculator')
            page.wait_for_function("document.querySelector('#status')?.dataset.state === 'ready'", timeout=120000)
            frame = next(f for f in page.frames if '/runtime/pocket-calculator' in f.url)
            canvas = frame.locator('#wpfCanvas')
            assert canvas.count() == 1
            assert canvas.evaluate('(c) => c.width > 0 && c.height > 0')
            page.screenshot(path=str(args.output/'calculator.png'))
            assert not errors, errors
            report = {'passed': True, 'demo': 'pocket-calculator', 'status': page.locator('#status').inner_text(), 'renderer': page.locator('#renderer-backend').inner_text(), 'pageErrors': errors}
            (args.output/'report.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
            print(json.dumps(report))
            browser.close()
    finally:
        server.shutdown()
        worker.join(timeout=5)
