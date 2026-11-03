"""Import pinned, prepared benchmark inputs into the unified workspace.

Old workspaces are read-only. Existing destination files must match; local fixes
are never silently overwritten. A receipt records every copied file's SHA-256.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
from datetime import datetime, timezone


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def migrate(source, destination, catalog):
    results = []
    for app in catalog:
        folder = source / app['slug']
        receipt = folder / 'preparation.json'
        if not receipt.is_file():
            results.append({'slug': app['slug'], 'imported': False, 'reason': 'No preparation receipt'})
            continue
        prepared = json.loads(receipt.read_text(encoding='utf-8-sig'))
        if not prepared.get('prepared') or not (folder / app['entryFile']).is_file():
            results.append({'slug': app['slug'], 'imported': False,
                            'reason': prepared.get('failure') or 'Entry assembly is absent'})
            continue
        if prepared.get('revision') != app['revision'] or prepared.get('source') != app['sourceUrl']:
            raise ValueError(f"{app['slug']}: input provenance does not match the catalog")
        target = destination / app['slug']
        files = []
        for item in sorted(folder.rglob('*')):
            if item.is_symlink() or item.is_junction():
                raise ValueError(f'Refusing linked input: {item}')
            if not item.is_file():
                continue
            relative = item.relative_to(folder)
            output = target / relative
            sha = digest(item)
            if output.exists() and digest(output) != sha:
                raise ValueError(f'Existing destination differs; preserve it for review: {output}')
            files.append({'path': relative.as_posix(), 'sha256': sha, 'bytes': item.stat().st_size})
        # Publish the readiness receipt last, after all payloads are verified.
        files.sort(key=lambda f: f['path'] == 'preparation.json')
        for file in files:
            output = target / file['path']
            output.parent.mkdir(parents=True, exist_ok=True)
            if not output.exists():
                shutil.copy2(folder / file['path'], output)
            if digest(output) != file['sha256']:
                raise IOError(f'Copied input hash mismatch: {output}')
        results.append({'slug': app['slug'], 'imported': True, 'files': files})
    return {'recordedAtUtc': datetime.now(timezone.utc).isoformat(),
            'source': str(source), 'destination': str(destination), 'apps': results}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--destination', required=True, type=Path)
    parser.add_argument('--receipt', required=True, type=Path)
    args = parser.parse_args()
    catalog = Path(__file__).resolve().parents[2] / 'src/WfWasm.CanvasHost/DesktopBenchmarks.json'
    result = migrate(args.source.resolve(), args.destination.resolve(),
                     json.loads(catalog.read_text(encoding='utf-8-sig')))
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    args.receipt.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({'imported': sum(a['imported'] for a in result['apps']),
                      'remaining': [{k: v for k, v in a.items() if k != 'files'}
                                    for a in result['apps'] if not a['imported']]}))
