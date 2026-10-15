# audit-all-manifests.py - R4 full-hierarchy evidence manifest audit.
#
# Scope: every manifest.json under docs/market-execution (top-level MG packages, every MR run
# package, and packages added by this remediation round).
#
# Per manifest it checks:
#   * path boundary   - a listed path must not escape its own package directory;
#   * completeness    - every listed file exists;
#   * unlisted files  - every file in the package (except manifest.json / manifest.sha256) is listed;
#   * sha256 + size   - match the recorded values;
#   * runId           - matches the package directory name when the manifest declares one;
#   * self entry      - a manifest listing manifest.json inside itself is a failure.
#
# Two mismatches were found by the independent acceptance (F-05) and are intentionally NOT
# rewritten (history stays readable). They are listed in known-mismatches.json and reported
# separately as knownMismatches; any OTHER failure makes this script exit non-zero.
import hashlib
import json
import pathlib
import sys

HERE = pathlib.Path(__file__).parent
# Defaults are derived from this file's own location on purpose: passing a path that contains
# non-ASCII characters through argv is unreliable on this host (the shell may re-encode it), and a
# silently wrong root would produce a "0 manifests / 0 failures" false pass.
ROOT = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else HERE.parent.parent
OUT = pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else HERE.parent / (HERE.name + '-audit.json')
KNOWN_FILE = pathlib.Path(sys.argv[3]) if len(sys.argv) > 3 else HERE / 'known-mismatches.json'

if not ROOT.is_dir():
    print(json.dumps({'error': 'root not found', 'root': str(ROOT)}, ensure_ascii=False))
    sys.exit(2)

EXEMPT_NAMES = {'manifest.json', 'manifest.sha256'}

known = {}
if KNOWN_FILE.is_file():
    for item in json.loads(KNOWN_FILE.read_text(encoding='utf-8-sig')).get('mismatches', []):
        key = (item['manifest'].replace('\\', '/'), item['path'].replace('\\', '/'))
        known[key] = item.get('reason', '')


def norm(value):
    return str(value or '').replace('\\', '/')


def entry_size(entry):
    for key in ('sizeBytes', 'bytes', 'size'):
        if isinstance(entry.get(key), int):
            return entry[key]
    return None


manifests = sorted(ROOT.rglob('manifest.json'))
runs = []
failures = []
known_hits = []
self_entries = []
total_entries = 0

for mf in manifests:
    pkg = mf.parent
    rel_manifest = norm(mf.relative_to(ROOT))
    try:
        data = json.loads(mf.read_text(encoding='utf-8-sig'))
    except Exception as exc:  # noqa: BLE001 - audit must record every unreadable manifest
        failures.append({'manifest': rel_manifest, 'reason': 'unreadable: ' + str(exc)})
        continue

    entries = data.get('files')
    if entries is None:
        entries = data.get('entries', data.get('artifacts', []))
    listed = set()
    checked = 0

    for entry in entries:
        name = norm(entry.get('path', ''))
        if not name:
            failures.append({'manifest': rel_manifest, 'reason': 'entry without path'})
            continue
        if pathlib.PurePosixPath(name).name in EXEMPT_NAMES:
            self_entries.append({'manifest': rel_manifest, 'path': name})
            failures.append({'manifest': rel_manifest, 'path': name, 'reason': 'manifest lists itself'})
            continue

        target = pkg / name
        try:
            inside = target.resolve().is_relative_to(pkg.resolve())
        except OSError:
            inside = False
        if not inside:
            failures.append({'manifest': rel_manifest, 'path': name, 'reason': 'path escapes package'})
            continue

        listed.add(name)
        checked += 1
        total_entries += 1

        if not target.is_file():
            failures.append({'manifest': rel_manifest, 'path': name, 'reason': 'missing'})
            continue

        actual_sha = hashlib.sha256(target.read_bytes()).hexdigest().upper()
        actual_size = target.stat().st_size
        recorded_sha = (entry.get('sha256') or '').upper()
        recorded_size = entry_size(entry)
        hash_ok = actual_sha == recorded_sha
        size_ok = recorded_size is None or actual_size == recorded_size
        if hash_ok and size_ok:
            continue

        record = {
            'manifest': rel_manifest, 'path': name,
            'hashMatches': hash_ok, 'sizeMatches': size_ok,
            'actualSha256': actual_sha, 'recordedSha256': recorded_sha,
            'actualSize': actual_size, 'recordedSize': recorded_size,
        }
        if (rel_manifest, name) in known:
            record['reason'] = known[(rel_manifest, name)]
            known_hits.append(record)
        else:
            failures.append(record)

    unlisted = []
    for path in sorted(pkg.rglob('*')):
        if not path.is_file() or path.name in EXEMPT_NAMES:
            continue
        rel = norm(path.relative_to(pkg))
        if rel not in listed:
            unlisted.append(rel)
    if unlisted:
        failures.append({'manifest': rel_manifest, 'reason': 'unlisted files', 'paths': unlisted})

    declared_run = data.get('runId')
    if declared_run and declared_run != pkg.name:
        failures.append({'manifest': rel_manifest, 'reason': 'runId mismatch',
                         'declaredRunId': declared_run, 'directory': pkg.name})

    runs.append({'manifest': rel_manifest, 'package': norm(pkg.relative_to(ROOT)),
                 'filesChecked': checked, 'declaredRunId': declared_run})

result = {
    'root': str(ROOT),
    'manifestCount': len(runs),
    'fileEntries': total_entries,
    'selfEntries': self_entries,
    'knownMismatches': known_hits,
    'failures': failures,
    'runs': runs,
}
OUT.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')

print(json.dumps({
    'manifestCount': result['manifestCount'],
    'fileEntries': result['fileEntries'],
    'selfEntries': len(self_entries),
    'knownMismatches': len(known_hits),
    'failures': len(failures),
}, ensure_ascii=False))
for item in failures[:20]:
    print('FAIL ' + json.dumps(item, ensure_ascii=False)[:400])
for item in known_hits:
    print('KNOWN ' + json.dumps(item, ensure_ascii=False)[:400])

sys.exit(1 if failures else 0)
