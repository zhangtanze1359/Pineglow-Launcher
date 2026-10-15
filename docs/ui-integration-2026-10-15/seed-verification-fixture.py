from pathlib import Path
import json,zipfile,datetime,shutil
root=Path(__file__).resolve().parent
runtime=root/'runtime'
p=runtime/'data/config.json'
if not (root/'runtime-config-before-fixture.json').exists():shutil.copyfile(p,root/'runtime-config-before-fixture.json')
config=json.loads(p.read_text(encoding='utf-8-sig'))
now=datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=8)))
instances=[]
for i,(name,loader,minutes) in enumerate([('验收实例 · Fabric','Fabric',90),('验收实例 · Vanilla','Vanilla',45)]):
 game=runtime/'fixture-game'/str(i)
 (game/'mods').mkdir(parents=True,exist_ok=True)
 if i==0:
  for mid in ['verify_alpha','verify_beta']:
   disabled=game/'mods'/f'{mid}.jar.disabled'
   if disabled.exists():
    assert disabled.resolve().is_relative_to(runtime.resolve())
    disabled.unlink() # This script owns these exact synthetic files.
   with zipfile.ZipFile(game/'mods'/f'{mid}.jar','w') as jar:
    jar.writestr('fabric.mod.json',json.dumps({'schemaVersion':1,'id':mid,'version':'1.0.0','name':mid,'environment':'*','depends':{'fabricloader':'>=0.1.0'}}))
 sessions=[]
 for day in [0,1,2,5]:
  start=(now-datetime.timedelta(days=day)).replace(hour=10,minute=0,second=0,microsecond=0)
  sessions.append({'StartedAt':start.isoformat(),'EndedAt':(start+datetime.timedelta(minutes=minutes+day*3)).isoformat()})
 instances.append({'Id':'ui-verify-'+str(i),'Name':name,'MinecraftVersion':'1.21.5','GameDirectory':str(game),'LoaderType':loader,'MaxMemoryMb':4096,'TotalPlaySeconds':sum((minutes+d*3)*60 for d in [0,1,2,5]),'PlayCount':4,'PlaySessions':sessions})
config['Instances']=instances
config['SelectedInstanceId']=instances[0]['Id']
config['GameDirectory']=str(runtime/'fixture-game'/'0')
p.write_text(json.dumps(config,ensure_ascii=False,indent=2),encoding='utf-8')
print('Synthetic sessions and mod files written ONLY into this run-owned portable fixture.')
