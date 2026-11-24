#!/usr/bin/env python3
"""OPEN-08 验收项 1 后半段：用真实 Key 跑一次 CurseForge 搜索 + 下载。

安全约定（必须遵守）：
  * Key 只从环境变量 PINEGLOW_CF_KEY 读，**绝不写入本文件、日志或任何落盘产物**。
  * 日志里出现的任何字符串都要先过 scrub() —— 把 Key 替换成 <redacted>。
  * 只下载一个公开 Mod 的 jar 到临时目录，验完即删。
  * 不做任何需要写权限或付费的操作：只有 search / files / download-url / GET jar。

用法：
  PINEGLOW_CF_KEY_FILE=<path> python verify-curseforge-live.py

  为什么用文件而不是环境变量：Key 形如 `$2a$10$...`，在 shell 里会被当作变量展开
  （Git Bash 实测把 60 字符破坏成 2 字符）。文件传值不做任何展开，最安全。
"""
import hashlib
import json
import os
import sys
import urllib.request
import urllib.error

API = 'https://api.curseforge.com/v1'
UA = 'PineglowLauncher/1.0.6 (OPEN-08 verification)'

_key_file = os.environ.get('PINEGLOW_CF_KEY_FILE', '')
if _key_file and os.path.exists(_key_file):
    KEY = open(_key_file, encoding='utf-8').read().strip()
else:
    KEY = os.environ.get('PINEGLOW_CF_KEY', '').strip()


def scrub(text):
    if KEY and KEY in text:
        return text.replace(KEY, '<redacted>')
    return text


def get(url, timeout=30):
    req = urllib.request.Request(url, headers={
        'x-api-key': KEY,
        'Accept': 'application/json',
        'User-Agent': UA,
    })
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, r.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read()
    except Exception as e:
        return None, str(e).encode('utf-8', 'ignore')


def main():
    out = []

    def log(s):
        s = scrub(s)
        out.append(s)
        print(s)

    log('=== OPEN-08 验收项 1：真实 CurseForge 调用 ===')
    log('')

    if not KEY:
        log('FAIL: 环境变量 PINEGLOW_CF_KEY 为空，无法验证。')
        return 1
    log(f'Key 状态: 已提供（长度 {len(KEY)}，值不打印）')
    log('')

    # --- 1. 搜索 ---
    log('--- 1. GET /mods/search（搜索公开 Mod）---')
    url = (API + '/mods/search?gameId=432&classId=6&searchFilter=jei'
           '&sortField=2&sortOrder=desc&pageSize=3')
    status, body = get(url)
    log(f'    HTTP {status}')
    if status != 200:
        log(f'    响应: {scrub(body[:300].decode("utf-8", "ignore"))}')
        log('FAIL: 搜索失败')
        return 1

    data = json.loads(body)
    items = data.get('data', [])
    log(f'    返回条目数: {len(items)}')
    if not items:
        log('FAIL: 搜索无结果')
        return 1

    mod = items[0]
    mod_id = mod['id']
    log(f'    选中 Mod: id={mod_id}  name={mod.get("name", "")!r}')
    log('')

    # --- 2. 列文件 ---
    log('--- 2. GET /mods/{id}/files（列出可用文件）---')
    status, body = get(f'{API}/mods/{mod_id}/files?pageSize=3')
    log(f'    HTTP {status}')
    if status != 200:
        log('FAIL: 列文件失败')
        return 1
    files = json.loads(body).get('data', [])
    log(f'    返回文件数: {len(files)}')
    if not files:
        log('FAIL: 无可用文件')
        return 1
    f0 = files[0]
    file_id = f0['id']
    fname = f0.get('fileName', '')
    log(f'    选中文件: id={file_id}  name={fname!r}')
    log('')

    # --- 3. 取下载地址 ---
    log('--- 3. GET /mods/{id}/files/{fileId}/download-url ---')
    status, body = get(f'{API}/mods/{mod_id}/files/{file_id}/download-url')
    log(f'    HTTP {status}')
    if status != 200:
        log('FAIL: 取下载地址失败（部分 Mod 作者禁止 API 直下，属正常业务限制）')
        log(f'    响应: {scrub(body[:300].decode("utf-8", "ignore"))}')
        return 1
    dl = json.loads(body).get('data', '')
    log(f'    下载地址: {"已获取（域名 " + dl.split("/")[2] + "）" if dl else "空"}')
    if not dl:
        log('FAIL: 下载地址为空')
        return 1
    log('')

    # --- 4. 真实下载 ---
    log('--- 4. 真实下载 jar 到临时目录 ---')
    req = urllib.request.Request(dl, headers={'User-Agent': UA})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            payload = r.read()
            log(f'    HTTP {r.status}  大小 {len(payload)} bytes')
    except Exception as e:
        log(f'FAIL: 下载异常 {scrub(str(e))}')
        return 1

    sha = hashlib.sha256(payload).hexdigest().upper()
    log(f'    SHA-256: {sha}')
    if len(payload) < 1024:
        log('FAIL: 下载内容过小，不像真实 jar')
        return 1
    log('')
    log('RESULT: PASS —— 真实 Key 可完成搜索 → 列文件 → 取下载地址 → 实际下载全链。')
    log('（下载内容只在内存中校验，未写入磁盘。）')

    outfile = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'raw', 'curseforge-live.txt')
    os.makedirs(os.path.dirname(outfile), exist_ok=True)
    with open(outfile, 'w', encoding='utf-8') as f:
        f.write('\n'.join(out) + '\n')
    print(f'\n(已写入 {outfile}；内容已脱敏)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
