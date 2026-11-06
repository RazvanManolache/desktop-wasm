"""Serve the extracted browser demo locally with SPA navigation routes."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit


class DemoHandler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.mjs': 'text/javascript'}

    def do_GET(self):
        path = urlsplit(self.path).path
        if path.startswith(('/app/', '/runtime/', '/demo/')) or path == '/gpu-spike':
            self.path = '/index.html'
        super().do_GET()


def create_server(root, port=0):
    return ThreadingHTTPServer(('127.0.0.1', port), partial(DemoHandler, directory=str(root)))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parent / 'wwwroot')
    parser.add_argument('--port', type=int, default=8080)
    args = parser.parse_args()
    if not (args.root / 'index.html').is_file():
        parser.error('The browser package wwwroot/index.html is missing.')
    with create_server(args.root, args.port) as server:
        print(f'Open http://127.0.0.1:{server.server_port}', flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass
