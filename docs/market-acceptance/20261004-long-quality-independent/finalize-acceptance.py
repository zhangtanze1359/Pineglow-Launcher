import json,pathlib,hashlib,shutil,subprocess,xml.etree.ElementTree as ET
from datetime import datetime,timezone,timedelta
base=pathlib.Path(__file__).parent
def load(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def save(p,o):p.write_text(json.dumps(o,ensure_ascii=False,indent=2),encoding='utf-8')
inputs=load(base/'independent-inputs.json'); source=pathlib.Path(inputs['sourceRoot']); snapshot=pathlib.Path(inputs['executionRoot'])
trx=[]
for p in sorted((base/'results').glob('*.trx')):
    root=ET.parse(p).getroot();counter=next(e for e in root.iter() if e.tag.endswith('Counters'))
    results=[e for e in root.iter() if e.tag.endswith('UnitTestResult')]
    trx.append(dict(path=p.relative_to(base).as_posix(),counters=counter.attrib,resultCount=len(results),outcomes={o:sum(1 for e in results if e.attrib.get('outcome')==o) for o in {e.attrib.get('outcome') for e in results}},sha256=sha(p)))
disproof=load(base/'runner-disproof-results.json')
dest=base/'runner-fixtures-preserved'
if not dest.exists():shutil.copytree(pathlib.Path(disproof['fixtureRoot']),dest)
review=base/'reviewed-source'
for rel in ['scripts/run-goal-validation.ps1','scripts/tests/goal-validation.test.cjs','scripts/audit-goal-evidence.py','ui/js/launcher.js','ui/tests/goal-core-flow.test.cjs']:
    dst=review/rel;dst.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(snapshot/rel,dst)
drift=[]
for e in inputs['inputs']:
    p=source/e['path']
    if not p.is_file() or sha(p)!=e['sha256']:drift.append(e['path'])
status=subprocess.run(['git','-C',str(source),'status','--short','--untracked-files=all'],stdout=subprocess.PIPE,check=True).stdout
(base/'source-status-after.txt').write_bytes(status)
before=(base/'source-status-before.txt').read_text(encoding='utf-8-sig').replace('\r\n','\n')
after=status.decode('utf-8').replace('\r\n','\n')
head=subprocess.run(['git','-C',str(source),'rev-parse','HEAD'],stdout=subprocess.PIPE,check=True).stdout.decode().strip()
audit=load(base/'audit-real.json')
summary=dict(schemaVersion=1,generatedHongKong=datetime.now(timezone(timedelta(hours=8))).isoformat(),sourceRoot=str(source),executionRoot=str(snapshot),sourceHead=head,sourceInputCount=len(inputs['inputs']),sourceDriftDuringAcceptance=drift,sourceStatusUnchanged=before==after,packageIntegrity=load(base/'package-integrity.json'),auditTotals=audit['totals'],trx=trx,csharpExecuted=sum(int(t['counters']['executed']) for t in trx),csharpPassed=sum(int(t['counters']['passed']) for t in trx),csharpRequiredSkipped=sum(int(t['counters']['notExecuted']) for t in trx),node=dict(tests=266,passed=266,failed=0,skipped=0),auditorTests=dict(tests=26,passed=26),runnerExistingTests=dict(tests=10,passed=10,environmentCorrection='PSModulePath set only in test parent process; first failure log retained'),importIndependentTests=dict(tests=3,passed=1,failed=2,skipped=0,exitCode=1),runnerDisproofCases=len(disproof['cases']),runnerUnexpectedPassCases=sum(1 for c in disproof['cases'] if c['actual']['exitCode']==0 and c['actual']['verdict']['verdict']=='PASS'),bisectChecks=19,goalGates={'G0':'PASS','G1':'PASS','G2':'FAIL','G3':'PASS','G4':'FAIL'},technicalAcceptance='FAIL',implementationReadyForAcceptedGoal=False,formalPhaseApproval='NOT_GRANTED',releaseApproval='NOT_GRANTED',limitations=['controlled DOM is not real WebView','73 selected C# tests exclude ImportWithALiveTokenButCancelledMidway; no full C# suite claimed','RA-01/02 remain excluded by earlier platform scope','real games/credential backend/update handover/cross-platform/clean checkout CI/release not verified'])
save(base/'acceptance-summary.json',summary)
print(json.dumps({k:summary[k] for k in ['sourceInputCount','sourceDriftDuringAcceptance','sourceStatusUnchanged','csharpExecuted','csharpPassed','runnerUnexpectedPassCases','goalGates','technicalAcceptance']},ensure_ascii=True))
