from pathlib import Path
import json,zipfile,datetime,sys
run=sys.argv[1] if len(sys.argv)>1 else 'native'
assert run in ('native','native-final')
d=Path(__file__).resolve().parent/run
instances=[]
for i,(name,loader) in enumerate([('布局复验 · Fabric','Fabric'),('布局复验 · 原版','Vanilla')]):
 game=d/'fixture-game'/str(i);(game/'mods').mkdir(parents=True,exist_ok=True)
 if i==0:
  for mid in ['layout_alpha','layout_beta']:
   with zipfile.ZipFile(game/'mods'/f'{mid}.jar','w') as jar:jar.writestr('fabric.mod.json',json.dumps({'schemaVersion':1,'id':mid,'version':'1.0.0','name':mid,'environment':'*'}))
 sessions=[]
 for day in [0,1,2,5]:
  start=(datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=8)))-datetime.timedelta(days=day)).replace(hour=10,minute=0,second=0,microsecond=0)
  sessions.append({'StartedAt':start.isoformat(),'EndedAt':(start+datetime.timedelta(hours=1)).isoformat()})
 instances.append({'Id':'layout-native-'+str(i),'Name':name,'MinecraftVersion':'1.21.5','GameDirectory':str(game),'LoaderType':loader,'MaxMemoryMb':4096,'TotalPlaySeconds':14400,'PlayCount':4,'PlaySessions':sessions})
config={'Instances':instances,'SelectedInstanceId':instances[0]['Id'],'Accounts':[],'MinimizeOnLaunch':False,'AutoCloseOnExit':False,'GameDirectory':instances[0]['GameDirectory'],'UseAutoMemory':True,'MaxMemoryMb':4096,'PlayerName':'LayoutFixture','AccountMode':'offline'}
(d/'data/config.json').write_text(json.dumps(config,ensure_ascii=False,indent=2),encoding='utf-8')
print('Seeded isolated, synthetic game directories; no user game data read or modified.')
