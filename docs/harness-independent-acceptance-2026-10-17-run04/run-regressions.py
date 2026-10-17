import concurrent.futures,json,pathlib,subprocess
root=pathlib.Path(__file__).resolve().parent
source=root.parent.parent
commands={
 'node-tests':['node','scripts/run-inventory-tests.cjs','--json',str(root/'inventory.json')],
 'visibility':['python','scripts/ha01-image-visibility-behavior.py','scripts/verify-ui-pages-live.cjs',str(root/'visibility.json')],
 'cdp-helper':['python','scripts/ha01-cdp-lazy-scroll-behavior.py','scripts/verify-ui-pages-live.cjs',str(root/'cdp-helper.json')],
 'closure':['node','scripts/check-ui-resource-closure.cjs','--json',str(root/'closure.json')],
 'official-ui':['python',str(root/'run-official-ui.py')]
}
(root/'commands.json').write_text(json.dumps(commands,ensure_ascii=False,indent=2),encoding='utf-8')
def run(item):
 name,args=item
 with (root/(name+'.log')).open('wb') as f:
  try: result=subprocess.run(args,cwd=source,stdout=f,stderr=subprocess.STDOUT,timeout=300).returncode
  except subprocess.TimeoutExpired: result='TIMEOUT'
 print(name,result,flush=True)
 return name,result
with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:
 results=dict(pool.map(run,commands.items()))
(root/'regression-exits.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
