import json,pathlib,subprocess,hashlib
root=pathlib.Path('docs/harness-independent-acceptance-2026-10-16-run02').resolve()
ctx=json.loads((root/'context.json').read_text(encoding='utf-8-sig'))
old=json.loads(pathlib.Path('docs/release-prep-verify-2026-10-16/ui-live-fixed.json').read_text(encoding='utf-8-sig'))
app=pathlib.Path(old['app'])
meta={'app':str(app),'sha256':hashlib.sha256(app.read_bytes()).hexdigest(),'size':app.stat().st_size}
(root/'candidate.json').write_text(json.dumps(meta,indent=2),encoding='utf-8')
args=['node',str(pathlib.Path(ctx['sourceRoot'])/'scripts/verify-ui-pages-live.cjs'),'--app',str(app),'--out',str(root/'ui-shots'),'--json',str(root/'official-ui.json'),'--keep','--timeout','40','--settle-timeout','45']
(root/'official-ui-command.json').write_text(json.dumps(args,ensure_ascii=False,indent=2),encoding='utf-8')
with (root/'official-ui.log').open('wb') as f:
    r=subprocess.run(args,stdout=f,stderr=subprocess.STDOUT,timeout=240)
(root/'official-ui-exit.json').write_text(json.dumps({'exit':r.returncode}),encoding='utf-8')
print('Official UI exit='+str(r.returncode))
