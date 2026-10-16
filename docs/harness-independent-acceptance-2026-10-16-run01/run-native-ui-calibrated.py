import json,pathlib,subprocess
root=pathlib.Path('docs/harness-independent-acceptance-2026-10-16-run01').resolve()
ctx=json.loads((root/'context.json').read_text(encoding='utf-8-sig'))
args=['node',str(root/'verify-ui-pages-visible-images.cjs'),'--app',str(pathlib.Path(ctx['neutralRoot'])/'publish/Pineglow.Launcher.exe'),'--out',str(root/'ui-shots-calibrated'),'--json',str(root/'native-ui-calibrated.json'),'--keep','--timeout','40','--settle-timeout','45']
(root/'native-ui-calibrated-command.json').write_text(json.dumps(args,ensure_ascii=False,indent=2),encoding='utf-8')
with (root/'native-ui-calibrated.log').open('wb') as log:
    r=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,timeout=240)
(root/'native-ui-calibrated-exit.json').write_text(json.dumps({'exitCode':r.returncode}),encoding='utf-8')
print('calibrated UI exit='+str(r.returncode))
