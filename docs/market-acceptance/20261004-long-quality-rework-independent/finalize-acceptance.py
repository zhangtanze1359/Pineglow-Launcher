import pathlib,json,hashlib,shutil,subprocess,xml.etree.ElementTree as ET
from datetime import datetime,timezone,timedelta
B=pathlib.Path(__file__).parent
def load(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def save(n,o):(B/n).write_text(json.dumps(o,ensure_ascii=False,indent=2),encoding='utf-8')
inputs=load(B/'independent-inputs.json');src=pathlib.Path(inputs['sourceRoot']);snap=pathlib.Path(inputs['executionRoot'])
run=B.parents[1]/'market-execution/LONG-QUALITY-REWORK/20261004-092504Z-dab448e2';pkg=run/'r4/FINAL-LOCAL-QUALITY/20261004-092504Z-dab448e2-rework'
trx=[]
for p in sorted((B/'results').glob('*.trx')):
 x=ET.parse(p).getroot();c=next(e for e in x.iter() if e.tag.endswith('Counters'));r=[e for e in x.iter() if e.tag.endswith('UnitTestResult')]
 trx.append(dict(path=p.relative_to(B).as_posix(),counters=c.attrib,resultCount=len(r),passedRows=sum(e.attrib.get('outcome')=='Passed' for e in r),sha256=sha(p)))
probes=load(B/'runner-independent-contract-results.json')
dest=B/'runner-fixtures-preserved'
if not dest.exists():shutil.copytree(pathlib.Path(probes['fixtureRoot']),dest)
review=B/'reviewed-source'
for rel in ['scripts/run-goal-validation.ps1','scripts/tests/goal-validation.test.cjs','scripts/audit-goal-evidence.py','ui/js/launcher.js','ui/js/i18n.js','ui/tests/goal-core-flow.test.cjs','ui/tests/import-cancel.test.cjs']:
 dst=review/rel;dst.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(snap/rel,dst)
sourceDrift=[e['path'] for e in inputs['inputs'] if not (src/e['path']).is_file() or sha(src/e['path'])!=e['sha256']]
snapshotDrift=[e['path'] for e in inputs['inputs'] if not (snap/e['path']).is_file() or sha(snap/e['path'])!=e['sha256']]
status=subprocess.run(['git','-C',str(src),'status','--short','--untracked-files=all'],stdout=subprocess.PIPE,check=True).stdout
(B/'source-status-after.txt').write_bytes(status)
same=(B/'source-status-before.txt').read_text(encoding='utf-8-sig').replace('\r\n','\n')==status.decode().replace('\r\n','\n')
prior=B.parent/'20261004-long-quality-independent';pm=load(prior.with_name(prior.name+'.manifest.json'))
priorIssues=[e['path'] for e in pm['files'] if not (prior/e['path']).is_file() or sha(prior/e['path'])!=e['sha256'] or (prior/e['path']).stat().st_size!=e['sizeBytes']]
priorInput=load(prior/'independent-inputs.json');r0=load(pkg/'source/r0-input-manifest.json');r3=load(pkg/'source/r3-input-manifest.json')
old={e['path']:e['sha256'] for e in priorInput['inputs']};red={e['path']:e['sha256'] for e in r0['inputs']};new={e['path']:e['sha256'] for e in r3['inputs']}
changed=sorted(k for k in red.keys()&new.keys() if red[k]!=new[k])
stage=load(pkg/'runner/runner-verdict.json');stageOriginal=load(pkg/'runner/archived-verdicts/runner-verdict.2026-10-04T101451Z.json')
oldManifest=B.parents[1]/'market-execution/LONG-QUALITY-GOAL/20261003-092612Z-b4d896a8/FINAL-LOCAL-QUALITY/20261003-092612Z-b4d896a8/manifest.json'
om=load(oldManifest);op=oldManifest.parent
oldPackageIssues=[e['path'] for e in om['files'] if not (op/e['path']).is_file() or sha(op/e['path'])!=e['sha256'] or (op/e['path']).stat().st_size!=e['sizeBytes']]
save('history-protection.json',dict(previousAcceptanceFiles=len(pm['files']),previousAcceptanceIssues=priorIssues,previousGoalFiles=len(om['files']),previousGoalIssues=oldPackageIssues,r0MatchesPreviousAcceptedInputs=old==red,r0ToR3Changed=changed,added=sorted(new.keys()-red.keys()),removed=sorted(red.keys()-new.keys())))
save('exploratory-probe-note.json',dict(probe='runner-output-timeout-result.json',usedAsAcceptanceGate=False,reason='Measured runner invocation finished in 944ms, below the one-second budget; descendant lifecycle was not measured. This experiment does not establish a timeout contract failure and is excluded from all defect counts.'))
summary=dict(schemaVersion=1,generatedHongKong=datetime.now(timezone(timedelta(hours=8))).isoformat(),sourceRoot=str(src),executionRoot=str(snap),head=subprocess.run(['git','-C',str(src),'rev-parse','HEAD'],stdout=subprocess.PIPE,check=True).stdout.decode().strip(),sourceInputCount=len(inputs['inputs']),sourceDriftDuringAcceptance=sourceDrift,executionInputDriftAfterTests=snapshotDrift,sourceStatusUnchanged=same,packageIntegrity=load(B/'package-integrity.json'),strictPackageAudit=load(B/'audit-rework-strict.json')['totals'],strictPackageFailureCodes=sorted({x['code'] for x in load(B/'audit-rework-strict.json')['failures']}),historyAudit=load(B/'audit-history.json')['totals'],trx=trx,csharpExecuted=sum(int(t['counters']['executed']) for t in trx),csharpPassed=sum(int(t['counters']['passed']) for t in trx),csharpSkipped=sum(int(t['counters']['notExecuted']) for t in trx),node=dict(tests=269,passCount=269,failed=0,skipped=0),runnerExistingTests=dict(tests=28,passed=28,failed=0,skipped=0),auditorTests=dict(tests=26,passed=26),importIndependentTests=dict(tests=3,passed=3,failed=0,skipped=0),runnerIndependentCases=dict(total=len(probes['cases']),passed=sum(c['accepted'] for c in probes['cases']),failed=sum(not c['accepted'] for c in probes['cases']),failedIds=[c['id'] for c in probes['cases'] if not c['accepted']]),bisectPassed=19,implementerCascade=dict(stageCount=stageOriginal['stageCount'],stageExitCodes=[dict(id=s['id'],exitCode=s.get('exitCode'),status=s['status']) for s in stageOriginal['stages']],resumeStageCount=stage['stageCount'],resumeStatuses=[s['status'] for s in stage['stages']],note='Source and raw original cascade records inspected; independent execution used separate checks and its own contract suite.'),technicalAcceptance='FAIL',gates={'R0':'PASS','R1':'PASS','R2':'FAIL','R3':'PASS_SELECTED_REGRESSION','R4':'FAIL_STRICT_NEW_SCHEMA'},formalPhaseApproval='NOT_GRANTED',releaseApproval='NOT_GRANTED',findings=['RW-01 executionRoot differs from manifest verified inputRoot yet PASS','RW-02 input changes during stage yet whole-run PASS','RW-03 expected TRX not bound unless caller also lists it in produces; changed TRX REUSED','RW-04 sealed rework package missing strict-new internal manifest.sha256'],limitations=['controlled DOM not real WebView','73 selected C# excludes ImportWithALiveTokenButCancelledMidway','RA01/02 excluded; no restricted counterexamples','real games/native credentials/update handover/cross-platform/CI/release unverified'])
save('acceptance-summary.json',summary)
print(json.dumps(dict(sourceDrift=sourceDrift,snapshotDrift=snapshotDrift,statusUnchanged=same,csharpExecuted=summary['csharpExecuted'],independentContract=summary['runnerIndependentCases'],strictFailure=summary['strictPackageFailureCodes'],priorIssues=priorIssues,oldPackageIssues=oldPackageIssues,changed=changed),ensure_ascii=True))
