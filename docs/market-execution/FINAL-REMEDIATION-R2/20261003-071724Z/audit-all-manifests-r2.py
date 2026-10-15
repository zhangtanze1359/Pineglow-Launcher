#!/usr/bin/env python3
"""audit-all-manifests-r2.py - R2 evidence manifest audit (RA-04 fixed).

Closes the three proven false passes of the previous auditor:

  1. "empty root"    -> a target set that is empty (or a root without manifests) is a FAILURE;
  2. "known path, third bytes" -> a historical exception is no longer exempted by path alone:
                        the manifest's recorded old sha/size AND the registered current sha/size
                        must all match; any further drift fails;
  3. "bad detached"  -> manifest.sha256 is verified against manifest.json (packages created by
                        this remediation line are required to carry one).

Coverage: the target set spans **all** delivery roots declared in audit-roots.json
(historical packages live in the ed41 worktree, this round's package lives in 0f45),
so it covers the top-level MG packages, every MR run package and the new one.

Per manifest it checks: path boundary, missing entries, duplicates, unlisted files,
sha256 + size, runId, self-reference (manifest.json listed inside itself) and the detached digest.

Exit codes: 0 = no failures (historical exceptions reported separately),
1 = failures, 2 = no usable target root.
"""
import hashlib
import json
import pathlib
import sys

HERE = pathlib.Path(__file__).parent
ROOTS_FILE = HERE / 'audit-roots.json'
DEFAULT_OUT = HERE.parent / (HERE.name + '-audit.json')
EXC_FILE = HERE / 'historical-exceptions.json'

EXEMPT_NAMES = {'manifest.json', 'manifest.sha256'}
REQUIRE_DETACHED_MARKER = 'FINAL-REMEDIATION'
EXCEPTION_FIELDS = ('recordedSha256', 'recordedSize', 'currentSha256', 'currentSize')


def norm(value):
    return str(value or '').replace('\\', '/')


def entry_size(entry):
    for key in ('sizeBytes', 'bytes', 'size'):
        if isinstance(entry.get(key), int):
            return entry[key]
    return None


def sha256_of(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def resolve_roots():
    """argv[1] (single root, used by the negative tests) > audit-roots.json > package's parent's parent."""
    if len(sys.argv) > 1 and sys.argv[1].strip():
        return [pathlib.Path(sys.argv[1])]
    if ROOTS_FILE.is_file():
        try:
            data = json.loads(ROOTS_FILE.read_text(encoding='utf-8-sig'))
            roots = [pathlib.Path(p) for p in data.get('roots', []) if str(p).strip()]
            if roots:
                return roots
        except Exception:  # noqa: BLE001 - falling back is fine, it will be reported as empty/failure
            pass
    return [HERE.parent.parent]


def main():
    roots = resolve_roots()
    out = pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else DEFAULT_OUT
    exc_file = pathlib.Path(sys.argv[3]) if len(sys.argv) > 3 else EXC_FILE

    failures = []
    known_hits = []
    missing_detached = []
    self_entries = []
    duplicate_entries = []
    runs = []
    total_entries = 0

    usable_roots = [r for r in roots if r.is_dir()]
    if not usable_roots:
        print(json.dumps({'error': 'no usable root', 'roots': [str(r) for r in roots]}, ensure_ascii=False))
        return 2

    exceptions = {}
    if not exc_file.is_file():
        failures.append({'reason': 'historical-exceptions file missing', 'file': str(exc_file)})
    else:
        try:
            raw = json.loads(exc_file.read_text(encoding='utf-8-sig'))
        except Exception as exc:  # noqa: BLE001
            failures.append({'reason': 'historical-exceptions unreadable: ' + str(exc), 'file': str(exc_file)})
            raw = {}
        for item in raw.get('exceptions', []):
            key = (norm(item.get('manifest')), norm(item.get('path')))
            absent = [f for f in EXCEPTION_FIELDS if item.get(f) in (None, '')]
            if absent:
                failures.append({'manifest': key[0], 'path': key[1],
                                 'reason': 'exception entry missing fields: ' + ','.join(absent)})
                continue
            exceptions[key] = item

    manifest_total = 0
    for root in usable_roots:
        manifests = sorted(root.rglob('manifest.json'))
        manifest_total += len(manifests)
        for mf in manifests:
            pkg = mf.parent
            rel_manifest = norm(mf.relative_to(root))
            try:
                data = json.loads(mf.read_text(encoding='utf-8-sig'))
            except Exception as exc:  # noqa: BLE001
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
                if name in listed:
                    duplicate_entries.append({'manifest': rel_manifest, 'path': name})
                    failures.append({'manifest': rel_manifest, 'path': name, 'reason': 'duplicate entry'})
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

                actual_sha = sha256_of(target)
                actual_size = target.stat().st_size
                recorded_sha = (entry.get('sha256') or '').upper()
                recorded_size = entry_size(entry)
                if actual_sha == recorded_sha and (recorded_size is None or actual_size == recorded_size):
                    continue

                record = {
                    'manifest': rel_manifest, 'path': name,
                    'actualSha256': actual_sha, 'recordedSha256': recorded_sha,
                    'actualSize': actual_size, 'recordedSize': recorded_size,
                }
                exc = exceptions.get((rel_manifest, name))
                if exc is None:
                    record['reason'] = 'sha256/size mismatch (not a registered exception)'
                    failures.append(record)
                    continue

                manifest_matches_recorded = (
                    recorded_sha == str(exc['recordedSha256']).upper()
                    and recorded_size == int(exc['recordedSize']))
                actual_matches_registered = (
                    actual_sha == str(exc['currentSha256']).upper()
                    and actual_size == int(exc['currentSize']))
                if manifest_matches_recorded and actual_matches_registered:
                    record['reason'] = exc.get('reason', 'registered historical exception')
                    known_hits.append(record)
                else:
                    record['reason'] = ('historical exception drifted again: manifest/recorded/current '
                                        'triple no longer matches the registration')
                    record['manifestMatchesRecorded'] = manifest_matches_recorded
                    record['actualMatchesRegistered'] = actual_matches_registered
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

            detached = pkg / 'manifest.sha256'
            if detached.is_file():
                text = detached.read_text(encoding='utf-8-sig').strip().split()
                declared = text[0].upper() if text else ''
                actual_detached = sha256_of(mf)
                if declared != actual_detached:
                    failures.append({'manifest': rel_manifest, 'reason': 'detached manifest.sha256 mismatch',
                                     'declared': declared, 'actual': actual_detached})
            elif REQUIRE_DETACHED_MARKER in norm(pkg):
                failures.append({'manifest': rel_manifest,
                                 'reason': 'missing detached manifest.sha256 (required for this remediation line)'})
            else:
                missing_detached.append(rel_manifest)

            runs.append({'manifest': rel_manifest, 'root': str(root), 'package': norm(pkg.relative_to(root)),
                         'filesChecked': checked, 'declaredRunId': declared_run})

    if manifest_total == 0:
        failures.append({'reason': 'non-empty target required: no manifest.json found under any root',
                         'roots': [str(r) for r in usable_roots]})

    result = {
        'roots': [str(r) for r in usable_roots],
        'manifestCount': len(runs),
        'fileEntries': total_entries,
        'selfEntries': self_entries,
        'duplicateEntries': duplicate_entries,
        'historicalExceptionsRetained': True,
        'historicalExceptionCount': len(known_hits),
        'knownMismatches': known_hits,
        'missingDetached': missing_detached,
        'failures': failures,
        'runs': runs,
    }
    out.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')

    print(json.dumps({
        'roots': len(usable_roots),
        'manifestCount': result['manifestCount'],
        'fileEntries': result['fileEntries'],
        'selfEntries': len(self_entries),
        'knownMismatches': len(known_hits),
        'missingDetached': len(missing_detached),
        'failures': len(failures),
        'exit': 1 if failures else 0,
    }, ensure_ascii=False))
    for item in failures[:20]:
        print('FAIL ' + json.dumps(item, ensure_ascii=False)[:400])
    for item in known_hits:
        print('RETAINED-HISTORICAL ' + json.dumps(item, ensure_ascii=False)[:300])

    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
