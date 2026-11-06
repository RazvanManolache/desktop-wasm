"""Package only source-built toolkit demos and their standalone local server."""
import hashlib
import json
import shutil
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parents[2]
stage = root / '.ci-dist/wpf'
library = stage / 'wwwroot/app-library.json'
apps = json.loads(library.read_text(encoding='utf-8'))
# Public checkouts omit converted external applications. Do not advertise them
# as runnable in this standalone artifact using stale staged-demo statuses.
apps = [app for app in apps if app['kind'] == 'toolkit']
assert apps and any(app['slug'] == 'pocket-calculator' for app in apps)
library.write_text(json.dumps(apps, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')
shutil.copyfile(root/'scripts/ci/serve_demo.py', stage/'serve_demo.py')
for name in ('LICENSE', 'THIRD-PARTY-NOTICES.md'):
    shutil.copyfile(root/name, stage/name)
(stage/'README.txt').write_text('''DesktopWasm browser demos

Install Python 3.10 or newer, open a terminal in this extracted folder, and run:
  python serve_demo.py
Open http://127.0.0.1:8080 in a current browser. Ctrl+C stops the local server.
You do not need the .NET SDK to run these compiled browser files.
Do not open index.html directly from the filesystem.

This package contains first-party toolkit demonstrations. Converted external
apps, commercial demo inputs and the unified WinForms backend are not included.
CI checks rendered calculator and planner frames and mounted navigation in
headless Chromium; this is a preview,
not a claim of complete WPF compatibility or validation of every demo.
''', encoding='utf-8')
import os
(stage/'BUILD-COMMIT.txt').write_text(os.environ.get('GITHUB_SHA', 'local')+'\n', encoding='utf-8')
output = root/'.ci-dist/downloads'
output.mkdir(parents=True, exist_ok=True)
archive = output/'DesktopWasm-browser-demos.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
    for p in sorted(stage.rglob('*')):
        if p.is_file():
            z.write(p, p.relative_to(stage))
(output/'SHA256SUMS.txt').write_text(hashlib.sha256(archive.read_bytes()).hexdigest()+'  '+archive.name+'\n', encoding='utf-8')
print(f'Packaged {len(apps)} toolkit demos: {archive}')
