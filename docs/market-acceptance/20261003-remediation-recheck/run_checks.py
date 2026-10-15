import hashlib, json, pathlib, shutil, subprocess, time, uuid
from datetime import datetime, timezone

OUT = pathlib.Path(__file__).parent
SOURCE = pathlib.Path('D:/松光启动器')
DELIVERY = pathlib.Path('C:/Users/Administrator/.codex/worktrees/ed41/松光启动器/docs')
RUN = DELIVERY / 'market-execution/FINAL-REMEDIATION/20261003-052200Z'
SNAP = pathlib.Path.home() / 'AppData/Local/Temp' / ('pineglow-codex-recheck-' + uuid.uuid4().hex)

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest().upper()
def save(name, value): (OUT / name).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')
def command(label, args, cwd):
    start = datetime.now(timezone.utc).isoformat()
    proc = subprocess.run(args, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (OUT / (label + '.log')).write_bytes(proc.stdout)
    item = dict(label=label, command=args, cwd=str(cwd), startUTC=start,
                endUTC=datetime.now(timezone.utc).isoformat(), exitCode=proc.returncode)
    save(label + '.command.json', item)
    print(json.dumps(item, ensure_ascii=False), flush=True)
    return proc.returncode

tracked = subprocess.check_output(['git', '-C', str(SOURCE), 'ls-files', '-z']).decode('utf-8').split('\0')
extras = [x.strip() for x in (SOURCE / 'scripts/source-snapshot-inputs.txt').read_text(encoding='utf-8-sig').splitlines() if x.strip() and not x.lstrip().startswith('#')]
inputs = sorted(set([x for x in tracked if x] + extras))
SNAP.mkdir()  # fresh GUID directory only; never delete or reuse an existing checkout
records = []
for rel in inputs:
    src, dst = SOURCE / rel, SNAP / rel
    if not src.is_file(): raise RuntimeError('missing input: ' + rel)
    if not dst.resolve().is_relative_to(SNAP.resolve()): raise RuntimeError('unsafe input: ' + rel)
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(src, dst)
    digest = sha(src)
    if sha(dst) != digest: raise RuntimeError('copy mismatch: ' + rel)
    records.append(dict(path=rel, sha256=digest, sizeBytes=src.stat().st_size))
probe_inputs = [('market-acceptance/20261003-final/FinalAcceptanceProbes.cs','FinalAcceptanceProbes.cs'),
                ('market-acceptance/20261003-W1-W3/IndependentRouteProbes.cs','IndependentRouteProbes.cs')]
for src_rel, name in probe_inputs:
    src = DELIVERY / src_rel
    dst = SNAP / 'dev/PineglowLauncher.Tests' / name
    shutil.copyfile(src, dst)
    records.append(dict(path=str(dst.relative_to(SNAP)).replace('\\','/'), sha256=sha(src), sizeBytes=src.stat().st_size, source=str(src)))
save('snapshot-manifest.json', dict(sourceRoot=str(SOURCE), snapshotRoot=str(SNAP), count=len(records), files=records))
(OUT / 'snapshot-root.txt').write_text(str(SNAP), encoding='utf-8')
status = subprocess.check_output(['git','-C',str(SOURCE),'status','--short','--untracked-files=all'])
(OUT / 'source-status-before.txt').write_bytes(status)
real_config = pathlib.Path.home() / 'AppData/Roaming/MCMiniLauncher'
config_state = {name: sha(real_config/name) if (real_config/name).is_file() else None for name in ['config.json','config.json.bak','config.json.prev','secrets.json']}
save('config-before.json', config_state)

repair_manifest = json.loads((RUN / 'green-snapshot-manifest.json').read_text(encoding='utf-8-sig'))
drift = []
for item in repair_manifest['files']:
    rel = item['path']; p = SNAP / rel
    if not p.is_file() or sha(p) != item['sha256'].upper(): drift.append(rel)
save('delivery-source-binding.json', dict(recordCount=len(repair_manifest['files']), drift=drift))
print('snapshot=' + str(SNAP) + ' inputs=' + str(len(records)) + ' deliveryDrift=' + str(len(drift)), flush=True)
test_project='dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj'
common=['dotnet','test',test_project,'-c','Release','--results-directory',str(OUT/'test-results')]
command('01-r0',common+['--filter','FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~SecretStoreBackendTests','--logger','trx;LogFileName=r0.trx'],SNAP)
command('02-existing-probes',common+['--no-restore','--filter','FullyQualifiedName~FinalAcceptanceProbes|FullyQualifiedName~IndependentRouteProbes|FullyQualifiedName~FinalAcceptanceRegressionTests','--logger','trx;LogFileName=existing-probes.trx'],SNAP)
command('03-full-csharp',common+['--no-restore','--logger','trx;LogFileName=full.trx'],SNAP)
node_inputs=sorted(str(p) for p in (SNAP/'ui/tests').glob('*.test.cjs'))
save('node-inputs.json', node_inputs)
command('04-node',['node','--test']+node_inputs,SNAP)
command('05-verifyhost-build',['dotnet','build','dev/PineglowLauncher/VerifyHost/VerifyHost.csproj','-c','Release'],SNAP)
command('06-bisect-smoke',[str(SNAP/'dev/PineglowLauncher/VerifyHost/bin/Release/net10.0/VerifyHost.exe'),'--bisect-smoke'],SNAP)
save('source-protection.json',dict(statusUnchanged=status==subprocess.check_output(['git','-C',str(SOURCE),'status','--short','--untracked-files=all']),
    sourceHashDrift=[r['path'] for r in records if 'source' not in r and sha(SOURCE/r['path'])!=r['sha256']],
    configUnchanged=config_state=={name:sha(real_config/name) if (real_config/name).is_file() else None for name in config_state}))
