const fs = require('fs');
const path = require('path');

const CSS = ['ui/css/figma.css', 'ui/css/pineglow.css', 'ui/css/redesign.css'];

// 解析 :root 块，收集 token 定义（后加载者覆盖前者）
function readTokens() {
  const defs = new Map(); // token -> {value, file, line}
  for (const f of CSS) {
    const lines = fs.readFileSync(f, 'utf8').split('\n');
    let depth = 0;
    let inRoot = false;
    lines.forEach((line, i) => {
      if (!inRoot && /:root\s*\{/.test(line)) {
        inRoot = true;
        depth = 0;
      }
      if (inRoot) {
        depth += (line.match(/\{/g) || []).length - (line.match(/\}/g) || []).length;
        const re = /(--[a-zA-Z0-9-]+)\s*:\s*([^;}]+)/g;
        let m;
        while ((m = re.exec(line))) {
          defs.set(m[1], { value: m[2].trim(), file: f, line: i + 1 });
        }
        if (depth <= 0 && /\}/.test(line)) inRoot = false;
      }
    });
  }
  return defs;
}

const tokens = readTokens();
const problems = [];

// 1) 除 :root 块外不得出现字面色值
for (const f of CSS) {
  const lines = fs.readFileSync(f, 'utf8').split('\n');
  let depth = 0;
  let inRoot = false;
  lines.forEach((line, i) => {
    if (!inRoot && /:root\s*\{/.test(line)) { inRoot = true; depth = 0; }
    if (inRoot) {
      depth += (line.match(/\{/g) || []).length - (line.match(/\}/g) || []).length;
      if (depth <= 0 && /\}/.test(line)) inRoot = false;
      return;
    }
    // 允许 var(--x, #hex) 之外的裸色值 = 违规
    const stripped = line.replace(/var\(\s*--[a-zA-Z0-9-]+\s*,\s*[^)]*\)/g, 'VAR');
    if (/#[0-9a-fA-F]{3,8}\b/.test(stripped)) {
      problems.push(`${f}:${i + 1} 选择器内出现字面色值：${line.trim().slice(0, 90)}`);
    }
  });
}

// 2) 引用但未定义的 token
const referenced = new Map();
for (const f of CSS) {
  const lines = fs.readFileSync(f, 'utf8').split('\n');
  lines.forEach((line, i) => {
    const re = /var\(\s*(--[a-zA-Z0-9-]+)/g;
    let m;
    while ((m = re.exec(line))) {
      if (!referenced.has(m[1])) referenced.set(m[1], []);
      referenced.get(m[1]).push(`${f}:${i + 1}`);
    }
  });
}
for (const [name, uses] of referenced) {
  if (!tokens.has(name)) {
    problems.push(`引用了未定义的 token ${name}，出现在：${uses.slice(0, 3).join(', ')}`);
  }
}

// 3) var(--x, #hex) 死回退：token 已定义时回退永不触发，只是误导
for (const f of CSS) {
  const lines = fs.readFileSync(f, 'utf8').split('\n');
  lines.forEach((line, i) => {
    const re = /var\(\s*(--[a-zA-Z0-9-]+)\s*,\s*#[0-9a-fA-F]{3,8}\s*\)/g;
    let m;
    while ((m = re.exec(line))) {
      if (tokens.has(m[1])) {
        problems.push(`${f}:${i + 1} ${m[1]} 已有定义，回退值永远不会生效：${line.trim().slice(0, 80)}`);
      }
    }
  });
}

if (problems.length) {
  console.error('[token-audit] 发现 ' + problems.length + ' 处问题：');
  for (const p of problems) console.error('  - ' + p);
  process.exit(1);
}

console.log(`[token-audit] OK：${tokens.size} 个 token，三层 CSS 无字面色值，无未定义引用，无死回退`);
