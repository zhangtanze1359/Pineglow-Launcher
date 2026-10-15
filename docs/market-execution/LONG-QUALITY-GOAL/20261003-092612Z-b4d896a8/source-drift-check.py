#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""source-drift-check.py - re-hash the frozen G0 input set and report every difference.

Reads <evidenceRoot>/g0/input-manifest.json (286 frozen product inputs), re-hashes each path in
D:\\松光启动器 and classifies every difference:

  unchanged   byte-identical to the freeze
  changed     the file is on the explicit modification whitelist of this goal -> expected
  UNEXPECTED  changed although this goal is not allowed to touch it -> must be explained

It never writes into the repository and never modifies a frozen manifest.

usage: python -X utf8 source-drift-check.py <evidenceRoot> <outputJson> [--label NAME]
"""

import hashlib
import json
import pathlib
import sys
from datetime import datetime, timezone

SOURCE_ROOT = pathlib.Path(r'D:\松光启动器')

# The exact objects this goal is allowed to modify (plan section 2.2).
ALLOWED = {
    'scripts/audit-goal-evidence.py',
    'scripts/capture-goal-baseline.ps1',
    'scripts/run-goal-validation.ps1',
    'scripts/tests/audit-goal-evidence.test.py',
    'scripts/tests/goal-validation.test.cjs',
    'scripts/source-snapshot-inputs.txt',
    'ui/js/api.js',
    'ui/js/launcher.js',
    'ui/js/i18n.js',
    'ui/css/figma.css',
    'ui/tests/instance-removal.test.cjs',
    'ui/tests/file-recovery.test.cjs',
    'ui/tests/bisect-recovery.test.cjs',
    'ui/tests/import-cancel.test.cjs',
    'ui/tests/diagnostic-history.test.cjs',
    'ui/tests/update-recovery.test.cjs',
    'ui/tests/goal-core-flow.test.cjs',
    'dev/PineglowLauncher.Tests/GoalOrdinaryFlowTests.cs',
    'dev/PineglowLauncher.Tests/LocalApiTestHarness.cs',
}


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest().upper()


def read_whitelist(path):
    rows = []
    for raw in pathlib.Path(path).read_text(encoding='utf-8-sig').splitlines():
        line = raw.strip()
        if not line or line.startswith('#'):
            continue
        rows.append(line.replace('\\', '/'))
    return rows


def main(argv):
    if len(argv) < 3:
        raise SystemExit('usage: source-drift-check.py <evidenceRoot> <outputJson> [--label NAME]')
    evidence_root = pathlib.Path(argv[1])
    out_path = pathlib.Path(argv[2])
    label = 'check'
    if '--label' in argv:
        label = argv[argv.index('--label') + 1]

    manifest = json.loads((evidence_root / 'g0' / 'input-manifest.json').read_text(encoding='utf-8-sig'))
    frozen = {e['path']: e for e in manifest['inputs']}
    frozen_names = set(frozen)

    allowed_drift, unexpected_drift, unchanged, missing = [], [], [], []
    for rel in sorted(frozen_names):
        entry = frozen[rel]
        target = SOURCE_ROOT / pathlib.PurePosixPath(rel)
        if not target.is_file():
            missing.append({'path': rel, 'frozenSha256': entry['sha256']})
            continue
        actual_size = target.stat().st_size
        actual_sha = sha256_of(target)
        if actual_sha == entry['sha256'] and actual_size == entry['sizeBytes']:
            unchanged.append(rel)
            continue
        item = {'path': rel, 'frozenSha256': entry['sha256'], 'actualSha256': actual_sha,
                'frozenSize': entry['sizeBytes'], 'actualSize': actual_size}
        if rel in ALLOWED:
            allowed_drift.append(item)
        else:
            unexpected_drift.append(item)

    whitelist_path = SOURCE_ROOT / 'scripts' / 'source-snapshot-inputs.txt'
    whitelist = read_whitelist(whitelist_path)
    new_inputs = [p for p in whitelist if p not in frozen_names]

    report = {
        'schemaVersion': 1,
        'label': label,
        'generatedUtc': datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
        'frozenManifest': str(evidence_root / 'g0' / 'input-manifest.json'),
        'frozenManifestSha256': sha256_of(evidence_root / 'g0' / 'input-manifest.json'),
        'sourceRoot': str(SOURCE_ROOT),
        'head': (SOURCE_ROOT / '.git' / 'HEAD').read_text(encoding='ascii').strip(),
        'counts': {
            'frozen': len(frozen_names),
            'unchanged': len(unchanged),
            'allowedDrift': len(allowed_drift),
            'unexpectedDrift': len(unexpected_drift),
            'missing': len(missing),
            'newInputsDeclaredInWhitelist': len(new_inputs),
        },
        'allowedDrift': allowed_drift,
        'unexpectedDrift': unexpected_drift,
        'missing': missing,
        'newInputsDeclaredInWhitelist': new_inputs,
        'changedRelativeToR2Count': len(allowed_drift),
    }
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)
        fh.write('\n')
    print(json.dumps({'label': label, 'counts': report['counts'],
                      'allowed': [d['path'] for d in allowed_drift],
                      'unexpected': [d['path'] for d in unexpected_drift]}, ensure_ascii=False))
    return 1 if (unexpected_drift or missing) else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
