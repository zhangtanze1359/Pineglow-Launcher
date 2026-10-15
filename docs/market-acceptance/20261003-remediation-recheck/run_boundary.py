import json, pathlib, shutil, subprocess, hashlib, difflib, uuid
from datetime import datetime, timezone
BASE=pathlib.Path(__file__).parent
OUT=BASE/('boundary-run-'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')+'-'+uuid.uuid4().hex[:8])
OUT.mkdir()
SNAP=pathlib.Path((BASE/'snapshot-root.txt').read_text(encoding='utf-8'))
SOURCE=pathlib.Path('D:/松光启动器')
ROOT=pathlib.Path('C:/Users/Administrator/.codex/worktrees/ed41/松光启动器/docs/market-execution')
RUN=ROOT/'FINAL-REMEDIATION/20261003-052200Z'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def save(name,value):(OUT/name).write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
def command(label,args,cwd):
    start=datetime.now(timezone.utc).isoformat(); p=subprocess.run(args,cwd=cwd,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    (OUT/(label+'.log')).write_bytes(p.stdout)
    item=dict(command=args,cwd=str(cwd),startUTC=start,endUTC=datetime.now(timezone.utc).isoformat(),exitCode=p.returncode)
    save(label+'.command.json',item);print(label+' exit='+str(p.returncode),flush=True);return p.returncode

shutil.copyfile(BASE/'RemediationBoundaryProbes.cs',SNAP/'dev/PineglowLauncher.Tests/RemediationBoundaryProbes.cs')
args=['dotnet','test','dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj','-c','Release','--no-restore','--filter','FullyQualifiedName~RemediationBoundaryProbes','--results-directory',str(OUT/'test-results'),'--logger','trx;LogFileName=boundaries.trx']
command('07-new-boundaries',args,SNAP)
args[-1]='trx;LogFileName=boundaries-repeat.trx'
command('08-new-boundaries-repeat',args[:5]+['--no-build']+args[5:],SNAP)

old=pathlib.Path((RUN/'pre-fix-source-root.txt').read_text(encoding='utf-8-sig').strip())
baseline=json.loads((ROOT.parent/'market-acceptance/20261003-final/snapshot-manifest.json').read_text(encoding='utf-8-sig'))
delta=[];unchanged=0;diff=[]
for item in baseline['files']:
    rel=item['path']; p=SOURCE/rel
    if not p.is_file() or sha(p)!=item['sha256'].upper():
        delta.append(rel)
        if p.is_file() and (old/rel).is_file():
            diff.extend(difflib.unified_diff((old/rel).read_text(encoding='utf-8-sig').splitlines(True),p.read_text(encoding='utf-8-sig').splitlines(True),fromfile='before/'+rel,tofile='after/'+rel))
    else:unchanged+=1
save('independent-delta.json',dict(baselineCount=len(baseline['files']),unchanged=unchanged,changed=delta))
(OUT/'reviewed-diff.patch').write_text(''.join(diff),encoding='utf-8')

issues=[];count=0;manifests=[];detached=[]
for mf in sorted(ROOT.rglob('manifest.json')):
    data=json.loads(mf.read_text(encoding='utf-8-sig'));pkg=mf.parent;relmf=str(mf.relative_to(ROOT)).replace('\\','/')
    records=data.get('files',data.get('entries',data.get('artifacts',[])));listed=set()
    for item in records:
        rel=item.get('path','');target=pkg/rel;listed.add(rel.replace('\\','/'));count+=1
        if not target.resolve().is_relative_to(pkg.resolve()):issues.append(dict(manifest=relmf,path=rel,reason='outside'));continue
        if not target.is_file():issues.append(dict(manifest=relmf,path=rel,reason='missing'));continue
        size=next((item[k] for k in ['sizeBytes','bytes','size'] if k in item),None)
        if sha(target)!=(item.get('sha256') or '').upper() or (size is not None and size!=target.stat().st_size):
            issues.append(dict(manifest=relmf,path=rel,reason='hash/size mismatch',actualSHA=sha(target),actualSize=target.stat().st_size,recordedSHA=item.get('sha256'),recordedSize=size))
    for p in pkg.rglob('*'):
        if p.is_file() and p.name not in ['manifest.json','manifest.sha256'] and str(p.relative_to(pkg)).replace('\\','/') not in listed:
            issues.append(dict(manifest=relmf,path=str(p.relative_to(pkg)),reason='unlisted'))
    digest_file=pkg/'manifest.sha256'
    if digest_file.is_file():
        recorded=digest_file.read_text(encoding='utf-8-sig').split()[0].upper();ok=recorded==sha(mf)
        detached.append(dict(manifest=relmf,matches=ok))
        if not ok:issues.append(dict(manifest=relmf,reason='detached SHA mismatch'))
    manifests.append(relmf)
save('independent-manifest-audit.json',dict(manifestCount=len(manifests),fileEntries=count,issues=issues,detached=detached))
print('independent audit manifests='+str(len(manifests))+' entries='+str(count)+' issues='+str(len(issues)),flush=True)

# Exercise delivery auditor against new synthetic evidence; never edit a historical package.
fixture=OUT/'auditor-fixtures';fixture.mkdir(exist_ok=True)
empty=fixture/'empty-root';empty.mkdir(exist_ok=True)
command('09-auditor-empty-root',['python',str(RUN/'audit-all-manifests.py'),str(empty),str(OUT/'auditor-empty-result.json'),str(RUN/'known-mismatches.json')],OUT)
changed=fixture/'changed-known-root';pkg=changed/'MG0-verification-package-20261003';pkg.mkdir(parents=True,exist_ok=True)
(pkg/'drift-notes.md').write_text('synthetic unexpected third version - this is not the pinned known mismatch',encoding='utf-8')
save_path=pkg/'manifest.json'
save_path.write_text(json.dumps(dict(files=[dict(path='drift-notes.md',sha256='0'*64,sizeBytes=123)])),encoding='utf-8')
command('10-auditor-known-path-new-bytes',['python',str(RUN/'audit-all-manifests.py'),str(changed),str(OUT/'auditor-changed-result.json'),str(RUN/'known-mismatches.json')],OUT)
sealed=fixture/'bad-detached-root';pkg=sealed/'run';pkg.mkdir(parents=True,exist_ok=True)
(pkg/'data.txt').write_text('synthetic',encoding='utf-8')
(pkg/'manifest.json').write_text(json.dumps(dict(files=[dict(path='data.txt',sha256=sha(pkg/'data.txt'),sizeBytes=9)])),encoding='utf-8')
(pkg/'manifest.sha256').write_text('0'*64,encoding='utf-8')
command('11-auditor-bad-detached',['python',str(RUN/'audit-all-manifests.py'),str(sealed),str(OUT/'auditor-detached-result.json'),str(RUN/'known-mismatches.json')],OUT)
print('retained-new-run='+str(OUT),flush=True)
