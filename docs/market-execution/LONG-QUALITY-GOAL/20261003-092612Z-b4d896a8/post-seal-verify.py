#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""post-seal-verify.py - independent check of a sealed LONG-QUALITY-GOAL package.

Re-hashes every file listed in manifest.json, looks for files present but not listed, and compares
manifest.json with its detached manifest.sha256. Runs from OUTSIDE the package and never writes
inside it.

usage: python -X utf8 post-seal-verify.py <packageDir> <outputJson>
"""

import datetime
import hashlib
import json
import pathlib
import sys


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest().upper()


def main(argv):
    pkg = pathlib.Path(argv[1])
    out_path = pathlib.Path(argv[2])
    manifest = json.loads((pkg / 'manifest.json').read_text(encoding='utf-8-sig'))

    problems = []
    checked = 0
    listed = set()
    for entry in manifest['files']:
        rel = entry['path']
        listed.add(rel)
        target = pkg / pathlib.PurePosixPath(rel)
        if not target.is_file():
            problems.append({'path': rel, 'problem': 'missing'})
            continue
        checked += 1
        actual = sha256_of(target)
        if actual != entry['sha256']:
            problems.append({'path': rel, 'problem': 'sha-mismatch',
                             'recorded': entry['sha256'], 'actual': actual})
        if target.stat().st_size != entry['sizeBytes']:
            problems.append({'path': rel, 'problem': 'size-mismatch'})

    listed |= {'manifest.json', 'manifest.sha256'}
    extra = []
    for p in sorted(pkg.rglob('*')):
        if not p.is_file():
            continue
        rel = p.relative_to(pkg).as_posix()
        if rel not in listed:
            extra.append(rel)

    manifest_sha = sha256_of(pkg / 'manifest.json')
    detached = (pkg / 'manifest.sha256').read_text(encoding='ascii').strip().split()[0].upper()

    report = {
        'schemaVersion': 1,
        'checkedUtc': datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
        'package': str(pkg),
        'runId': manifest.get('runId'),
        'manifestDeclaredCount': manifest.get('count'),
        'filesListed': len(manifest['files']),
        'filesChecked': checked,
        'problems': problems,
        'extraFilesNotListed': extra,
        'manifestSha256': manifest_sha,
        'detachedSha256': detached,
        'detachedMatches': manifest_sha == detached,
        'countMatches': manifest.get('count') == len(manifest['files']),
        'verdict': 'PASS' if (not problems and not extra and manifest_sha == detached
                              and manifest.get('count') == len(manifest['files'])) else 'FAIL',
    }
    with open(out_path, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)
        fh.write('\n')
    print(json.dumps({k: report[k] for k in ('runId', 'filesListed', 'filesChecked',
                                             'problemsCount', 'extraCount', 'detachedMatches',
                                             'verdict') if k in report} | {'extraCount': len(extra),
                                                                           'problemsCount': len(problems)},
                     ensure_ascii=False))
    return 0 if report['verdict'] == 'PASS' else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
