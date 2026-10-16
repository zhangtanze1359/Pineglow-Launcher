import json, pathlib, subprocess
root=pathlib.Path('docs/harness-independent-acceptance-2026-10-16-run01')
ctx=json.loads((root/'context.json').read_text(encoding='utf-8-sig'))
old=json.loads(pathlib.Path('docs/release-prep-verify-2026-10-16/standalone-cold-start.json').read_text(encoding='utf-8-sig'))
args=['node',str(pathlib.Path(ctx['executionRoot'])/'scripts/verify-ui-pages-live.cjs'),'--app',old['exePath'],'--out',str(pathlib.Path(ctx['evidenceRoot'])/'ui-shots'),'--json',str(pathlib.Path(ctx['evidenceRoot'])/'native-ui.json'),'--keep','--timeout','40','--settle-timeout','45']
(root/'native-ui-command.json').write_text(json.dumps(args,ensure_ascii=False,indent=2),encoding='utf-8')
with (root/'native-ui.log').open('wb') as log:
    result=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,timeout=240)
(root/'native-ui-exit.json').write_text(json.dumps({'exitCode':result.returncode}),encoding='utf-8')
print('native-ui exit='+str(result.returncode))
print((root/'native-ui.log').read_text(encoding='utf-8',errors='replace')[-3500:])
