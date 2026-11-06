"""Assemble a static GitHub Pages showcase from the source-built WPF runtime."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil

DEMOS = ('pocket-calculator', 'project-planner', 'color-lab', 'asset-explorer')


def boot_assets(value):
    for name, entry in value.items():
        if isinstance(entry, dict):
            yield from boot_assets(entry)
        elif isinstance(entry, str) and entry.startswith('sha256-') and name != 'hash':
            yield name, entry


def build(runtime, output, base_path):
    root = Path(__file__).resolve().parents[2]
    runtime, output = runtime.resolve(), output.resolve()
    assert output.is_relative_to(root / '.ci-dist') and output != root / '.ci-dist'
    assert runtime != output and not runtime.is_relative_to(output)
    assert re.fullmatch(r'/(?:[A-Za-z0-9_.-]+/)*', base_path), base_path
    if output.exists():
        assert not output.is_symlink(), output
        shutil.rmtree(output)
    shutil.copytree(root / 'site', output)
    html = (output / 'index.html').read_text(encoding='utf-8')
    assert set(re.findall(r'data-demo="([a-z0-9-]+)"', html)) == set(DEMOS)
    (output / 'index.html').write_text(html.replace('@@BASE_PATH@@', base_path), encoding='utf-8')
    target = output / 'demos'
    shutil.copytree(runtime, target, ignore=shutil.ignore_patterns('apps', '_framework', '*.br', '*.gz', '*.pdb', '*.map'))
    # Repeated local publishes may retain obsolete fingerprinted DLLs. Copy
    # the current boot closure, verifying its bytes before publication.
    framework = target / '_framework'
    framework.mkdir()
    boot = json.loads((runtime / '_framework/blazor.boot.json').read_text(encoding='utf-8-sig'))
    for name, expected in boot_assets(boot['resources']):
        source = (runtime / '_framework' / name).resolve()
        assert source.is_relative_to(runtime / '_framework'), name
        actual = 'sha256-' + base64.b64encode(hashlib.sha256(source.read_bytes()).digest()).decode()
        assert actual == expected, f'Boot resource integrity mismatch: {name}'
        (framework / name).parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, framework / name)
    for name in ('blazor.boot.json', 'blazor.webassembly.js', 'dotnet.js'):
        shutil.copyfile(runtime / '_framework' / name, framework / name)
    catalog = json.loads((target / 'app-library.json').read_text(encoding='utf-8-sig'))
    selected = [app for app in catalog if app['slug'] in DEMOS]
    assert {app['slug'] for app in selected} == set(DEMOS)
    assert all(app['kind'] == 'toolkit' and app['status'] == 'ready' for app in selected)
    (target / 'app-library.json').write_text(json.dumps(selected, ensure_ascii=False, indent=2), encoding='utf-8')
    runtime_html = (target / 'index.html').read_text(encoding='utf-8-sig')
    assert '<base href="/"' in runtime_html
    (target / 'index.html').write_text(runtime_html.replace('<base href="/"', f'<base href="{base_path}demos/"'), encoding='utf-8')
    licenses = output / 'licenses'
    licenses.mkdir()
    for name in ('LICENSE', 'THIRD-PARTY-NOTICES.md'):
        shutil.copyfile(root / name, licenses / name)
    (output / '.nojekyll').touch()
    (output / 'build.json').write_text(json.dumps({'commit': os.environ.get('GITHUB_SHA', 'local'),
        'demos': list(DEMOS), 'basePath': base_path}, indent=2), encoding='utf-8')
    size = sum(path.stat().st_size for path in output.rglob('*') if path.is_file())
    assert size < 250 * 1024 * 1024, f'Unexpected showcase size: {size}'
    print(json.dumps({'output': str(output), 'demos': list(DEMOS), 'bytes': size, 'basePath': base_path}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', type=Path, default=Path('.ci-dist/wpf/wwwroot'))
    parser.add_argument('--output', type=Path, default=Path('.ci-dist/pages'))
    parser.add_argument('--base-path', default='/desktop-wasm/')
    args = parser.parse_args()
    build(args.runtime, args.output, args.base_path)
