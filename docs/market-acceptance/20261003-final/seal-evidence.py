import hashlib, json, pathlib, subprocess, datetime

root = pathlib.Path(__file__).parent
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest().upper()
inputs = json.loads((root / 'snapshot-manifest.json').read_text(encoding='utf-8-sig'))
source = pathlib.Path(inputs['sourceRoot'])
changed = [f['path'] for f in inputs['files'] if not (source / f['path']).is_file() or sha(source / f['path']) != f['sha256'].upper()]
current = subprocess.run(['git','-C',str(source),'status','--short','--untracked-files=all'],capture_output=True,check=True).stdout.decode('utf-8-sig').splitlines()
before = (root / 'source-status-before.txt').read_text(encoding='utf-8-sig').splitlines()
status_diff = sorted(set(current).symmetric_difference(before))
config_before = json.loads((root / 'real-config-before.json').read_text(encoding='utf-8-sig'))
configs = []
for item in config_before:
    p = pathlib.Path(item['path'])
    expected = item.get('sha256',item.get('sha',item.get('hash')))
    actual = sha(p) if p.is_file() else None
    configs.append({'path':str(p),'beforeSha':expected,'afterSha':actual,'changed':expected != actual})
check = {'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'inputs':len(inputs['files']),'changed':changed,'beforeStatusEntries':len(before),'afterStatusEntries':len(current),'statusDiff':status_diff,'realConfigFiles':configs,'nativeCredentialsInspected':False}
(root / 'source-final-check.json').write_text(json.dumps(check,ensure_ascii=False,indent=2),encoding='utf-8')
if changed or status_diff or any(x['changed'] for x in configs):
    raise SystemExit('SOURCE OR CONFIG DRIFT: inspect source-final-check.json')
excluded = {'manifest.json','manifest.sha256','seal-verification.json'}
files = [{'path':p.relative_to(root).as_posix(),'sizeBytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(root.rglob('*')) if p.is_file() and p.name not in excluded]
docs = root.parent.parent
names = ['deepseek-harness-final-acceptance-2026-10-03.md','deepseek-harness-final-remediation-workflow-2026-10-03.md','deepseek-harness-final-remediation-prompt-2026-10-03.md']
artifacts = [{'absolutePath':str(docs/n),'sizeBytes':(docs/n).stat().st_size,'sha256':sha(docs/n)} for n in names]
manifest = {'schemaVersion':1,'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'sourceRoot':str(source),'snapshotRoot':inputs['snapshotRoot'],'files':files,'documentArtifacts':artifacts,'excluded':{'manifest.json':'detached digest avoids self-reference','manifest.sha256':'detached manifest digest','seal-verification.json':'derived validation result written after sealing'}}
mf = root / 'manifest.json'
mf.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
(root / 'manifest.sha256').write_text(sha(mf)+'  manifest.json\n',encoding='ascii')
failures = []
for f in files:
    p = root / f['path']
    if not p.resolve().is_relative_to(root.resolve()) or p.stat().st_size != f['sizeBytes'] or sha(p) != f['sha256']:
        failures.append(f['path'])
for f in artifacts:
    p = pathlib.Path(f['absolutePath'])
    if sha(p) != f['sha256'] or p.stat().st_size != f['sizeBytes']: failures.append(str(p))
if (root / 'manifest.sha256').read_text(encoding='ascii').split()[0] != sha(mf): failures.append('manifest digest')
result={'fileCount':len(files),'documentCount':len(artifacts),'failures':failures,'sourceProtected':True,'manifestSha256':sha(mf)}
(root / 'seal-verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result))
if failures: raise SystemExit(1)
