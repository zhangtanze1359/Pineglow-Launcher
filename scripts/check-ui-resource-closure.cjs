#!/usr/bin/env node
/*
 * check-ui-resource-closure.cjs —— UI 本地静态资源闭包检查（发版阻断项 R02 的门槛）
 *
 * 背景（独立事实，见 docs/release-readiness-review-2026-10-05.md R02）：
 *   ui/index.html 引用 css/pineglow.css，该 CSS 又引用 ../assets/pine-forest.svg；
 *   两个文件当时都未纳入候选源码白名单，副本/正式包缺它们，但 Node 全量和构建仍然通过。
 *   也就是说「构建通过」不能证明「用户拿到的界面资源是完整的」。
 *
 * 本检查器的职责：把 UI 目录里的本地静态引用闭包求出来，逐项断言文件真实存在。
 *   - 只处理本地相对引用；远程（http/https/protocol-relative）、data:、blob:、mailto:、
 *     tel:、javascript:、about:、纯锚点(#) 引用一律排除。
 *   - HTML：src / href / poster / data-src / srcset(取每个候选) 属性，外加内联 <style> 里的 url()。
 *   - CSS：url(...) 与 @import "..." / @import url(...)。
 *   - JS：只认「看起来像本地静态资源路径」的引号字符串（带静态资源扩展名），
 *     避免把 API 路由、i18n key 之类误判为资源。
 *   - 引用目标按「引用文件所在目录」解析；去掉 query/hash；反斜杠统一为正斜杠。
 *
 * 退出码：0 = 闭包完整；1 = 有缺失/非法引用；2 = 用法错误。
 *
 * 用法：
 *   node scripts/check-ui-resource-closure.cjs [--root ui] [--json <out.json>] [--quiet]
 */

'use strict';

const fs = require('fs');
const path = require('path');

const ASSET_EXT = new Set([
  '.css', '.js', '.mjs', '.cjs', '.json',
  '.html', '.htm',
  '.svg', '.png', '.jpg', '.jpeg', '.webp', '.gif', '.ico', '.bmp', '.avif',
  '.woff', '.woff2', '.ttf', '.otf', '.eot',
  '.mp3', '.ogg', '.wav', '.mp4', '.webm',
  '.glb', '.gltf', '.bin', '.wasm'
]);

const NON_LOCAL_SCHEME = /^(?:[a-z][a-z0-9+.-]*:|\/\/)/i;

function parseArgs(argv) {
  const out = { root: null, json: null, quiet: false, entries: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--root') { out.root = argv[++i]; }
    else if (a === '--json') { out.json = argv[++i]; }
    else if (a === '--entry') { out.entries.push(argv[++i]); }
    else if (a === '--quiet') { out.quiet = true; }
    else if (a === '--help' || a === '-h') { out.help = true; }
    else { throw new Error(`未知参数: ${a}`); }
  }
  return out;
}

function isLocalRef(raw) {
  if (!raw) return false;
  let v = raw.trim();
  if (v === '') return false;
  if (v.startsWith('#')) return false;
  if (NON_LOCAL_SCHEME.test(v)) return false;
  if (/^(data|blob|mailto|tel|javascript|about|file):/i.test(v)) return false;
  if (v.includes('{{') || v.includes('}}') || v.includes('${') || v.includes('<%')) return false;
  if (v.includes('%s') || v.includes('{0}')) return false;
  return true;
}

function normalizeRef(raw, referrerDir) {
  let v = raw.trim();
  // 去掉引号/括号残渣
  v = v.replace(/^['"]/, '').replace(/['"]$/, '');
  const hash = v.indexOf('#');
  if (hash >= 0) v = v.slice(0, hash);
  const q = v.indexOf('?');
  if (q >= 0) v = v.slice(0, q);
  if (v === '') return null;
  try { v = decodeURIComponent(v); } catch { /* 保留原样 */ }
  if (v.startsWith('/')) return null; // 站点绝对路径：本应用由本地 HTTP 根提供，按不支持处理
  return path.normalize(path.join(referrerDir, v)).replace(/\\/g, '/');
}

const HTML_ATTR = /(?:^|[\s"'])(src|href|poster|data-src)\s*=\s*("([^"]*)"|'([^']*)'|([^\s>]+))/gi;
const SRCSET_ATTR = /\bsrcset\s*=\s*("([^"]*)"|'([^']*)')/gi;
const CSS_URL = /url\(\s*(?:"([^"]*)"|'([^']*)'|([^)'"]*))\s*\)/gi;
const CSS_IMPORT = /@import\s+(?:url\(\s*(?:"([^"]*)"|'([^']*)'|([^)'"]*))\s*\)|"([^"]*)"|'([^']*)')/gi;
const JS_STRING = /(["'`])([^"'`\r\n]{1,300})\1/g;
const INLINE_STYLE = /<style[^>]*>([\s\S]*?)<\/style>/gi;

function scanHtml(text) {
  const refs = [];
  let m;
  HTML_ATTR.lastIndex = 0;
  while ((m = HTML_ATTR.exec(text)) !== null) {
    const val = m[3] !== undefined ? m[3] : (m[4] !== undefined ? m[4] : m[5]);
    refs.push(val);
  }
  SRCSET_ATTR.lastIndex = 0;
  while ((m = SRCSET_ATTR.exec(text)) !== null) {
    const raw = m[2] !== undefined ? m[2] : m[3];
    for (const part of String(raw).split(',')) {
      const candidate = part.trim().split(/\s+/)[0];
      if (candidate) refs.push(candidate);
    }
  }
  INLINE_STYLE.lastIndex = 0;
  while ((m = INLINE_STYLE.exec(text)) !== null) {
    for (const r of scanCss(m[1])) refs.push(r);
  }
  return refs;
}

function scanCss(text) {
  const refs = [];
  let m;
  CSS_URL.lastIndex = 0;
  while ((m = CSS_URL.exec(text)) !== null) {
    const val = m[1] !== undefined ? m[1] : (m[2] !== undefined ? m[2] : m[3]);
    if (val) refs.push(val);
  }
  CSS_IMPORT.lastIndex = 0;
  while ((m = CSS_IMPORT.exec(text)) !== null) {
    const val = m[1] !== undefined ? m[1] : (m[2] !== undefined ? m[2] : (m[3] !== undefined ? m[3] : (m[4] !== undefined ? m[4] : m[5])));
    if (val) refs.push(val);
  }
  return refs;
}

function scanJs(text) {
  const refs = [];
  let m;
  // sourceMappingURL / sourceURL 注释里的路径不是运行期依赖，且压缩产物里常带源码树路径
  text = text.replace(/\/\/[#@]\s*source(?:Mapping)?URL=[^\r\n]*/g, '');
  JS_STRING.lastIndex = 0;
  while ((m = JS_STRING.exec(text)) !== null) {
    const val = m[2];
    if (!val) continue;
    const clean = val.split('#')[0].split('?')[0];
    const ext = path.extname(clean).toLowerCase();
    if (!ASSET_EXT.has(ext)) continue;
    if (clean.startsWith('/') || clean.startsWith('.')) continue; // 绝对/纯相对前缀交给别的规则
    if (/^[a-z][a-z0-9+.-]*:/i.test(clean)) continue;
    if (clean.split('/').length > 4) continue;
    refs.push(val);
  }
  return refs;
}

function scanFile(absPath, relPath) {
  const ext = path.extname(absPath).toLowerCase();
  let text;
  try { text = fs.readFileSync(absPath, 'utf8'); } catch { return []; }
  if (ext === '.html' || ext === '.htm') return scanHtml(text);
  if (ext === '.css') return scanCss(text);
  // vendor/ 下是第三方压缩产物，字符串启发式会产生大量与运行期无关的假阳性（源码路径等），不参与 JS 扫描。
  if (/(^|\/)vendor\//i.test(relPath || '')) return [];
  if (ext === '.js' || ext === '.mjs' || ext === '.cjs') return scanJs(text);
  return [];
}

function listFiles(dir, acc = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === 'node_modules' || entry.name === '.git') continue;
      listFiles(p, acc);
    } else if (entry.isFile()) {
      acc.push(p);
    }
  }
  return acc;
}

function main() {
  let args;
  try { args = parseArgs(process.argv.slice(2)); }
  catch (e) { console.error(String(e.message || e)); process.exit(2); }
  if (args.help) { console.log('用法: node scripts/check-ui-resource-closure.cjs [--root ui] [--json out.json] [--quiet]'); return; }

  const scriptDir = __dirname;
  const repoRoot = path.resolve(scriptDir, '..');
  const root = path.resolve(repoRoot, args.root || 'ui');
  if (!fs.existsSync(root) || !fs.statSync(root).isDirectory()) {
    console.error(`[closure] 根目录不存在或不是目录: ${root}`);
    process.exit(2);
  }

  const files = listFiles(root);
  const byRel = new Map();
  for (const f of files) byRel.set(path.relative(root, f).replace(/\\/g, '/'), f);

  const entries = args.entries.length > 0
    ? args.entries.slice()
    : files.filter(f => /\.html?$/i.test(f)).map(f => path.relative(root, f).replace(/\\/g, '/'));

  if (entries.length === 0) {
    console.error(`[closure] 未找到 HTML 入口: ${root}`);
    process.exit(2);
  }

  const visited = new Set();
  const edges = [];
  const missing = [];
  const queue = entries.map(e => e.replace(/\\/g, '/'));

  while (queue.length > 0) {
    const rel = queue.shift();
    if (visited.has(rel)) continue;
    visited.add(rel);
    const abs = byRel.get(rel);
    if (!abs || !fs.existsSync(abs)) {
      missing.push({ from: '(entry)', ref: rel, resolved: rel });
      continue;
    }
    const referrerDir = path.posix.dirname(rel);
    for (const raw of scanFile(abs, rel)) {
      if (!isLocalRef(raw)) continue;
      const resolved = normalizeRef(raw, referrerDir);
      if (!resolved) continue;
      const exists = byRel.has(resolved);
      edges.push({ from: rel, ref: raw.trim(), resolved, exists });
      if (!exists) { missing.push({ from: rel, ref: raw.trim(), resolved }); continue; }
      const ext = path.extname(resolved).toLowerCase();
      if ((ext === '.css' || ext === '.html' || ext === '.htm' || ext === '.js') && !visited.has(resolved)) {
        queue.push(resolved);
      }
    }
  }

  const result = {
    generatedAt: new Date().toISOString(),
    root: root.replace(/\\/g, '/'),
    entryPoints: entries,
    scannedFiles: visited.size,
    totalLocalReferences: edges.length,
    missingCount: missing.length,
    missing
  };

  if (args.json) {
    fs.mkdirSync(path.dirname(path.resolve(args.json)), { recursive: true });
    fs.writeFileSync(path.resolve(args.json), JSON.stringify(result, null, 2), 'utf8');
  }

  if (!args.quiet) {
    console.log(`[closure] root=${result.root}`);
    console.log(`[closure] 入口: ${entries.join(', ')}`);
    console.log(`[closure] 扫描文件 ${visited.size} 个，本地引用 ${edges.length} 条`);
    if (missing.length > 0) {
      console.log('[closure] 缺失引用:');
      for (const m of missing) console.log(`  - ${m.from} -> ${m.ref}  (解析为 ${m.resolved})`);
    } else {
      console.log('[closure] 本地资源闭包完整：无缺失引用');
    }
    for (const e of edges) {
      if (/\.(css)$/i.test(e.resolved)) console.log(`[closure]   css: ${e.from} -> ${e.resolved}`);
    }
  }

  if (missing.length > 0) process.exit(1);
  return;
}

main();
