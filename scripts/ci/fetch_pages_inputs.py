"""Download the pinned converted application inputs for the static showcase."""
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import shutil
import urllib.request
import zipfile


def main():
    root = Path(__file__).resolve().parents[2]
    metadata = json.loads(Path(__file__).with_name('showcase-inputs.json').read_text(encoding='utf-8'))
    with urllib.request.urlopen(metadata['url'], timeout=90) as response:
        payload = response.read(10 * 1024 * 1024 + 1)
    assert len(payload) == metadata['bytes'] < 10 * 1024 * 1024, 'Unexpected input archive size'
    assert hashlib.sha256(payload).hexdigest() == metadata['sha256'], 'Input archive integrity mismatch'
    output = root / '.ci-dist/showcase-inputs'
    assert output.resolve().is_relative_to(root / '.ci-dist') and not output.is_symlink()
    with zipfile.ZipFile(io.BytesIO(payload)) as archive:
        members = archive.infolist()
        assert sum(member.file_size for member in members) < 20 * 1024 * 1024
        for member in members:
            name = PurePosixPath(member.filename)
            assert not name.is_absolute() and '..' not in name.parts and '\\' not in member.filename, member.filename
            assert (output / member.filename).resolve().is_relative_to(output), member.filename
        if output.exists():
            shutil.rmtree(output)
        output.mkdir(parents=True)
        archive.extractall(output)
    for app in metadata['apps']:
        assert (output / 'apps' / app['app'] / 'app-manifest.json').is_file(), app['app']
    print(json.dumps({'inputs': [app['app'] for app in metadata['apps']], 'bytes': len(payload)}))


if __name__ == '__main__':
    main()
