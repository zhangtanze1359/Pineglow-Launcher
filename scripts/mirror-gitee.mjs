#!/usr/bin/env node
/**
 * 本机手动镜像发布到 Gitee（国内更新源）
 *
 * 用法：
 *   GITEE_TOKEN=<token> node scripts/mirror-gitee.mjs v1.0.0
 *
 * 流程：
 *   1. 从 GitHub Release 下载 exe（保证与发布产物一致）
 *   2. 创建/复用 Gitee 同名 release，上传 exe 附件
 *   3. 计算 sha256/size，更新 Gitee 仓库根 update.json（主源=Gitee，备用=GitHub）
 *
 * 说明：
 *   - GitHub Actions runner（Azure 美国）到 Gitee 跨国上传 42MB 会超时，
 *     本机网络到 Gitee 通畅，故设计为本机手动执行（约 1 分钟）
 *   - token 只从环境变量读取，不写入任何文件
 */
import { createHash } from 'node:crypto';
import { readFile, writeFile, stat } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

// ---- 配置（Gitee 账号与 GitHub 账号不同，注意区分）----
const GITEE_OWNER = 'ztz1359';
const GITEE_REPO = 'pineglow-launcher';
const GH_OWNER = 'zhangtanze1359';
const GH_REPO = 'Pineglow-Launcher';
const EXE_NAME = 'Pineglow-Launcher-win-x64-single-file.exe';

const token = process.env.GITEE_TOKEN;
const tag = process.argv[2];

if (!token) { console.error('缺少 GITEE_TOKEN 环境变量'); process.exit(1); }
if (!tag) { console.error('用法: GITEE_TOKEN=<token> node scripts/mirror-gitee.mjs v1.0.0'); process.exit(1); }
const version = tag.replace(/^v/, '');
const giteeApi = `https://gitee.com/api/v5/repos/${GITEE_OWNER}/${GITEE_REPO}`;

// ---- 1. 下载 exe（GitHub Release）----
console.log(`[1/4] 从 GitHub Release 下载 ${EXE_NAME} …`);
const exePath = join(tmpdir(), EXE_NAME.replace(/\.exe$/, `-${tag}.exe`));
const dlUrl = `https://github.com/${GH_OWNER}/${GH_REPO}/releases/download/${tag}/${EXE_NAME}`;
const dlRes = await fetch(dlUrl);
if (!dlRes.ok) { console.error(`下载失败 HTTP ${dlRes.status}: ${dlUrl}`); process.exit(1); }
const exeBuf = Buffer.from(await dlRes.arrayBuffer());
await writeFile(exePath, exeBuf);
const { size } = await stat(exePath);
const sha256 = createHash('sha256').update(exeBuf).digest('hex');
console.log(`  已下载 ${(size / 1024 / 1024).toFixed(1)}MB, sha256=${sha256.slice(0, 16)}…`);

// ---- 2. 创建/复用 Gitee release ----
console.log('[2/4] 创建/复用 Gitee release …');
const tryCreate = await fetch(`${giteeApi}/releases`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ access_token: token, tag_name: tag, name: tag, body: `Pineglow Launcher ${version}`, target_commitish: 'master' }),
}).then(r => r.json());

let release;
if (tryCreate.id) {
  release = tryCreate;
  console.log(`  已创建 release id=${release.id}`);
} else {
  const existing = await fetch(`${giteeApi}/releases/tags/${tag}?access_token=${token}`).then(r => r.json());
  if (existing.id) { release = existing; console.log(`  复用 release id=${release.id}`); }
  else { console.error(`创建/查询 release 失败: ${JSON.stringify(tryCreate).slice(0, 200)}`); process.exit(1); }
}

// ---- 3. 上传 exe 附件 ----
console.log('[3/4] 上传 exe 附件到 Gitee …');
const oldAsset = (release.assets || []).find(a => a.name === EXE_NAME);
if (oldAsset) {
  await fetch(`${giteeApi}/releases/${release.id}/attach_files/${oldAsset.id}?access_token=${token}`, { method: 'DELETE' });
  console.log('  已删除旧附件');
}
const fd = new FormData();
fd.append('access_token', token);
fd.append('file', new Blob([exeBuf], { type: 'application/octet-stream' }), EXE_NAME);
const upJson = await fetch(`${giteeApi}/releases/${release.id}/attach_files`, { method: 'POST', body: fd }).then(r => r.json());
if (!upJson.browser_download_url) { console.error(`上传失败: ${JSON.stringify(upJson).slice(0, 200)}`); process.exit(1); }
console.log(`  上传成功: ${upJson.browser_download_url}`);

// ---- 4. 更新 update.json（主源=Gitee，备用=GitHub）----
console.log('[4/4] 更新 Gitee 仓库根 update.json …');
const manifest = { version, url: upJson.browser_download_url, fallbackUrl: dlUrl, size, sha256, notes: `Pineglow Launcher ${version}`, mandatory: false };
const contentB64 = Buffer.from(JSON.stringify(manifest), 'utf8').toString('base64');

let fileSha = null;
try {
  const existing = await fetch(`${giteeApi}/contents/update.json?access_token=${token}`).then(r => r.json());
  if (existing.sha) fileSha = existing.sha;
} catch { /* 不存在则创建 */ }

const body = { access_token: token, content: contentB64, message: `release: ${tag}` };
if (fileSha) body.sha = fileSha;
// Gitee contents API：不存在用 POST 创建，已存在用 PUT 更新
const method = fileSha ? 'PUT' : 'POST';
const up = await fetch(`${giteeApi}/contents/update.json`, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }).then(r => r.json());
if (!up.content) { console.error(`update.json 更新失败: ${JSON.stringify(up).slice(0, 200)}`); process.exit(1); }
console.log(`  update.json 已更新（${method}）`);

console.log('\n完成！国内用户将通过 update.json 自动获取更新（Gitee 主源，失败回退 GitHub）。');
