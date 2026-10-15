import hashlib,json,pathlib,subprocess,xml.etree.ElementTree as ET
from datetime import datetime,timezone
BASE=pathlib.Path(__file__).parent
ROOT=pathlib.Path('D:/松光启动器')
RUN=sorted(BASE.glob('boundary-run-*'))[-1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def write(p,data):p.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
summaries=[]
for p in [*sorted((BASE/'test-results').glob('*.trx')),*sorted((RUN/'test-results').glob('*.trx'))]:
    tree=ET.parse(p);c=tree.find('.//t:Counters',ns);times=tree.find('t:Times',ns)
    failed=[]
    for r in tree.findall('.//t:UnitTestResult',ns):
        if r.get('outcome')=='Failed':failed.append(dict(name=r.get('testName'),message=r.findtext('.//t:Message',default='',namespaces=ns),stdout=r.findtext('.//t:StdOut',default='',namespaces=ns)))
    summaries.append(dict(file=str(p.relative_to(BASE)),counters=c.attrib if c is not None else None,times=times.attrib if times is not None else None,failures=failed))
write(BASE/'test-summary.json',summaries)
snapshot=json.loads((BASE/'snapshot-manifest.json').read_text(encoding='utf-8'))
config=json.loads((BASE/'config-before.json').read_text(encoding='utf-8'))
cfgroot=pathlib.Path.home()/'AppData/Roaming/MCMiniLauncher'
source_hash_drift=[item['path'] for item in snapshot['files'] if 'source' not in item and sha(ROOT/item['path'])!=item['sha256']]
write(BASE/'source-final-protection.json',dict(utc=datetime.now(timezone.utc).isoformat(),sourceRoot=str(ROOT),sourceHashDrift=source_hash_drift,
    statusUnchanged=(BASE/'source-status-before.txt').read_bytes()==subprocess.check_output(['git','-C',str(ROOT),'status','--short','--untracked-files=all']),
    configUnchanged=config=={name:sha(cfgroot/name) if (cfgroot/name).is_file() else None for name in config},configAfter={name:sha(cfgroot/name) if (cfgroot/name).is_file() else None for name in config}))
files=[]
for p in sorted(BASE.rglob('*')):
    if p.is_file() and p not in [BASE/'manifest.json',BASE/'manifest.sha256']:
        files.append(dict(path=str(p.relative_to(BASE)).replace('\\','/'),sizeBytes=p.stat().st_size,sha256=sha(p)))
write(BASE/'manifest.json',dict(utc=datetime.now(timezone.utc).isoformat(),purpose='Independent acceptance: NOT_PASSED; source unchanged',files=files))
(BASE/'manifest.sha256').write_text(sha(BASE/'manifest.json')+'  manifest.json\n',encoding='utf-8')
print(json.dumps(dict(sourceHashDrift=source_hash_drift,evidenceFiles=len(files),latestBoundaryRun=str(RUN),tests=[dict(file=x['file'],counters=x['counters']) for x in summaries]),ensure_ascii=False))
