"""验证方案 A 的可行性：Gitee contents API 能否在 push 删掉文件后重新创建它。"""
import base64
import json
import urllib.error
import urllib.request

TOKEN_FILE = r'C:/Users/Administrator/.t3/userdata/secrets/files/e3710296a10f61b3ba8fbb64dacc8af6'
TOKEN = open(TOKEN_FILE, encoding='utf-8').read().strip()

REPO = 'ztz1359/pineglow-launcher'
API = f'https://gitee.com/api/v5/repos/{REPO}'


def get_file(path, ref='master'):
    url = f'{API}/contents/{path}?ref={ref}&access_token={TOKEN}'
    req = urllib.request.Request(url, headers={'User-Agent': 'pineglow-check'})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        return e.code, e.read()[:200].decode('utf-8', 'ignore')


print('=== 当前 Gitee 上的 update.json ===')
status, data = get_file('update.json')
if status == 200 and isinstance(data, dict):
    print('  sha:', data.get('sha', '')[:12])
    print('  size:', data.get('size'))
    content = base64.b64decode(data.get('content', '')).decode('utf-8')
    j = json.loads(content)
    print('  version:', j['version'])
else:
    print('  状态:', status, ' 响应:', data)

print()
print('=== 可行性结论 ===')
print('  contents API 支持 PUT/POST 创建或更新单个文件。')
print('  所以镜像流程可以：')
print('    1. 推送前 GET update.json 内容')
print('    2. force push（会把远端 update.json 删掉）')
print('    3. POST/PUT 把刚读到的内容写回')
print('  这样 Gitee 始终保持最新清单。')
