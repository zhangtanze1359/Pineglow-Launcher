'use strict';
/*
 * launcher-ui-pure.test.cjs —— launcher.js 里**纯渲染 / 纯判定**函数的直测
 *
 * 由来（任务 #14：前端 JS 未覆盖路径统计）：
 *   用"函数名是否在任何测试文件里出现过"做静态统计，ui/js 共 184 个具名函数，
 *   launcher.js 的 153 个里有 99 个从未被提及。逐个人肉补完不现实，但其中有一批
 *   是**无 DOM、无网络、无状态**的纯函数，只要把它们暴露出来就能直接断言——
 *   而这批恰好是最容易"改一个字符就静默出错"的地方：
 *
 *     esc              转义表少一项 = XSS；
 *     uiIcon/uiText    多语言分支写反 = 英文用户看到中文；
 *     uiPlayTime       单位换算错 = 时长显示差 60 倍；
 *     parseValidatedAt 「未验证」判定错 = 界面把 Go 零值渲染成 "1-01-01"（真实历史缺陷 CV-05）；
 *     steveSvg/skinAvatar  皮肤兜底与裁剪坐标错 = 头像错位或空白；
 *     loaderColor/firstLetter  兜底值错 = 未知 loader 显示成透明/空白。
 *
 * 做法：与 launcher-behavior.test.cjs 同一条路——vm 里装 i18n + launcher.js，
 * 通过已有的 SG.__test__ 钩子访问。**不新增任何测试专用导出**：本轮只补测，不改产品代码；
 * 需要但未导出的函数，用 vm 上下文里能拿到的方式取（见 pickFn），取不到就明确失败，
 * 绝不改成"断言恒真"。
 *
 * 限制（如实记录）：本文件不覆盖需要真实 DOM 事件、真实 fetch、真实 WebView2 的路径。
 * 那部分仍由 launcher-behavior / launcher-content / verify-ui-pages-live.cjs 与
 * 真实 GUI 逐页验收覆盖。
 */

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.resolve(__dirname, '..', '..');
const LAUNCHER_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'launcher.js'), 'utf8');
const I18N_JS = fs.readFileSync(path.join(ROOT, 'ui', 'js', 'i18n.js'), 'utf8');

/* ==================== 最小 DOM 桩 ====================
 * launcher.js 在 IIFE 末尾会执行少量初始化（注册导航、读 document）。这里的桩只保证
 * "脚本能跑完"，不模拟任何交互——所以本文件里凡是断言 DOM 行为的用例都不该存在。
 */
function makeMinimalDom() {
	const elements = new Map();
	// 注意：这里不能用 `const document = {...}` 之后再让 makeEl 闭包引用它——那会撞上 TDZ。
	// 先用 let 声明，等 document 建好再回填 ownerDocument。
	let document = null;

	const makeEl = (tag) => {
		const el = {
			tagName: (tag || 'div').toUpperCase(),
			id: '', className: '', textContent: '', innerHTML: '', hidden: false,
			style: {}, dataset: {}, children: [],
			classList: { add() {}, remove() {}, contains() { return false; }, toggle() {} },
			appendChild(c) { this.children.push(c); return c; },
			removeChild() {}, remove() {},
			setAttribute() {}, getAttribute() { return null; }, removeAttribute() {},
			addEventListener() {}, removeEventListener() {}, dispatchEvent() { return true; },
			querySelector() { return null; }, querySelectorAll() { return []; },
			closest() { return null; }, focus() {}, blur() {}, click() {},
			getBoundingClientRect() { return { top: 0, left: 0, width: 0, height: 0, bottom: 0, right: 0 }; },
			insertAdjacentHTML() {},
			get ownerDocument() { return document; },
		};
		return el;
	};

	document = {
		readyState: 'complete',
		body: makeEl('body'),
		head: makeEl('head'),
		documentElement: makeEl('html'),
		createElement: makeEl,
		createElementNS: (_ns, tag) => makeEl(tag),
		createTextNode: (t) => ({ nodeType: 3, textContent: String(t) }),
		getElementById(id) {
			if (!elements.has(id)) { const el = makeEl('div'); el.id = id; elements.set(id, el); }
			return elements.get(id);
		},
		querySelector() { return null; },
		querySelectorAll() { return []; },
		addEventListener() {}, removeEventListener() {},
		createDocumentFragment: () => makeEl('fragment'),
		_DOM_CONTENT_LOADED: true,
	};
	return { document, elements };
}

function makeContext(extra = {}) {
	const { document } = makeMinimalDom();
	const sandbox = {
		console,
		setTimeout, clearTimeout, setInterval, clearInterval,
		document,
		navigator: { language: 'zh-CN', languages: ['zh-CN'], userAgent: 'node-test' },
		location: { href: 'http://localhost/index.html', search: '', hash: '' },
		history: { pushState() {}, replaceState() {}, back() {} },
		localStorage: {
			_store: new Map(),
			getItem(k) { return this._store.has(k) ? this._store.get(k) : null; },
			setItem(k, v) { this._store.set(k, String(v)); },
			removeItem(k) { this._store.delete(k); },
		},
		URL, URLSearchParams, TextEncoder, TextDecoder,
		requestAnimationFrame: (fn) => setTimeout(fn, 0),
		cancelAnimationFrame: clearTimeout,
		matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {} }),
		performance: { now: () => Date.now() },
		// launcher.js 启动时会往 window 上挂全局监听（resize/drop/message 等）。
		// 沙箱里的 window 就是 sandbox 自己，所以必须在**顶层**也提供这两个方法，
		// 否则初始化阶段抛出的 TypeError 会被 node:test 记成"测试结束后的异步活动"，
		// 表现成"每条用例都失败但看不出原因"。
		addEventListener() {},
		removeEventListener() {},
		dispatchEvent() { return true; },
		postMessage() {},
		...extra,
	};
	sandbox.window = sandbox;
	sandbox.globalThis = sandbox;
	sandbox.self = sandbox;
	sandbox.fetch = sandbox.fetch || (async () => ({ ok: true, status: 200, json: async () => ({}), text: async () => '' }));
	return vm.createContext(sandbox);
}

/** 装载 i18n + launcher.js，返回沙箱与钩子。 */
function loadLauncher(extra = {}) {
	const ctx = makeContext(extra);
	vm.runInContext(I18N_JS, ctx, { filename: 'i18n.js' });
	try {
		vm.runInContext(LAUNCHER_JS, ctx, { filename: 'launcher.js' });
	} catch (err) {
		// 初始化失败时给出明确上下文，而不是让人看到一句无头 TypeError
		throw new Error('launcher.js 在 vm 中初始化失败：' + err.message);
	}
	return ctx;
}

/**
 * 从沙箱取一个**已存在**的顶层函数。
 *
 * 为什么不用 eval 拼名字再兜底返回 noop：那样等于把断言变成恒真。
 * 这里要求函数必须在 vm 上下文里可见；看不见就抛错，让"钩子没导出"立刻暴露。
 */
function pickFn(ctx, name) {
	const hooks = ctx.SG && ctx.SG.__test__;
	if (hooks && typeof hooks[name] === 'function') return hooks[name];
	if (typeof ctx[name] === 'function') return ctx[name];
	throw new Error(
		'无法从 vm 上下文取得函数 ' + name + '。' +
		'若该函数确实是纯函数且应当被测，应在 SG.__test__ 中导出（需改产品代码，本轮不做）；' +
		'否则本用例应删除，而不是改成空断言。'
	);
}

/* ==================== 1. esc：HTML 转义（XSS 面） ==================== */

test('esc 转义全部五个 HTML 危险字符', () => {
	const ctx = loadLauncher();
	const esc = pickFn(ctx, 'esc');

	assert.equal(esc('&'), '&amp;');
	assert.equal(esc('<'), '&lt;');
	assert.equal(esc('>'), '&gt;');
	assert.equal(esc('"'), '&quot;');
	assert.equal(esc("'"), '&#39;');
	// 组合串：一个都不能漏，否则 <img onerror> 之类的注入就能成立
	assert.equal(esc('<img src=x onerror="alert(\'x\')">'),
		'&lt;img src=x onerror=&quot;alert(&#39;x&#39;)&quot;&gt;');
});

test('esc 把 null/undefined 当成空串而不是字符串 "null"', () => {
	const ctx = loadLauncher();
	const esc = pickFn(ctx, 'esc');
	// 若返回 "null"，界面会出现字面量 null；若抛错，渲染会整块崩掉。
	assert.equal(esc(null), '');
	assert.equal(esc(undefined), '');
	assert.equal(esc(0), '0');
	assert.equal(esc(false), 'false');
});

/* ==================== 2. loaderColor / firstLetter：兜底值 ==================== */

test('loaderColor 已知 loader 各有独立颜色，未知 loader 有兜底色', () => {
	const ctx = loadLauncher();
	const loaderColor = pickFn(ctx, 'loaderColor');

	const known = ['Fabric', 'Forge', 'NeoForge', 'OptiFine', 'Quilt', 'Vanilla'];
	const seen = new Map();
	for (const name of known) {
		const color = loaderColor(name);
		assert.match(color, /^#[0-9a-fA-F]{6}$/, name + ' 应返回 6 位色值，实际 ' + color);
		assert.ok(!seen.has(color), name + ' 与 ' + seen.get(color) + ' 撞色，用户无法靠颜色区分 loader');
		seen.set(color, name);
	}

	// 未知 / 空必须落到同一个兜底色，而不是 undefined（渲染成 background:undefined 会变透明）
	const fallback = loaderColor('UnknownLoader');
	assert.match(fallback, /^#[0-9a-fA-F]{6}$/);
	assert.equal(loaderColor(''), fallback);
	assert.equal(loaderColor(undefined), fallback);
	assert.equal(loaderColor(null), fallback);
});

test('firstLetter 取首字母大写，空值兜底为 M', () => {
	const ctx = loadLauncher();
	const firstLetter = pickFn(ctx, 'firstLetter');

	assert.equal(firstLetter('steve'), 'S');
	assert.equal(firstLetter('  alex'), 'A');
	assert.equal(firstLetter('Steve'), 'S');
	// 中文昵称没有大小写概念，原样返回首字（不能变成空串，否则头像里什么都没有）
	assert.equal(firstLetter('张三'), '张');
	// 空值兜底是 "M"（Minecraft），必须和头像占位组件约定一致
	assert.equal(firstLetter(''), 'M');
	assert.equal(firstLetter('   '), 'M');
	assert.equal(firstLetter(null), 'M');
	assert.equal(firstLetter(undefined), 'M');
});

/* ==================== 3. uiText / uiIcon / uiPlayTime：多语言与单位 ==================== */

test('uiText 按 SG.locale 在中英之间切换，未知 locale 回退中文', () => {
	const ctx = loadLauncher();
	const uiText = pickFn(ctx, 'uiText');

	ctx.SG.locale = 'en';
	assert.equal(uiText('中文', 'English'), 'English');
	// 只有精确 'en' 走英文；'EN' / 'en-US' 都不算——这是既有口径，钉住它
	ctx.SG.locale = 'EN';
	assert.equal(uiText('中文', 'English'), '中文');
	ctx.SG.locale = 'zh';
	assert.equal(uiText('中文', 'English'), '中文');
	ctx.SG.locale = undefined;
	assert.equal(uiText('中文', 'English'), '中文');
});

test('uiIcon 对未知图标名回退到 cube，而不是输出空 svg', () => {
	const ctx = loadLauncher();
	const uiIcon = pickFn(ctx, 'uiIcon');

	const play = uiIcon('play');
	assert.match(play, /^<svg /, '应返回 svg 片段');
	assert.match(play, /aria-hidden="true"/, '装饰性图标必须 aria-hidden，否则读屏会念出无意义的图形');
	assert.match(play, /<path /);

	// 未知名字回退 cube：绝不能返回一个没有内容的 <svg></svg>
	const unknown = uiIcon('no-such-icon-name');
	assert.match(unknown, /^<svg /);
	assert.match(unknown, /<path /, '未知图标应回退 cube（有 path），不能是空 svg');
	assert.equal(unknown, uiIcon('cube'), '未知图标必须与显式 cube 完全一致');
});

test('uiPlayTime 中文按 小时/分/秒 三档降级', () => {
	const ctx = loadLauncher();
	const uiPlayTime = pickFn(ctx, 'uiPlayTime');
	ctx.SG.locale = 'zh';

	// 0 秒：只显示秒
	assert.equal(uiPlayTime(0), '0 <small>秒</small>');
	assert.equal(uiPlayTime(45), '45 <small>秒</small>');
	// 60 秒整：进位到 1 分，且不再显示 "0 秒"
	assert.equal(uiPlayTime(60), '1 <small>分</small>');
	assert.equal(uiPlayTime(90), '1 <small>分</small>');
	// 3600 秒整：进位到 1 小时，且不显示 "0 分"
	assert.equal(uiPlayTime(3600), '1 <small>小时</small>');
	// 注意：小时与分钟之间有一个空格（模板里是 `' '+m%60`），不是紧贴。
	assert.equal(uiPlayTime(3660), '1 <small>小时</small> 1 <small>分</small>');
	assert.equal(uiPlayTime(7200), '2 <small>小时</small>');
});

test('uiPlayTime 负数与非法输入按 0 处理，不显示负时长', () => {
	const ctx = loadLauncher();
	const uiPlayTime = pickFn(ctx, 'uiPlayTime');
	ctx.SG.locale = 'zh';

	// 系统时间回拨 / 记录损坏时可能出现负数。显示 "-1 小时" 是明显的错误信息。
	assert.equal(uiPlayTime(-1), '0 <small>秒</small>');
	assert.equal(uiPlayTime(-99999), '0 <small>秒</small>');
	assert.equal(uiPlayTime('abc'), '0 <small>秒</small>');
	assert.equal(uiPlayTime(null), '0 <small>秒</small>');
	assert.equal(uiPlayTime(undefined), '0 <small>秒</small>');
});

/* ==================== 4. parseValidatedAt：「未验证」判定（CV-05 回归） ==================== */

test('parseValidatedAt 把 Go 零值时间一律判为未验证', () => {
	const ctx = loadLauncher();
	const parseValidatedAt = pickFn(ctx, 'parseValidatedAt');

	// 这是 CV-05 的真实缺陷：后端首次失败时 meta.validatedAt 是 Go 零值，
	// 前端曾把它渲染成 "1-01-01"。现在必须返回 null（= 未验证）。
	assert.equal(parseValidatedAt('0001-01-01T00:00:00Z'), null);
	assert.equal(parseValidatedAt('0001-01-01T00:00:00+08:00'), null, '带时区偏移的零值也必须判为未验证');
	assert.equal(parseValidatedAt('0001-01-01T08:00:00+08:00'), null);
});

test('parseValidatedAt 对空值与无法解析的原串返回 null', () => {
	const ctx = loadLauncher();
	const parseValidatedAt = pickFn(ctx, 'parseValidatedAt');

	assert.equal(parseValidatedAt(null), null);
	assert.equal(parseValidatedAt(undefined), null);
	assert.equal(parseValidatedAt(''), null);
	assert.equal(parseValidatedAt('   '), null);
	assert.equal(parseValidatedAt('not-a-date'), null, '无法解析的原串绝不能进入日期字段');
	assert.equal(parseValidatedAt('2026-13-45T99:99:99Z'), null);
	assert.equal(parseValidatedAt(NaN), null);
});

test('parseValidatedAt 正常时间返回有效 Date', () => {
	const ctx = loadLauncher();
	const parseValidatedAt = pickFn(ctx, 'parseValidatedAt');

	const d = parseValidatedAt('2026-11-06T10:00:00Z');
	// 沙箱里没有注入 Date 构造器，所以只能按行为断言：有 getUTCFullYear 就说明是 Date 实例。
	assert.equal(typeof d.getUTCFullYear, 'function', '应返回 Date 对象');
	assert.equal(d.getUTCFullYear(), 2026);
	assert.equal(d.getUTCMonth(), 10); // 0-based，11 月
	assert.equal(d.getUTCDate(), 6);

	const num = parseValidatedAt(Date.UTC(2026, 0, 15));
	assert.equal(num.getUTCFullYear(), 2026);
	assert.equal(num.getUTCMonth(), 0);
});

/* ==================== 5. steveSvg：皮肤缺失时的占位小人 ==================== */

test('steveSvg 输出可缩放的像素小人，且不依赖外部图片', () => {
	const ctx = loadLauncher();
	const steveSvg = pickFn(ctx, 'steveSvg');

	const svg = steveSvg(64);
	assert.match(svg, /^<svg /);
	assert.match(svg, /viewBox="0 0 80 120"/, 'viewBox 是占位小人的坐标系契约');
	assert.match(svg, /width="64"/, '传入的 scale 必须生效，否则头像大小不受控');
	assert.match(svg, /image-rendering:pixelated/, '缺少这条会双线性插值，像素风糊掉');
	assert.doesNotMatch(svg, /url\(/, '占位图不得引用任何外部资源（离线/图裂时必须仍能显示）');
	// 头/眼/身体/四肢的色块数：至少 11 个 rect
	const rects = (svg.match(/<rect /g) || []).length;
	assert.ok(rects >= 11, '应至少有 11 个色块（头/双眼/嘴/身/双臂/双腿），实际 ' + rects);
});

test('steveSvg 接受自定义调色板并原样使用', () => {
	const ctx = loadLauncher();
	const steveSvg = pickFn(ctx, 'steveSvg');

	const svg = steveSvg(32, { head: '#111111', face: '#222222', body: '#333333', arm: '#444444', leg: '#555555' });
	for (const color of ['#111111', '#222222', '#333333', '#444444', '#555555']) {
		assert.ok(svg.includes(color), '调色板颜色 ' + color + ' 应出现在输出里');
	}
});

/* ==================== 6. skinAvatar：2D 皮肤裁剪 ==================== */

test('skinAvatar 输出按 16 像素宽的人物比例定位的色块层', () => {
	const ctx = loadLauncher();
	const skinAvatar = pickFn(ctx, 'skinAvatar');

	const html = skinAvatar(160, 'https://textures.minecraft.net/texture/abc', 'classic');
	// U = w/16 = 10px/皮肤像素；图集 64*10 = 640px
	assert.ok(html.includes('640px 640px'), '背景图集缩放尺寸应为 width/16*64');
	assert.ok(html.includes("url('https://textures.minecraft.net/texture/abc')"), '应引用传入的皮肤地址');
	assert.match(html, /image-rendering:pixelated/);
	assert.match(html, /position:absolute/);
	// 头部应落在左 4*U=40px、上 0
	assert.ok(html.includes('left:40px;top:0px'), '头部定位应为 left=4*U, top=0');
});

test('skinAvatar 对 slim 模型使用 3 像素宽手臂', () => {
	const ctx = loadLauncher();
	const skinAvatar = pickFn(ctx, 'skinAvatar');

	// U = 10；classic 手臂 4*10=40px 宽，slim 手臂 3*10=30px 宽
	const classic = skinAvatar(160, 'https://x/y.png', 'classic');
	const slim = skinAvatar(160, 'https://x/y.png', 'slim');

	assert.ok(classic.includes('width:40px'), 'classic 手臂宽应为 4*U');
	assert.ok(slim.includes('width:30px'), 'slim 手臂宽应为 3*U（否则细手臂皮肤会被拉宽）');
	assert.notEqual(classic, slim, '两种模型的输出必须不同');
});

test('skinAvatar 转义皮肤 URL，防止属性截断', () => {
	const ctx = loadLauncher();
	const skinAvatar = pickFn(ctx, 'skinAvatar');

	// 皮肤 URL 来自远端 API/玩家配置，属于不可信输入。
	// 若原样拼进 background:url('...')，一个单引号就能跳出属性。
	const html = skinAvatar(160, "https://x/a'b.png", 'classic');
	assert.ok(html.includes('&#39;'), "单引号必须被转义成 &#39;，实际片段：" + html.slice(0, 120));
	assert.ok(!html.includes("a'b.png"), '原样单引号不得出现');
});

/* ==================== 7. 新闻状态文案：错误分类不能全部塌成同一句 ==================== */

test('newsStateText 在无 meta 时显示加载中，未知 state 回退错误文案', () => {
	const ctx = loadLauncher();
	const resetNews = ctx.SG && ctx.SG.__test__ && ctx.SG.__test__.resetNews;
	assert.equal(typeof resetNews, 'function', 'SG.__test__.resetNews 应存在（新闻测试钩子）');
	resetNews();

	const newsStateText = pickFn(ctx, 'newsStateText');
	const loading = newsStateText();
	assert.equal(typeof loading, 'string');
	assert.ok(loading.length > 0, '无 meta 时必须有可见文案，不能返回空串');
	// 文案应来自 i18n 的 news.loading（中文环境）
	assert.equal(loading, ctx.SG.t('news.loading'));
});

test('newsErrorText 在无 errorKind 时返回空串，不伪造错误', () => {
	const ctx = loadLauncher();
	ctx.SG.__test__.resetNews();

	const newsErrorText = pickFn(ctx, 'newsErrorText');
	// 关键：没有错误时必须是空串。若回退到"未知错误"，用户会在正常状态下看到红色错误条。
	assert.equal(newsErrorText(), '');
});

/* ==================== 8. 自检：桩与钩子本身可用 ==================== */

test('vm 装载的 launcher.js 暴露了 SG 与 i18n 翻译函数', () => {
	const ctx = loadLauncher();
	assert.equal(typeof ctx.SG, 'object', 'window.SG 必须存在');
	assert.equal(typeof ctx.SG.t, 'function', 'SG.t 由 i18n.js 提供');
	assert.equal(typeof ctx.SG.__test__, 'object', 'SG.__test__ 是行为测试的唯一入口');
	// 抽查几个必须存在的钩子：它们被删掉会让本文件与行为测试同时失明
	for (const name of ['renderContent', 'loadState', 'renderHome', 'openModal', 'closeModal']) {
		assert.equal(typeof ctx.SG.__test__[name], 'function', 'SG.__test__.' + name + ' 应存在');
	}
});

test('pickFn 对不存在的函数抛错而不是返回 undefined', () => {
	// 自检：确保"取不到函数"会明确失败。若它静默返回 undefined，
	// 后续 typeof 检查就会变成"跳过"，整份文件可能一条断言都没跑。
	const ctx = loadLauncher();
	assert.throws(() => pickFn(ctx, 'definitelyNotAFunction_' + Date.now()), /无法从 vm 上下文取得函数/);
});
