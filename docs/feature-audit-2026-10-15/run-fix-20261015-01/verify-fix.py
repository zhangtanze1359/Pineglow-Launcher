"""Actual built UI and live JS adapter with isolated API fixtures; no user writes."""
import functools
import json
import sys
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse
from playwright.sync_api import sync_playwright, expect

sys.stdout.reconfigure(encoding='utf-8')
out = Path(__file__).resolve().parent
repo = out.parents[2]
ui = repo / 'dev/PineglowLauncher/bin/Release/net10.0/UI'
class QuietHandler(SimpleHTTPRequestHandler):
    def log_message(self, *args):
        pass
server = ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(QuietHandler, directory=str(ui)))
threading.Thread(target=server.serve_forever, daemon=True).start()
report = {'sourceRoot': str(repo), 'executionRoot': str(ui), 'layer': 'Real Edge + Release build UI + intercepted API fixtures', 'checks': [], 'smoke': [], 'errors': [], 'requests': []}
config = {'selectedInstanceId': 'b', 'maxMemoryMb': 4096, 'useAutoMemory': True, 'downloadSource': 'bmclapi', 'useMirror': True,
          'proxyEnabled': True, 'proxyHost': '127.0.0.1', 'proxyPort': 7890, 'proxyUsername': 'fixture-user', 'proxyPasswordConfigured': True,
          'newsFakeIpCompat': True, 'minimizeOnLaunch': True, 'autoCloseOnExit': True, 'aiDiagnosisEnabled': False, 'aiProviders': []}
instances = [{'id': 'a', 'name': '验证 Vanilla', 'minecraftVersion': '1.21.1', 'loaderType': 'Vanilla'}, {'id': 'b', 'name': '验证 Fabric', 'minecraftVersion': '1.21.1', 'loaderType': 'Fabric'}]
worlds = {'worlds': [{'instanceId': 'b', 'folderName': '验证存档', 'name': '验证存档'}]}
servers = {'servers': [{'id': 's1', 'name': '验证服务器', 'host': 'example.invalid', 'port': 25565}]}
fixture = {'failed': set(), 'empty': set(), 'hold': set(), 'pending': {}, 'patches': []}
def sources():
    return {'selected': config['downloadSource'], 'sources': [] if 'sources' in fixture['empty'] else [{'key': 'official', 'available': True, 'latencyMs': 90}, {'key': 'bmclapi', 'available': True, 'latencyMs': 20}]}
def record(name):
    report['checks'].append({'name': name, 'status': 'PASS'})

with sync_playwright() as p:
    browser = p.chromium.launch(channel='msedge', headless=True)
    page = browser.new_page(viewport={'width': 1280, 'height': 800}, locale='zh-CN')
    page.on('pageerror', lambda error: report['errors'].append(str(error)))
    def api(route):
        request = route.request
        path = urlparse(request.url).path
        report['requests'].append({'path': path, 'method': request.method})
        if path in fixture['hold']:
            fixture['pending'][path] = route
            return
        if path in fixture['failed']:
            route.fulfill(status=503, json={'error': '<img src=x onerror="window.bad=true"> fixture unavailable'})
            return
        if path == '/api/state': body = {'config': config, 'instances': instances, 'accounts': [], 'java': []}
        elif path == '/api/instances': body = {'instances': instances, 'selectedInstanceId': config['selectedInstanceId']}
        elif path == '/api/config':
            patch = request.post_data_json
            fixture['patches'].append(patch)
            config.update(patch)
            body = {'ok': True}
        elif path == '/api/sources': body = sources()
        elif path == '/api/sources/select':
            config['downloadSource'] = request.post_data_json['key']
            config['useMirror'] = config['downloadSource'] != 'official'
            body = {'ok': True, 'selected': config['downloadSource'], 'useMirror': config['useMirror']}
        elif path == '/api/worlds': body = {'worlds': []} if 'worlds' in fixture['empty'] else worlds
        elif path == '/api/servers': body = {'servers': []} if 'servers' in fixture['empty'] else servers
        elif path == '/api/versions': body = {'local': ['1.21.1']}
        elif path == '/api/versions/remote': body = {'versions': [{'id': '1.21.1', 'type': 'release'}]}
        elif path == '/api/news': body = {'items': [], 'meta': {'state': 'empty', 'isStale': False}}
        elif path == '/api/update': body = {'state': 'idle', 'currentVersion': '1.0.0'}
        elif path == '/api/update/check': body = {'enabled': True, 'updateAvailable': False, 'currentVersion': '1.0.0'}
        else: body = {'ok': True, 'accounts': [], 'tasks': [], 'installations': [], 'sessions': [], 'perInstance': []}
        route.fulfill(json=body)
    page.route('**/api/**', api)
    def ready(nav):
        page.wait_for_function('(n) => window.SG && SG.__test__ && SG.__test__.getCurrentView() === n', arg=nav)
        expect(page.locator('#view-' + nav)).to_be_visible()
    def go(nav):
        page.locator(f'[data-nav="{nav}"]').click()
        ready(nav)
    def fresh(nav='home'):
        fixture['navigationGeneration'] = fixture.get('navigationGeneration', 0) + 1
        page.goto(f'http://127.0.0.1:{server.server_port}/?fixture={fixture["navigationGeneration"]}#{nav}', wait_until='networkidle')
        ready(nav)
    def close(id):
        page.locator('#' + id).click()
        expect(page.locator('#modalHost')).to_be_hidden()
    def count(path):
        return sum(r['path'] == path for r in report['requests'])
    try:
        fresh()
        expect(page.locator('.home-game-title')).to_have_text('验证 Fabric')
        page.locator('#btnPlay').click()
        page.wait_for_function('true')
        page.wait_for_timeout(100)
        assert count('/api/instances/b/launch') == 1 and count('/api/instances/a/launch') == 0
        record('FA01_SavedInstanceLaunchTarget')

        go('settings')
        page.locator('#btnProxy').click()
        expect(page.locator('#proxyToggle')).to_have_attribute('aria-checked', 'true')
        expect(page.locator('#pHost')).to_have_value('127.0.0.1')
        expect(page.locator('#pPort')).to_have_value('7890')
        expect(page.locator('#pPass')).to_have_value('')
        page.locator('#pOk').click()
        page.wait_for_function('true'); page.wait_for_timeout(100)
        assert fixture['patches'][-1] == {'proxyEnabled': True, 'proxyHost': '127.0.0.1', 'proxyPort': 7890, 'proxyUsername': 'fixture-user', 'proxyPassword': ''}
        expect(page.locator('#modalHost')).to_be_hidden()
        page.locator('#btnQuick').click()
        expect(page.locator('#qMin')).to_have_attribute('aria-checked', 'true')
        expect(page.locator('#qClose')).to_have_attribute('aria-checked', 'true')
        close('qCancel')
        expect(page.locator('[data-toggle="newsFakeIp"]')).to_have_attribute('aria-checked', 'true')
        fresh('settings')
        page.locator('#btnProxy').click(); expect(page.locator('#pHost')).to_have_value('127.0.0.1'); close('pCancel')
        record('FA02_ProxyRoundTripPreservesValuesAndSecrets')

        page.locator('#btnSourceChange').click()
        expect(page.locator('.source-option')).to_have_count(2)
        expect(page.locator('[data-source="bmclapi"] .source-check')).to_have_count(1)
        bounds = page.locator('.source-option').evaluate_all('(rows)=>rows.map(e=>{const r=e.getBoundingClientRect();return {top:r.top,bottom:r.bottom,width:r.width}})')
        assert bounds[0]['width'] > 300 and bounds[1]['top'] >= bounds[0]['bottom']
        page.screenshot(path=str(out / 'source-picker-green.png'), animations='disabled')
        page.locator('[data-source="official"]').click()
        expect(page.locator('#modalHost')).to_be_hidden()
        assert config['downloadSource'] == 'official'
        fresh('settings'); page.locator('#btnSourceChange').click()
        expect(page.locator('[data-source="official"] .source-check')).to_have_count(1)
        close('scClose')
        fixture['failed'].add('/api/sources'); page.locator('#btnSourceChange').click()
        expect(page.locator('#scRetry')).to_be_visible()
        fixture['failed'].clear(); page.locator('#scRetry').click()
        expect(page.locator('.source-option')).to_have_count(2); close('scClose')
        fixture['empty'].add('sources'); page.locator('#btnSourceChange').click()
        expect(page.locator('#scRetry')).to_be_visible()
        fixture['empty'].clear(); page.locator('#scRetry').click()
        expect(page.locator('.source-option')).to_have_count(2); close('scClose')
        record('FA03_SourceListSelectionPersistenceAndRetry')

        fixture['hold'].add('/api/sources'); page.locator('#btnSourceChange').click()
        expect(page.locator('#scClose')).to_be_visible()
        close('scClose'); page.locator('#btnQuickOpen').click()
        expect(page.locator('#commandQuery')).to_be_visible()
        fixture['pending'].pop('/api/sources').fulfill(json=sources()); fixture['hold'].clear()
        page.wait_for_timeout(100); expect(page.locator('#commandQuery')).to_be_visible()
        page.locator('#commandQuery').press('Escape'); expect(page.locator('#modalHost')).to_be_hidden()
        record('FA03_LateSourceResponsePreservesBusinessDialog')

        fixture['hold'].add('/api/sources/select'); page.locator('#btnSourceChange').click()
        expect(page.locator('.source-option')).to_have_count(2)
        page.locator('[data-source="bmclapi"]').click()
        expect(page.locator('[data-source="bmclapi"]')).to_be_disabled()
        close('scClose'); page.locator('#btnQuickOpen').click()
        before_generation = page.evaluate('SG.__test__.getNavSeq()')
        pending = fixture['pending'].pop('/api/sources/select')
        config['downloadSource'] = pending.request.post_data_json['key']; config['useMirror'] = True
        pending.fulfill(json={'ok': True, 'selected': config['downloadSource'], 'useMirror': True}); fixture['hold'].clear()
        page.wait_for_timeout(100)
        expect(page.locator('#commandQuery')).to_be_visible()
        assert page.evaluate('SG.__test__.getNavSeq()') == before_generation
        expect(page.locator('#btnSourceChange')).to_contain_text('BMCLAPI')
        page.locator('#commandQuery').press('Escape'); expect(page.locator('#modalHost')).to_be_hidden()
        record('FA03_LateSourceSavePreservesNavigationAndDialog')

        toggle = page.locator('[data-toggle="aiDiag"]')
        toggle.click(); expect(toggle).to_have_attribute('aria-checked', 'true'); expect(toggle).to_be_enabled()
        assert config['aiDiagnosisEnabled'] is True
        page.locator('#btnLangEn').click(); ready('settings')
        expect(toggle).to_have_attribute('aria-checked', 'true')
        fixture['failed'].add('/api/config'); toggle.click()
        expect(toggle).to_be_enabled(); expect(toggle).to_have_attribute('aria-checked', 'true')
        expect(page.locator('#toast')).to_contain_text('fixture unavailable')
        fixture['failed'].clear(); toggle.click()
        expect(toggle).to_have_attribute('aria-checked', 'false'); expect(toggle).to_be_enabled()
        assert config['aiDiagnosisEnabled'] is False
        record('FA04_AiToggleSuccessFailureRollbackAndRetry')

        before = count('/api/config'); fixture['hold'].add('/api/config')
        page.evaluate('const b=document.querySelector("[data-toggle=aiDiag]");b.click();b.click()')
        expect(toggle).to_be_disabled()
        page.locator('#btnLangZh').click(); ready('settings'); expect(toggle).to_be_disabled()
        page.evaluate('document.querySelector("[data-toggle=aiDiag]").click()')
        page.wait_for_timeout(100); assert count('/api/config') == before + 1
        pending = fixture['pending'].pop('/api/config'); config.update(pending.request.post_data_json)
        pending.fulfill(json={'ok': True}); fixture['hold'].clear()
        expect(toggle).to_be_enabled(); expect(toggle).to_have_attribute('aria-checked', 'true')
        record('FA04_PendingSaveDeduplicatesAcrossRerender')

        for nav in ['worlds', 'servers']:
            fixture['failed'].add('/api/' + nav); go(nav)
            retry = page.locator(f'[data-list-retry="{nav}"]')
            expect(retry).to_be_visible(); expect(page.locator('#view-' + nav + ' .empty-state')).to_have_count(0)
            assert page.locator('#view-' + nav + ' .tech-details').get_attribute('open') is None
            expect(page.locator('#view-' + nav + ' img')).to_have_count(0)
            page.screenshot(path=str(out / (nav + '-failure-green.png')), animations='disabled')
            before = count('/api/' + nav)
            page.evaluate('(n)=>{const b=document.querySelector("[data-list-retry="+n+"]");b.click();b.click()}', nav)
            expect(retry).to_be_visible(); page.wait_for_timeout(100)
            assert count('/api/' + nav) == before + 1
            fixture['failed'].clear(); retry.click()
            expect(page.locator('.world-card' if nav == 'worlds' else '.server-card')).to_be_visible()
            fixture['empty'].add(nav); fresh(nav)
            expect(page.locator('#view-' + nav + ' .empty-state')).to_be_visible(); expect(retry).to_have_count(0)
            fixture['empty'].clear(); fresh(nav)
            record('FA05_' + nav + '_FailureRetryRecoveryAndTrueEmpty')

        fixture['failed'].add('/api/worlds'); fresh('worlds'); fixture['failed'].clear(); fixture['hold'].add('/api/worlds')
        page.locator('[data-list-retry]').click(); go('servers')
        fixture['pending'].pop('/api/worlds').fulfill(json=worlds); fixture['hold'].clear()
        page.wait_for_timeout(100); ready('servers')
        record('FA05_LateRetryDoesNotNavigateBack')

        for width, height in [(1280, 800), (960, 640)]:
            page.set_viewport_size({'width': width, 'height': height})
            for nav in ['home', 'installations', 'worlds', 'servers', 'playtime', 'content', 'skins', 'downloads', 'news', 'settings']:
                go(nav)
                overflow = page.locator('#contentPanel').evaluate('(e)=>e.scrollWidth-e.clientWidth')
                assert overflow <= 1, (nav, width, overflow)
                assert len(page.locator('#view-' + nav).inner_text().strip()) > 5
                report['smoke'].append({'view': nav, 'width': width, 'overflow': overflow, 'status': 'PASS'})
        assert not report['errors'], report['errors']
        record('TenViewsTwoSizesNoUncaughtJavaScriptErrors')
        report['status'] = 'PASS'
    finally:
        (out / 'browser-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        browser.close(); server.shutdown(); server.server_close()
print(json.dumps({k: v for k, v in report.items() if k != 'requests'}, ensure_ascii=False, indent=2))
