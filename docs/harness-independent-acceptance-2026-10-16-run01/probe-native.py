import json, os, pathlib, re, shutil, socket, subprocess, time
from playwright.sync_api import sync_playwright

evidence = pathlib.Path(__file__).resolve().parent
ctx = json.loads((evidence / 'context.json').read_text(encoding='utf-8-sig'))
neutral = pathlib.Path(ctx['neutralRoot'])
work = neutral / '交互与图片 验收'
work.mkdir(exist_ok=False)
app = work / 'Pineglow.Launcher.exe'
shutil.copyfile(neutral / 'publish/Pineglow.Launcher.exe', app)
(work / 'portable.flag').touch()

def port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]

api_port, debug_port = port(), port()
env = {k: v for k, v in os.environ.items() if not k.startswith('PINEGLOW_')}
env.update(PINEGLOW_API_PORT=str(api_port), PINEGLOW_WEBVIEW_DEBUG=str(debug_port),
           DOTNET_BUNDLE_EXTRACT_BASE_DIR=str(work / 'bundle-extract'))
report = {'work': str(work), 'apiPort': api_port, 'debugPort': debug_port,
          'checks': [], 'images': {}, 'errors': [], 'verdict': 'UNKNOWN'}

def check(name, ok, detail):
    report['checks'].append({'name': name, 'ok': bool(ok), 'detail': detail})

def safe_image(row):
    for key in ('src', 'currentSrc'):
        row[key] = re.sub(r'([?&]token=)[^&\s]*', r'\1<REDACTED>', row.get(key) or '')[:250]
    return row

stderr = (evidence / 'native-interaction.stderr.log').open('wb')
stdout = (evidence / 'native-interaction.stdout.log').open('wb')
startup = subprocess.STARTUPINFO()
startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
startup.wShowWindow = 0
proc = subprocess.Popen([str(app)], cwd=work, env=env, stdout=stdout, stderr=stderr, startupinfo=startup)
report['pid'] = proc.pid
try:
    with sync_playwright() as p:
        browser = None
        deadline = time.time() + 40
        while time.time() < deadline:
            if proc.poll() is not None:
                raise RuntimeError('Native process exited: ' + str(proc.returncode))
            try:
                browser = p.chromium.connect_over_cdp('http://127.0.0.1:' + str(debug_port), timeout=1500)
                break
            except Exception:
                time.sleep(.5)
        if browser is None:
            raise RuntimeError('Native WebView CDP did not start')
        page = browser.contexts[0].pages[0]
        page.wait_for_selector('.nav-item[data-nav="home"]', timeout=30000)
        page.wait_for_function('window.SG && SG.api && SG.api._implMode === "live"')
        report['url'] = page.url
        check('real_native_live_api', page.url.startswith('http://127.0.0.1:' + str(api_port)), page.url)

        for view in ('content', 'skins'):
            page.locator('.nav-item[data-nav="' + view + '"]').click()
            root = page.locator('#view-' + view)
            root.wait_for(state='visible')
            page.wait_for_timeout(2500)
            if view == 'content':
                page.wait_for_selector('#view-content .content-card', timeout=45000)
                page.wait_for_timeout(4000)
            rows = root.locator('img').evaluate_all('''els => els.map(i => {
                const r = i.getBoundingClientRect(), cs = getComputedStyle(i);
                return {id:i.id, className:i.className, src:i.getAttribute('src'), currentSrc:i.currentSrc,
                    hidden:i.hidden, display:cs.display, visibility:cs.visibility, loading:i.loading,
                    complete:i.complete, naturalWidth:i.naturalWidth, iconState:i.dataset.iconState || '',
                    width:r.width,height:r.height,top:r.top,bottom:r.bottom,
                    visible:!i.hidden && cs.display!=='none' && cs.visibility!=='hidden' && r.width>0 && r.height>0};
            })''')
            report['images'][view] = [safe_image(row) for row in rows]
            if view == 'content':
                report['contentIconsBeforeScroll'] = {'all': len(rows), 'loaded': sum(r['naturalWidth'] > 0 for r in rows),
                                                      'failed': sum(r['iconState']=='failed' for r in rows)}
                root.locator('.content-card').last.scroll_into_view_if_needed()
                page.wait_for_timeout(5000)
                after = root.locator('img').evaluate_all('els=>els.map(i=>({iconState:i.dataset.iconState||"",naturalWidth:i.naturalWidth,loading:i.loading,hidden:i.hidden,display:getComputedStyle(i).display}))')
                report['contentIconsAfterScroll'] = {'all': len(after), 'loaded': sum(r['naturalWidth'] > 0 for r in after),
                                                     'failed': sum(r['iconState']=='failed' for r in after)}
            page.screenshot(path=str(evidence / ('native-' + view + '-diagnostic.png')))

        page.locator('.nav-item[data-nav="settings"]').click()
        toggle = page.locator('[data-toggle="aiDiag"]')
        toggle.wait_for(state='visible')
        previous = toggle.get_attribute('aria-checked') == 'true'
        toggle.click()
        page.wait_for_function('!document.querySelector("[data-toggle=aiDiag]").disabled')
        current = page.evaluate('SG.api.getState().then(s=>!!s.config.aiDiagnosisEnabled)')
        check('ai_toggle_real_api_save', current == (not previous), {'before': previous, 'saved': current})
        page.reload()
        page.wait_for_selector('.nav-item[data-nav="settings"]')
        page.locator('.nav-item[data-nav="settings"]').click()
        toggle = page.locator('[data-toggle="aiDiag"]')
        toggle.wait_for(state='visible')
        check('ai_toggle_reload_persisted', (toggle.get_attribute('aria-checked') == 'true') == current, {'expected': current})
        toggle.click()
        page.wait_for_function('!document.querySelector("[data-toggle=aiDiag]").disabled')
        restored = page.evaluate('SG.api.getState().then(s=>!!s.config.aiDiagnosisEnabled)')
        check('ai_toggle_restore', restored == previous, {'restored': restored})
        page.locator('#btnSourceChange').click()
        page.wait_for_selector('.source-option, #scRetry', timeout=45000)
        options = page.locator('.source-option')
        source_keys = options.evaluate_all('els=>els.map(e=>({key:e.dataset.source,selected:!!e.querySelector(".source-check")}))')
        check('source_picker_native_no_reference_error', len(source_keys) > 0, source_keys)
        report['sourceOptions'] = source_keys
        page.screenshot(path=str(evidence / 'native-source-picker.png'))
        if page.locator('#scClose').count():
            page.locator('#scClose').click()
        report['verdict'] = 'PASS' if all(c['ok'] for c in report['checks']) else 'FAIL'
except Exception as e:
    report['errors'].append(str(e))
    report['verdict'] = 'FAIL'
finally:
    subprocess.run(['taskkill.exe', '/PID', str(proc.pid), '/T', '/F'], capture_output=True)
    stdout.close()
    stderr.close()
    (evidence / 'native-interaction.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'verdict': report['verdict'], 'checks': report['checks'], 'errors': report['errors'],
                      'contentIconsBeforeScroll': report.get('contentIconsBeforeScroll'),
                      'contentIconsAfterScroll': report.get('contentIconsAfterScroll')}, ensure_ascii=False, indent=2))
