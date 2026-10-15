import hashlib, json, pathlib, shutil, tempfile, subprocess
BASE = pathlib.Path(__file__).parent
RUN = BASE.parents[1] / 'market-execution' / 'LONG-QUALITY-GOAL' / '20261003-092612Z-b4d896a8'
PKG = RUN / 'FINAL-LOCAL-QUALITY' / RUN.name
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def save(name,obj): (BASE/name).write_text(json.dumps(obj,ensure_ascii=False,indent=2),encoding='utf-8')
m=json.loads((PKG/'manifest.json').read_text(encoding='utf-8-sig'))
issues=[]; listed=set()
for e in m['files']:
    listed.add(e['path']); p=PKG/e['path']
    if not p.is_file() or p.stat().st_size!=e['sizeBytes'] or sha(p)!=e['sha256'].upper(): issues.append(e['path'])
actual={p.relative_to(PKG).as_posix() for p in PKG.rglob('*') if p.is_file()}
detached=(PKG/'manifest.sha256').read_text().split()[0].upper()
save('package-integrity.json',dict(count=len(m['files']),issues=issues,unlisted=sorted(actual-listed-{'manifest.json','manifest.sha256'}),manifestSha256=sha(PKG/'manifest.json'),detachedMatches=detached==sha(PKG/'manifest.json')))
terminal=json.loads((PKG/'g4-terminal/source-after-manifest.json').read_text(encoding='utf-8-sig'))
snapshot=pathlib.Path(tempfile.mkdtemp(prefix='codex-quality-acceptance-'))
drift=[]; copied=[]
for e in terminal['inputs']:
    p=pathlib.Path(terminal['sourceRoot'])/e['path']; dst=snapshot/e['path']
    if not p.is_file(): drift.append(dict(path=e['path'],reason='missing')); continue
    h=sha(p)
    if h!=e['sha256'].upper() or p.stat().st_size!=e['sizeBytes']: drift.append(dict(path=e['path'],expected=e['sha256'],actual=h))
    dst.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(p,dst)
    if sha(dst)!=h: raise RuntimeError('snapshot mismatch '+e['path'])
    copied.append(dict(path=e['path'],sha256=h,sizeBytes=dst.stat().st_size))
save('independent-inputs.json',dict(sourceRoot=terminal['sourceRoot'],executionRoot=str(snapshot),inputs=copied,sealedInputCount=len(terminal['inputs']),drift=drift))
print(json.dumps(dict(packageEntries=len(listed),packageIssues=issues,unlisted=sorted(actual-listed-{'manifest.json','manifest.sha256'}),detachedMatches=detached==sha(PKG/'manifest.json'),inputs=len(copied),drift=drift,snapshot=str(snapshot)),ensure_ascii=True))
