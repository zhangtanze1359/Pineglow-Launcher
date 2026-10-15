import pathlib,json,hashlib,tempfile,shutil,difflib
BASE=pathlib.Path(__file__).parent
RUN=BASE.parents[1]/'market-execution/LONG-QUALITY-REWORK/20261004-092504Z-dab448e2'
PKG=RUN/'r4/FINAL-LOCAL-QUALITY/20261004-092504Z-dab448e2-rework'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def load(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def save(n,o):(BASE/n).write_text(json.dumps(o,ensure_ascii=False,indent=2),encoding='utf-8')
m=load(PKG/'manifest.json');entries=m['entries'];issues=[]
for e in entries:
 p=PKG/e['path']
 if not p.is_file() or sha(p)!=e['sha256'] or p.stat().st_size!=e['sizeBytes']:issues.append(e['path'])
listed={e['path'] for e in entries};actual={p.relative_to(PKG).as_posix() for p in PKG.rglob('*') if p.is_file()}
detached=RUN/'r4/20261004-092504Z-dab448e2-rework.manifest.sha256'
save('package-integrity.json',dict(entries=len(entries),issues=issues,unlisted=sorted(actual-listed-{'manifest.json'}),manifestSha256=sha(PKG/'manifest.json'),actualDetached=str(detached),actualDetachedMatches=detached.read_text().split()[0].upper()==sha(PKG/'manifest.json'),internalDetachedPresent=(PKG/'manifest.sha256').is_file(),documentedRelativeDetachedExists=(PKG/'../20261004-092504Z-dab448e2-rework.manifest.sha256').is_file()))
terminal=load(PKG/'source/terminal-source-after-manifest.json');source=pathlib.Path(terminal['sourceRoot'])
snapshot=pathlib.Path(tempfile.mkdtemp(prefix='codex-rework-acceptance-'))
copied=[];drift=[]
for e in terminal['inputs']:
 p=source/e['path'];dst=snapshot/e['path']
 if not p.is_file():drift.append(dict(path=e['path'],reason='missing'));continue
 h=sha(p)
 if h!=e['sha256'] or p.stat().st_size!=e['sizeBytes']:drift.append(dict(path=e['path'],expected=e['sha256'],actual=h))
 dst.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(p,dst)
 if sha(dst)!=h:raise RuntimeError('copy mismatch '+e['path'])
 copied.append(dict(path=e['path'],sizeBytes=dst.stat().st_size,sha256=h))
independent=dict(schemaVersion=1,sourceRoot=str(source),executionRoot=str(snapshot),inputs=copied,drift=drift)
save('independent-inputs.json',independent)
old=BASE.parent/'20261004-long-quality-independent/reviewed-source'
for rel in ['ui/js/launcher.js','ui/tests/goal-core-flow.test.cjs','scripts/run-goal-validation.ps1','scripts/tests/goal-validation.test.cjs']:
 if (old/rel).is_file():
  diff=''.join(difflib.unified_diff((old/rel).read_text(encoding='utf-8').splitlines(True),(snapshot/rel).read_text(encoding='utf-8').splitlines(True),fromfile='previous/'+rel,tofile='current/'+rel))
  (BASE/(rel.replace('/','_')+'.diff')).write_text(diff,encoding='utf-8')
expected=dict(schemaVersion=1,roots=[dict(id='rework-final',path=str(PKG.parent),required=True,schemaPolicy='strict-new',packageCount=1,packages=[dict(relativePath=PKG.name,runId=PKG.name,required=True,schemaPolicy='strict-new',manifestSha256=sha(PKG/'manifest.json'))])],exceptions=[])
save('expected-rework-strict.json',expected)
print(json.dumps(dict(packageEntries=len(entries),packageIssues=issues,detachedMatches=detached.read_text().split()[0].upper()==sha(PKG/'manifest.json'),internalDetachedPresent=(PKG/'manifest.sha256').is_file(),inputs=len(copied),drift=drift,snapshot=str(snapshot)),ensure_ascii=True))
