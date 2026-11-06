import io, json, os, sys

# Usage: bundlegate.py <label:path> [<label:path> ...]
# Each root must contain _framework/blazor.boot.json; every resource key
# must exist under <root>/_framework. Skips the 'hash' metadata section.
failed = False
for arg in sys.argv[1:]:
    label, root = arg.split(':', 1)
    boot = os.path.join(root, '_framework', 'blazor.boot.json')
    man = json.load(io.open(boot, encoding='utf-8'))
    res = man.get('resources') or {}
    names = []
    for section, entries in res.items():
        if section == 'hash':
            continue
        if isinstance(entries, dict):
            names.extend(entries.keys())
        elif isinstance(entries, list):
            names.extend(x for x in entries if isinstance(x, str))
        elif isinstance(entries, str):
            names.append(section)
    missing = [n for n in names
               if not os.path.exists(os.path.join(root, '_framework',
                                                  *n.split('/')))]
    print(label, 'referenced:', len(names), 'missing:', len(missing),
          flush=True)
    for m in missing[:10]:
        print('  MISSING:', m, flush=True)
    if missing or not names:
        failed = True

sys.exit(1 if failed else 0)
