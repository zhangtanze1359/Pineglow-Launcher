'use strict';
/*
 * goal-core-flow.test.cjs — LONG-QUALITY-GOAL / G2 六流程普通行为验证
 * ---------------------------------------------------------------------------
 * 与 api-contract / MR0x 静态扫描测试互补：这里在 vm 中加载真实 i18n.js + 真实
 * launcher.js，注入可控 SG.api 与受控 DOM，然后**调用真实生产入口**
 * （openRecoveryPanel / openModBisect / openDiagnoseHistory / handleImportFiles /
 *   initUpdateSection / renderInstallations+openInstanceMenu），断言可观察行为：
 * 请求次数、按钮可用性、状态文案、失败分支、重试入口。
 *
 * 静态扫描只能作为辅助，不能单独证明行为；真实 Avalonia/WebView2 GUI 仍属未验证。
 *
 * 本文件下面的 DOM 桩与 ui/tests/launcher-behavior.test.cjs 同源（同一受控桩代码），
 * 因此两类测试对 DOM 语义的判断口径一致。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');
const API_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'api.js'), 'utf8');

/* ===== 受控 DOM 桩（与 launcher-behavior.test.cjs 同源） ===== */

