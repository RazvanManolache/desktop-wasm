"""Serve the extracted browser demo locally with SPA navigation routes."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit


class DemoHandler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.mjs': 'text/javascript'}

    def __init__(self, *args, mount='', **kwargs):
        self.mount = mount.rstrip('/')
        super().__init__(*args, **kwargs)

    def do_GET(self):
        path = urlsplit(self.path).path
        if self.mount:
            if path == self.mount:
                path = '/'
            elif path.startswith(self.mount + '/'):
                path = path[len(self.mount):]
            else:
                self.send_error(404)
                return
            self.path = path
        if path.startswith(('/app/', '/runtime/', '/demo/')) or path == '/gpu-spike':
            self.path = '/index.html'
        if self.mount and self.path in ('/', '/index.html'):
            # Match the unified gateway's mounted HTML response. Its base
            # scopes CSS, entry modules and Blazor assets before scripts run.
            html = (Path(self.directory) / 'index.html').read_text(encoding='utf-8')
            body = html.replace('<base href="/"', f'<base href="{self.mount}/"').encode('utf-8')
            self.send_response(200)
            self.send_header('Content-Type', 'text/html; charset=utf-8')
            self.send_header('Content-Length', str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        super().do_GET()


def create_server(root, port=0, mount=''):
    return ThreadingHTTPServer(('127.0.0.1', port), partial(DemoHandler, directory=str(root), mount=mount))


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
