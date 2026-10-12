'use strict';
/*
 * MR17：核心流程键盘、焦点与缩放 —— 无障碍契约测试
 * --------------------------------------------------------------------------
 * 改动前的实测缺口（审计结论）：
 *   1. CSS 里输入框有 `outline: none`，而全文件**没有** :focus-visible 兜底
 *      → 键盘用户完全看不到焦点在哪（WCAG 2.4.7 / AA）；
 *   2. 弹窗只有鼠标路径：**没有 Escape**、**没有焦点陷阱**、关闭后**焦点丢失**；
 *   3. role="button" 的元素（账户卡片、删除账户）可聚焦却**无法用键盘激活**；
 *   4. 异步结果（toast）没有 live region，屏幕阅读器读不到；
 *   5. .account-card-del 约 19px 高，**低于 24×24**（WCAG 2.5.8 / AA 2.2）；
 *   6. 没有跳转链接，键盘用户必须逐个 Tab 穿过整个标题栏。
 *
 * 覆盖范围（明确边界，不声称端到端）：
 *   A. CSS 契约：焦点可见、焦点不被遮挡、操作区粘底、目标尺寸、跳转链接样式。
 *   B. HTML 契约：lang、跳转链接与目标可聚焦、toast live region。
 *   C. launcher.js 契约（静态扫描）：Escape / 焦点陷阱 / 焦点返回 / 对话框语义 /
 *      危险操作不默认聚焦 / role=button 键盘激活且不双重触发。
 *   D. i18n：跳转链接文案中英双语（避免英文界面出现中文）。
 *
 * **不覆盖**：真实 WebView2 里的键盘操作、屏幕阅读器宣读、125%/150%/200% 缩放核对
 * —— 卡片明确要求"键盘真实操作与屏幕阅读器手工检查独立于 DOM 模拟"，这些在本包标为**未验证**。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const CSS = fs.readFileSync(path.join(ROOT, 'ui', 'css', 'figma.css'), 'utf8');
const HTML = fs.readFileSync(path.join(ROOT, 'ui', 'index.html'), 'utf8');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');

function makeStubBrowser() {
	const document = {
		head: { appendChild() {} },
		body: { appendChild() {} },
		documentElement: { setAttribute() {}, getAttribute() { return ''; } },
		querySelector() { return null; },
		createElement() { return { style: {}, setAttribute() {}, appendChild() {} }; },
		addEventListener() {},
	};
	const ctx = {
		SG: { mock: { instances: [], mods: {}, servers: [], contentTasks: [] } },
		location: { protocol: 'http:' },
		fetch: () => Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({}), text: () => Promise.resolve('') }),
		document,
		navigator: { language: 'en-US', userLanguage: 'en-US' },
		localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
		setTimeout, clearTimeout, console,
		URLSearchParams, Promise, JSON, Date, Object, Number, Math,
	};
	Object.defineProperty(ctx, 'window', { get() { return this; }, set() { }, configurable: true });
	return ctx;
}

function loadI18n() {
	const ctx = makeStubBrowser();
	vm.createContext(ctx);
	vm.runInContext(I18N_JS, ctx);
	return ctx.SG;
}

/* ============== A. CSS 契约 ============== */

test('MR17 CSS: 焦点必须可见 —— 每处 outline:none 都要有 :focus-visible 兜底', () => {
	// 这是本次审计发现的最严重问题：有 outline:none，却没有全局 :focus-visible
	const stripped = CSS.replace(/\/\*[\s\S]*?\*\//g, ''); // 去掉注释，避免注释里的字样干扰
	// 必须是**全局**规则（行首就是 :focus-visible），而不是某个控件的 scoped 规则：
	// 改动前文件里已有 `.skin3d-canvas:focus-visible` / `.skin-control:focus-visible`，
	// 那种 scoped 规则对"绝大多数控件"没有任何帮助，不能算兜底。
	assert.ok(/^\s*:focus-visible\s*\{/m.test(stripped), 'CSS 必须有全局 :focus-visible 规则（行首，非 scoped）');

	const outlineNone = (stripped.match(/outline:\s*none/g) || []).length;
	assert.ok(outlineNone > 0, '本用例前提：文件里确实存在 outline: none');
	// 全局 :focus-visible 是这些 outline:none 的兜底，两者必须同时存在
	assert.ok(/MR17/.test(CSS), '无障碍补充块应当可识别（便于复验与整块回退）');
});

test('MR17 CSS: 焦点不被遮挡 + 操作区粘底（小窗口/高缩放）', () => {
	assert.ok(/scroll-margin-top\s*:/.test(CSS), '必须有 scroll-margin-top（WCAG 2.4.11）');
	assert.ok(/scroll-margin-bottom\s*:/.test(CSS), '必须有 scroll-margin-bottom');
	assert.ok(/\.modal-actions\s*\{[^}]*position:\s*sticky/s.test(CSS), '.modal-actions 必须粘底，滚动后仍可点');
	assert.ok(/\.modal-actions\s*\{[^}]*bottom:\s*-?24px/s.test(CSS), '粘底偏移要与 .modal 的 24px 内边距对齐');
});

test('MR17 CSS: 小图标按钮达到 24×24 最小目标尺寸', () => {
	assert.ok(/\.account-card-del[\s\S]{0,200}min-height:\s*24px/.test(CSS), '.account-card-del 必须 ≥24px 高');
	assert.ok(/\.mod-del[\s\S]{0,200}min-height:\s*24px/.test(CSS), '.mod-del 必须 ≥24px 高');
});

test('MR17 CSS: 跳转链接与屏幕阅读器专用文本', () => {
	assert.ok(/\.visually-hidden\s*\{/.test(CSS), '必须有 .visually-hidden');
	assert.ok(/\.skip-link\s*\{/.test(CSS), '必须有 .skip-link 样式');
	assert.ok(/\.skip-link:focus-visible\s*\{/.test(CSS), '跳转链接必须在获得焦点时可见');
});

/* ============== B. HTML 契约 ============== */

test('MR17 HTML: lang、跳转链接与可聚焦目标、toast live region', () => {
	assert.ok(/<html[^>]+lang="zh-CN"/.test(HTML), 'html 必须有 lang（WCAG 3.1.1）');

	const skip = HTML.match(/<a[^>]*class="skip-link"[^>]*>/);
	assert.ok(skip, '必须有跳转链接');
	assert.ok(/href="#view-home"/.test(skip[0]), '跳转链接必须指向主内容');
	assert.ok(/data-i18n="a11y\.skipToContent"/.test(skip[0]), '跳转链接文案必须可本地化');
	assert.ok(/id="view-home"[^>]*tabindex="-1"/.test(HTML), '跳转目标必须可编程聚焦（tabindex="-1"）');

	const toast = HTML.match(/<div[^>]*id="toast"[^>]*>/);
	assert.ok(toast, '必须存在 toast 元素');
	assert.ok(/role="status"/.test(toast[0]), 'toast 必须是 status 角色');
	assert.ok(/aria-live="polite"/.test(toast[0]), 'toast 必须声明 aria-live');
});

/* ============== C. launcher.js 契约 ============== */

test('MR17 JS: 弹窗具备 Escape / 焦点陷阱 / 焦点返回 / 对话框语义', () => {
	assert.ok(LAUNCHER_JS.includes('function installModalKeyboard('), '必须有键盘契约安装函数');
	assert.ok(/e\.key === 'Escape'/.test(LAUNCHER_JS), 'Escape 必须能关闭弹窗');
	assert.ok(/e\.key !== 'Tab'/.test(LAUNCHER_JS), '必须有 Tab 焦点循环');
	assert.ok(/modalReturnFocus/.test(LAUNCHER_JS), '关闭后必须把焦点还给触发元素');
	assert.ok(/setAttribute\('role', 'dialog'\)/.test(LAUNCHER_JS), '弹窗必须是 dialog 角色');
	assert.ok(/setAttribute\('aria-modal', 'true'\)/.test(LAUNCHER_JS), '弹窗必须声明 aria-modal');
	// 关闭时移除监听器，避免多次打开叠加
	assert.ok(/removeEventListener\('keydown', modalKeyHandler, true\)/.test(LAUNCHER_JS), '关闭时必须卸掉键盘监听');
});

test('MR17 JS: 危险操作不默认获得焦点', () => {
	assert.ok(LAUNCHER_JS.includes('function focusModalInitial('), '必须有初始焦点选择函数');
	const block = LAUNCHER_JS.slice(LAUNCHER_JS.indexOf('function focusModalInitial('),
		LAUNCHER_JS.indexOf('function focusModalInitial(') + 900);
	assert.ok(/safe/.test(block), '必须优先选择非破坏性按钮');
	assert.ok(/ghost/.test(block), '安全按钮的识别依据应包含 ghost 样式（取消/关闭类）');
	// 打开弹窗时必须调用它
	assert.ok(/focusModalInitial\(host\)/.test(LAUNCHER_JS), 'openModal 必须调用初始焦点选择');
});

test('MR17 JS: role=button 可用 Enter/Space 激活，且不双重触发原生按钮', () => {
	const idx = LAUNCHER_JS.indexOf('role="button"');
	assert.ok(idx >= 0, '前提：页面里确实有 role=button 的元素');

	assert.ok(/closest\('\[role="button"\]'\)/.test(LAUNCHER_JS), '必须有 role=button 的键盘激活');
	assert.ok(/e\.key !== 'Enter' && e\.key !== ' '/.test(LAUNCHER_JS), '必须处理 Enter 与 Space');
	assert.ok(/el\.tagName === 'BUTTON'/.test(LAUNCHER_JS), '必须排除原生 button，避免双重触发');
	assert.ok(/el\.click\(\)/.test(LAUNCHER_JS), '键盘激活应复用 click 路径（不另写一份逻辑）');
});

/* ============== D. i18n ============== */

test('MR17 i18n: 跳转链接文案中英双语齐备，且两侧不同', () => {
	const sg = loadI18n();
	const values = {};
	for (const loc of ['zh', 'en']) {
		sg.setLocale(loc);
		const v = sg.t('a11y.skipToContent');
		assert.equal(typeof v, 'string', `${loc} 缺少 a11y.skipToContent`);
		assert.ok(v.length > 0 && v !== 'a11y.skipToContent', `${loc} 的文案为空`);
		values[loc] = v;
	}
	assert.notEqual(values.zh, values.en, '中英文案不应相同（否则英文界面会出现中文）');
});
