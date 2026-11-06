'use strict';
/*
 * css-token-contract.test.cjs — M1-3 门槛测试：CSS token 单一来源与无硬编码
 * ---------------------------------------------------------------------------
 * 由来（M1-3，2026-11-06）：
 *   docs/design-system.md 的"设计红线"第 5 条写着「禁止硬编码色值，所有颜色走 token」，
 *   但 figma.css 里实际有 18 行选择器内字面色值（新闻占位图、诊断徽章、Mod 删除按钮等），
 *   另有 18 处 `var(--x, #hex)` 死回退 —— 那些 token 早已定义，回退值永远不会生效，
 *   只会让后来者以为"这个 token 可能不存在"。
 *
 *   更麻烦的是三层 CSS 的覆盖关系没有任何机器校验：figma.css → pineglow.css →
 *   redesign.css，同名的 23 个 token 被逐层覆盖，最终值由加载顺序决定。
 *   改任何一层都可能"改了不生效"，而没有任何测试会报错。
 *
 * 本文件把三条纪律钉成门槛：
 *   CT-01 三层 CSS 的 :root 之外不得出现字面色值（identity/RGBA 除外）
 *   CT-02 引用的 token 必须已定义（否则属性静默失效）
 *   CT-03 "已定义 token 的回退值"视为缺陷（死回退，误导后来者）
 *   CT-04 加载顺序与文档一致：figma → pineglow → redesign
 *   CT-05 检查器脚本本身对已知违规必须非零退出（证明断言有牙）
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const ROOT = path.resolve(__dirname, '..', '..');
const CHECKER = path.join(ROOT, 'scripts', 'check-css-tokens.cjs');
const CSS_DIR = path.join(ROOT, 'ui', 'css');
const INDEX = path.join(ROOT, 'ui', 'index.html');
const CSS_FILES = ['figma.css', 'pineglow.css', 'redesign.css'];

function readCss(name) {
  return fs.readFileSync(path.join(CSS_DIR, name), 'utf8');
}

/** 把文件按 :root 块切开，返回 { rootBlocks, outside } */
function splitRoot(text) {
  const lines = text.split('\n');
  const outside = [];
  let depth = 0;
  let inRoot = false;
  lines.forEach((line, i) => {
    if (!inRoot && /:root\s*\{/.test(line)) { inRoot = true; depth = 0; }
    if (inRoot) {
      depth += (line.match(/\{/g) || []).length - (line.match(/\}/g) || []).length;
      if (depth <= 0 && /\}/.test(line)) inRoot = false;
      return;
    }
    outside.push({ line: i + 1, text: line });
  });
  return outside;
}

function collectTokens() {
  const defs = new Map();
  for (const name of CSS_FILES) {
    const text = readCss(name);
    let depth = 0;
    let inRoot = false;
    text.split('\n').forEach((line, i) => {
      if (!inRoot && /:root\s*\{/.test(line)) { inRoot = true; depth = 0; }
      if (!inRoot) return;
      depth += (line.match(/\{/g) || []).length - (line.match(/\}/g) || []).length;
      const re = /(--[a-zA-Z0-9-]+)\s*:\s*([^;}]+)/g;
      let m;
      while ((m = re.exec(line))) defs.set(m[1], { value: m[2].trim(), file: name, line: i + 1 });
      if (depth <= 0 && /\}/.test(line)) inRoot = false;
    });
  }
  return defs;
}

test('CT-01 三层 CSS 的 :root 之外没有字面色值', () => {
  const offenders = [];
  for (const name of CSS_FILES) {
    for (const { line, text } of splitRoot(readCss(name))) {
      // var(--x, #hex) 的回退值单独由 CT-03 处理，这里先剥掉再看残留
      const stripped = text.replace(/var\(\s*--[a-zA-Z0-9-]+\s*,\s*[^)]*\)/g, 'VAR');
      if (/#[0-9a-fA-F]{3,8}\b/.test(stripped)) offenders.push(`${name}:${line} ${text.trim().slice(0, 90)}`);
    }
  }
  assert.deepEqual(offenders, [],
    '选择器内出现字面色值（必须改用 token）：\n' + offenders.join('\n'));
});

test('CT-02 引用的 token 必须已定义', () => {
  const tokens = collectTokens();
  const missing = [];
  for (const name of CSS_FILES) {
    readCss(name).split('\n').forEach((line, i) => {
      const re = /var\(\s*(--[a-zA-Z0-9-]+)/g;
      let m;
      while ((m = re.exec(line))) {
        if (!tokens.has(m[1])) missing.push(`${name}:${i + 1} → ${m[1]}`);
      }
    });
  }
  assert.deepEqual(missing, [],
    '引用了未定义的 token（该属性会静默失效）：\n' + missing.join('\n'));
});

test('CT-03 已定义 token 不得再写回退值（死回退）', () => {
  const tokens = collectTokens();
  const dead = [];
  for (const name of CSS_FILES) {
    readCss(name).split('\n').forEach((line, i) => {
      const re = /var\(\s*(--[a-zA-Z0-9-]+)\s*,\s*#[0-9a-fA-F]{3,8}\s*\)/g;
      let m;
      while ((m = re.exec(line))) {
        if (tokens.has(m[1])) dead.push(`${name}:${i + 1} → ${m[1]}`);
      }
    });
  }
  assert.deepEqual(dead, [],
    '以下回退值永远不会生效，只会让读者误以为 token 可能缺失：\n' + dead.join('\n'));
});

test('CT-04 CSS 加载顺序与文档一致', () => {
  const html = fs.readFileSync(INDEX, 'utf8');
  const idx = CSS_FILES.map(f => html.indexOf('css/' + f));
  for (let i = 0; i < CSS_FILES.length; i++) {
    assert.ok(idx[i] >= 0, `index.html 未加载 ${CSS_FILES[i]}`);
  }
  assert.ok(idx[0] < idx[1] && idx[1] < idx[2],
    `加载顺序必须为 figma → pineglow → redesign，实际位置 ${idx.join(' < ')}。`
    + '顺序错会让 token 覆盖关系整体反转，且不会有任何报错。');
});

test('CT-05 检查器对违规必须非零退出（断言有牙）', () => {
  // 真实 UI 必须先通过
  const real = spawnSync(process.execPath, [CHECKER], { encoding: 'utf8', cwd: ROOT });
  assert.equal(real.status, 0, `真实 ui/ 应通过，实际 status=${real.status}\n${real.stderr}`);

  // 造一个含字面色值的夹具，检查器必须非零退出
  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'pineglow-token-'));
  const cssDir = path.join(tmp, 'ui', 'css');
  fs.mkdirSync(cssDir, { recursive: true });
  for (const name of CSS_FILES) fs.copyFileSync(path.join(CSS_DIR, name), path.join(cssDir, name));

  const figmaPath = path.join(cssDir, 'figma.css');
  fs.appendFileSync(figmaPath, '\n.bad-example { color: #ff00ff; }\n', 'utf8');

  const bad = spawnSync(process.execPath, [CHECKER], { encoding: 'utf8', cwd: tmp });
  assert.notEqual(bad.status, 0, '检查器必须对字面色值非零退出，否则门槛是空的');

  // 再验未定义 token 也能被抓到
  fs.writeFileSync(figmaPath, readCss('figma.css'), 'utf8');
  fs.appendFileSync(figmaPath, '\n.bad-token { color: var(--does-not-exist); }\n', 'utf8');
  const bad2 = spawnSync(process.execPath, [CHECKER], { encoding: 'utf8', cwd: tmp });
  assert.notEqual(bad2.status, 0, '检查器必须对未定义 token 非零退出');

  fs.rmSync(tmp, { recursive: true, force: true });
});
