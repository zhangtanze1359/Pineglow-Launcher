import json
import sys
from pathlib import Path
from urllib.parse import urlparse
from playwright.sync_api import sync_playwright, expect

root = Path(__file__).resolve().parent
report = {'layer': 'Chromium + built UI + intercepted local API fixtures', 'checks': [], 'errors': []}

def record(name):
    report['checks'].append({'name': name, 'status': 'PASS'})

with sync_playwright() as p:
    browser = p.chromium.launch(channel='msedge', headless=True)
    context = browser.new_context(viewport={'width': 1280, 'height': 800}, locale='zh-CN')
    page = context.new_page()
    page.on('pageerror', lambda e: report['errors'].append(str(e)))
    page.clock.install()
    fixture = {'version': '1.1.0', 'error': False, 'state': 'idle', 'checks': 0, 'download': 0, 'apply': 0}
    notes = '【本机验证数据】新增自动更新提醒\n修复旧主源遮蔽新版。\n<img src=x onerror="window.updateXss=true">\n' + '较长的版本介绍用于检查换行和滚动。' * 35

    def api(route):
        path = urlparse(route.request.url).path
        if path == '/api/state':
            data = {'config': {}, 'instances': [], 'accounts': [], 'java': []}
        elif path == '/api/update/check':
            fixture['checks'] += 1
            data = {'enabled': True, 'currentVersion': '1.0.0', 'latestVersion': fixture['version'], 'updateAvailable': True, 'notes': notes}
            if fixture['error']:
                data = {'enabled': True, 'currentVersion': '1.0.0', 'error': 'fixture offline'}
        elif path == '/api/update/download':
            fixture['download'] += 1
            fixture['state'] = 'completed'
            data = {'ok': True, 'state': 'downloading'}
        elif path == '/api/update/apply':
            fixture['apply'] += 1
            data = {'ok': True, 'message': '本机验证：未替换任何程序'}
        elif path == '/api/update':
            data = {'state': fixture['state'], 'currentVersion': '1.0.0', 'version': fixture['version'], 'progress': 1, 'trustNote': '验证数据'}
        elif path == '/api/news':
            data = {'items': [], 'meta': {'state': 'empty', 'isStale': False}}
        else:
            data = {'accounts': [], 'tasks': [], 'stats': {}, 'installations': [], 'ok': True}
        route.fulfill(json=data)

    context.route('**/api/**', api)
    try:
        page.goto(sys.argv[1], wait_until='networkidle')
        page.wait_for_function('window.SG && SG.__test__ && SG.__test__.getCurrentView()')
        assert page.evaluate('SG.apiMode') == 'live'
        assert fixture['checks'] == 0
        page.clock.run_for(3100)
        expect(page.locator('#updateLater')).to_be_visible()
        expect(page.locator('.update-release-notes')).to_contain_text('本机验证数据')
        assert page.locator('.update-release-notes img').count() == 0
        assert page.evaluate('window.updateXss === undefined')
        page.clock.run_for(300)
        page.screenshot(path=str(root / 'update-notice-1280.png'), animations='disabled')
        record('StartupUsesLiveCheckAndShowsEscapedVersionIntroduction')
        assert fixture['download'] == 0 and fixture['apply'] == 0
        page.locator('#updateLater').click()
        page.clock.run_for(250)
        expect(page.locator('#updateNotice')).to_be_visible()
        page.clock.run_for(600000)
        expect(page.locator('#modalHost')).to_be_hidden()
        assert fixture['checks'] >= 2
        record('LaterKeepsBannerAndPeriodicChecksDoNotRepeatSameVersion')

        page.locator('#btnQuickOpen').click()
        fixture['version'] = '1.2.0'
        page.clock.run_for(600000)
        expect(page.locator('#commandQuery')).to_be_visible()
        page.locator('#commandQuery').press('Escape')
        page.clock.run_for(5250)
        expect(page.locator('#modalHost .modal-title')).to_contain_text('1.2.0')
        record('NewVersionDefersUntilBusinessDialogCloses')
        page.set_viewport_size({'width': 960, 'height': 640})
        assert page.locator('.modal').evaluate('(e) => e.scrollWidth <= e.clientWidth + 1')
        page.clock.run_for(300)
        page.screenshot(path=str(root / 'update-notice-960.png'), animations='disabled')
        record('LongIntroductionFits960ViewportAndScrolls')

        page.locator('#updateOpenSettings').click()
        page.wait_for_function('SG.__test__.getCurrentView() === "settings"')
        expect(page.locator('#btnDownloadUpdate')).to_be_visible()
        expect(page.locator('#updateStatus .update-notes')).to_contain_text('本机验证数据')
        assert fixture['download'] == 0 and fixture['apply'] == 0
        page.clock.run_for(250)
        page.locator('#btnDownloadUpdate').click()
        page.clock.run_for(2500)
        expect(page.locator('#btnApplyUpdate')).to_be_visible()
        assert fixture['download'] == 1 and fixture['apply'] == 0
        page.locator('#btnApplyUpdate').click()
        expect(page.locator('#toast')).to_contain_text('本机验证')
        assert fixture['apply'] == 1
        record('IntroductionToSettingsToDownloadToExplicitRestartContract')

        page.locator('#btnLangEn').click()
        expect(page.locator('#updateNotice')).to_contain_text('Version 1.2.0')
        page.locator('#updateNotice').click()
        expect(page.locator('.update-release-label').last).to_have_text('What’s new')
        record('EnglishBannerAndIntroductionLabels')
        page.locator('#updateLater').click()
        page.clock.run_for(250)
        before = fixture['checks']
        page.clock.run_for(61000)
        page.evaluate('window.dispatchEvent(new Event("focus"))')
        page.wait_for_function('true')
        page.wait_for_timeout(50)
        assert fixture['checks'] == before + 1
        page.evaluate('window.dispatchEvent(new Event("focus"))')
        page.wait_for_timeout(50)
        assert fixture['checks'] == before + 1
        record('ForegroundCheckIsThrottled')

        fixture['error'] = True
        page.reload(wait_until='networkidle')
        page.clock.run_for(3100)
        expect(page.locator('#modalHost')).to_be_hidden()
        fixture['error'] = False
        page.clock.run_for(600000)
        expect(page.locator('#updateLater')).to_be_visible()
        record('AutomaticFailureIsQuietAndNextCheckRecovers')
        assert not report['errors'], report['errors']
        record('NoUncaughtJavaScriptErrors')
        report['status'] = 'PASS'
    finally:
        (root / 'browser-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        browser.close()
print(json.dumps(report, ensure_ascii=False, indent=2))
