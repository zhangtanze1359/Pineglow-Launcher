import pathlib,json,hashlib
root=pathlib.Path(r'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution')
runs=[]; failures=[]; self_entries=[];total=0
for mf in sorted(list(root.glob('*/*/manifest.json')) + list(root.glob('*/manifest.json'))):
    try: data=json.loads(mf.read_text(encoding='utf-8-sig'))
    except Exception as e: failures.append({'manifest':str(mf),'reason':str(e)});continue
    listed=set();checked=0
    for f in data.get('files',[]):
        name=f.get('path','');p=mf.parent/name.replace('\\','/')
        if not p.resolve().is_relative_to(mf.parent.resolve()):failures.append({'manifest':str(mf),'path':name,'reason':'path escapes run'});continue
        listed.add(name.replace('\\','/'));checked+=1;total+=1
        if not p.is_file():failures.append({'manifest':str(mf),'path':name,'reason':'missing'});continue
        actual=hashlib.sha256(p.read_bytes()).hexdigest().upper(); size=p.stat().st_size
        expected_size=f.get('sizeBytes',f.get('bytes'))
        ok=actual==f.get('sha256','').upper() and (expected_size is None or size==expected_size)
        if name=='manifest.json':self_entries.append({'manifest':str(mf),'matches':ok,'size':size,'expectedSize':expected_size})
        if not ok:failures.append({'manifest':str(mf),'path':name,'hashMatches':actual==f.get('sha256','').upper(),'size':size,'expectedSize':expected_size})
    unlisted=[str(p.relative_to(mf.parent)).replace('\\','/') for p in mf.parent.rglob('*') if p.is_file() and p.name!='manifest.json' and str(p.relative_to(mf.parent)).replace('\\','/') not in listed]
    if unlisted:failures.append({'manifest':str(mf),'reason':'unlisted files','paths':unlisted})
    if data.get('runId') and data['runId']!=mf.parent.name:failures.append({'manifest':str(mf),'reason':'runId mismatch'})
    runs.append({'manifest':str(mf),'filesChecked':checked})
out={'manifestCount':len(runs),'fileEntries':total,'selfEntries':self_entries,'failures':failures,'runs':runs}
pathlib.Path(__file__).with_name('independent-evidence-audit.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:out[k] for k in ['manifestCount','fileEntries','selfEntries','failures']},ensure_ascii=False))
