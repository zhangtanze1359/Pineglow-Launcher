"""Read-only product audit: real UI/API adapter, intercepted synthetic backend replies."""
import copy
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
repo = out.parent.parent
class QuietHandler(SimpleHTTPRequestHandler):
    def log_message(self, *args):
        pass
server = ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(QuietHandler, directory=str(repo / 'ui')))
threading.Thread(target=server.serve_forever, daemon=True).start()
report = {'sourceRoot': str(repo), 'head': '1356efa70cfd91f6289ee6e0dc6af5160b444ab3', 'layer': 'Real Edge + source UI + controlled API fixtures; no real user writes', 'findings': [], 'smoke': [], 'errors': [], 'requests': []}
base_config = {'selectedInstanceId': 'inst-b', 'selectedVersion': '1.21.1', 'maxMemoryMb': 4096, 'useAutoMemory': True, 'downloadSource': 'bmclapi', 'useMirror': True, 'proxyEnabled': True, 'proxyHost': '127.0.0.1', 'proxyPort': 7890, 'proxyUsername': 'audit-user', 'proxyPasswordConfigured': True, 'newsFakeIpCompat': True, 'minimizeOnLaunch': True, 'autoCloseOnExit': True, 'aiDiagnosisEnabled': False, 'aiProviders': []}
instances = [{'id': 'inst-a', 'name': 'Audit Vanilla', 'minecraftVersion': '1.21.1', 'loaderType': 'Vanilla', 'gameDirectory': 'D:\\AuditFixtures\\A'}, {'id': 'inst-b', 'name': 'Audit Fabric', 'minecraftVersion': '1.21.1', 'loaderType': 'Fabric', 'gameDirectory': 'D:\\AuditFixtures\\B'}]
fixture = {'config': copy.deepcopy(base_config), 'patches': [], 'failed': set()}

def finding(id, title, actual):
    report['findings'].append({'id': id, 'title': title, 'actual': actual, 'status': 'REPRODUCED'})

with sync_playwright() as p:
    browser = p.chromium.launch(channel='msedge', headless=True)
    context = browser.new_context(viewport={'width': 1280, 'height': 800}, locale='zh-CN')
    page = context.new_page()
    page.on('pageerror', lambda e: report['errors'].append(str(e)))
    def api(route):
        request = route.request
        path = urlparse(request.url).path
        report['requests'].append({'path': path, 'method': request.method})
        if path in fixture['failed']:
            route.fulfill(status=503, json={'error': 'AUDIT_SERVICE_UNAVAILABLE'})
            return
        if path == '/api/config' and request.method == 'POST':
            patch = request.post_data_json
            fixture['patches'].append(patch)
            fixture['config'].update(patch)
            body = {'ok': True}
        elif path == '/api/state':
            body = {'config': fixture['config'], 'instances': instances, 'accounts': [], 'java': []}
        elif path == '/api/instances':
            body = {'instances': instances, 'selectedInstanceId': fixture['config']['selectedInstanceId']}
        elif path == '/api/sources':
            body = {'selected': 'bmclapi', 'sources': [{'key': 'official', 'name': 'Official', 'available': True, 'latencyMs': 80}, {'key': 'bmclapi', 'name': 'BMCLAPI', 'available': True, 'latencyMs': 20}]}
        elif path == '/api/worlds':
            body = {'worlds': [{'name': 'Audit World', 'folderName': 'Audit World', 'instanceId': 'inst-b', 'sizeBytes': 1024}]}
        elif path == '/api/servers':
            body = {'servers': [{'id': 's1', 'name': 'Audit Server', 'host': 'example.invalid', 'port': 25565}]}
        elif path == '/api/versions':
            body = {'local': ['1.21.1']}
        elif path == '/api/versions/remote':
            body = {'versions': [{'id': '1.21.1', 'type': 'release'}]}
        elif path == '/api/news':
            body = {'items': [], 'meta': {'state': 'empty', 'isStale': False}}
        elif path == '/api/update/check':
            body = {'enabled': True, 'currentVersion': '1.0.0', 'updateAvailable': False}
        elif path == '/api/update':
            body = {'state': 'idle', 'currentVersion': '1.0.0'}
        else:
            body = {'ok': True, 'accounts': [], 'tasks': [], 'installations': [], 'totalPlaySeconds': 0, 'totalPlayCount': 0, 'sessions': [], 'perInstance': []}
        route.fulfill(json=body)
    context.route('**/api/**', api)
    def go(nav):
        page.locator(f'[data-nav="{nav}"]').click()
        page.wait_for_function('(n) => SG.__test__.getCurrentView() === n', arg=nav)
        expect(page.locator('#view-' + nav)).to_be_visible()
    def fresh(nav='home'):
        page.goto(f'http://127.0.0.1:{server.server_port}/#{nav}', wait_until='networkidle')
        page.wait_for_function('(n) => window.SG && SG.__test__ && SG.__test__.getCurrentView() === n', arg=nav)
    try:
        fresh()
        selected = page.evaluate('SG.__test__.getSelectedInstanceId()')
        assert selected == 'inst-a'
        finding('FA-01', 'Saved selected instance is dropped by live API adapter', {'serverSelected': 'inst-b', 'actualSelected': selected, 'homeTitle': page.locator('.home-game-title').inner_text()})
        page.screenshot(path=str(out / 'saved-instance-lost.png'), animations='disabled')
        page.locator('#btnPlay').click()
        page.wait_for_timeout(100)
        launch_paths = [r['path'] for r in report['requests'] if r['path'].endswith('/launch')]
        assert launch_paths == ['/api/instances/inst-a/launch']
        report['findings'][-1]['actual']['dispatchedLaunchPath'] = launch_paths[0]

        go('settings')
        mapped = page.evaluate('SG.__test__.getState().config')
        missing = [k for k in base_config if k not in mapped]
        assert 'proxyEnabled' in missing and 'newsFakeIpCompat' in missing and 'minimizeOnLaunch' in missing
        page.locator('#btnProxy').click()
        actual = {'enabled': page.locator('#proxyToggle').get_attribute('aria-checked'), 'host': page.locator('#pHost').input_value(), 'port': page.locator('#pPort').input_value(), 'missingFields': missing}
        assert actual['enabled'] == 'false' and actual['host'] == ''
        page.screenshot(path=str(out / 'configured-proxy-looks-empty.png'), animations='disabled')
        page.locator('#pOk').click()
        page.wait_for_timeout(100)
        actual['savedPatch'] = fixture['patches'][-1]
        assert actual['savedPatch']['proxyEnabled'] is False and actual['savedPatch']['proxyPort'] == 0
        finding('FA-02', 'Existing proxy and other settings disappear from UI; saving can overwrite them', actual)

        error_count = len(report['errors'])
        page.locator('#btnSourceChange').click()
        page.wait_for_timeout(150)
        assert any('dlSource is not defined' in e for e in report['errors'][error_count:])
        assert page.locator('.source-option').count() == 0
        finding('FA-03', 'Download-source selector throws a ReferenceError', {'error': report['errors'][error_count:], 'availableSourceCount': 2, 'renderedSourceCount': 0})

        page.locator('[data-toggle="aiDiag"]').click()
        page.wait_for_timeout(100)
        assert fixture['config']['aiDiagnosisEnabled'] is True
        assert page.evaluate('SG.__test__.getState().config.aiDiagnosisEnabled') is False
        page.evaluate('SG.setLocale("en")')
        page.wait_for_function('SG.__test__.getCurrentView() === "settings"')
        expect(page.locator('[data-toggle="aiDiag"]')).to_have_attribute('aria-checked', 'false')
        finding('FA-04', 'AI enable switch does not update cached state after successful save', {'persistedInFixture': True, 'cachedState': False, 'afterLocaleRerender': 'false'})
        fixture['failed'].add('/api/config')
        page.locator('[data-toggle="aiDiag"]').click()
        page.wait_for_timeout(100)
        expect(page.locator('[data-toggle="aiDiag"]')).to_have_attribute('aria-checked', 'true')
        report['findings'][-1]['failedSaveAlsoLeavesSwitchOn'] = True
        fixture['failed'].clear()

        fixture['failed'].update(['/api/worlds', '/api/servers'])
        fresh('worlds')
        world_text = page.locator('#view-worlds').inner_text()
        go('servers')
        server_text = page.locator('#view-servers').inner_text()
        assert 'AUDIT_SERVICE_UNAVAILABLE' not in world_text + server_text
        assert page.locator('#view-servers .empty-state').count() == 1
        finding('FA-05', 'World and server read failures are rendered as empty collections', {'worldText': world_text, 'serverText': server_text, 'fixtureServerCount': 1, 'fixtureWorldCount': 1, 'serviceStatus': 503})
        fixture['failed'].clear()

        fresh()
        for nav in ['home', 'installations', 'worlds', 'servers', 'playtime', 'content', 'skins', 'downloads', 'news', 'settings']:
            go(nav)
            text = page.locator('#view-' + nav).inner_text()
            assert len(text.strip()) > 5
            report['smoke'].append({'view': nav, 'visible': True, 'textLength': len(text), 'horizontalOverflow': page.locator('#contentPanel').evaluate('(e) => e.scrollWidth - e.clientWidth')})
        report['status'] = 'COMPLETE_WITH_REPRODUCED_FINDINGS'
    finally:
        (out / 'browser-audit.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        browser.close()
        server.shutdown()
        server.server_close()
print(json.dumps({k: v for k, v in report.items() if k != 'requests'}, ensure_ascii=False, indent=2))
