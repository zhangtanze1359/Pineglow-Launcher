/* ============== 工具函数 ============== */
// 极简 HTML 解析器：把 html 字符串解析为子树并 append 到 targetEl
// 用于 innerHTML 重新赋值与 makeMockContainer
const VOID_TAGS = new Set(['br', 'hr', 'img', 'input', 'meta', 'link']);
function parseInto(targetEl, html) {
	const document = targetEl.ownerDocument;
	const tokenRe = /<\/?([a-zA-Z][a-zA-Z0-9-]*)\s*([^>]*?)\/?>|([^<]+)|<!--[\s\S]*?-->/g;
	const attrRe = /([a-zA-Z_:][\w:-]*)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+)))?/g;
	const stack = [targetEl];
	html.replace(tokenRe, (match, tagName, attrs, text) => {
		if (text !== undefined) {
			const cur = stack[stack.length - 1];
			const t = document.createTextNode(text);
			cur.appendChild(t);
			return '';
		}
		if (match.startsWith('<!--')) return '';
		const isClose = match.startsWith('</');
		if (isClose) {
			if (stack.length > 1) stack.pop();
			return '';
		}
		const isSelfClose = match.endsWith('/>') || VOID_TAGS.has(tagName.toLowerCase());
		const el = document.createElement(tagName);
		if (attrs) {
			let m;
			attrRe.lastIndex = 0;
			while ((m = attrRe.exec(attrs)) !== null) {
				const k = m[1];
				const v = m[2] !== undefined ? m[2] : (m[3] !== undefined ? m[3] : (m[4] !== undefined ? m[4] : ''));
				el.setAttribute(k, v);
			}
		}
		const cur = stack[stack.length - 1];
		cur.appendChild(el);
		if (!isSelfClose) stack.push(el);
		return '';
	});
}
function parseHtml(document, html) {
	const root = document.createElement('div');
	parseInto(root, html);
	return root;
}
function makeMockContainer(document, html) {
	const root = parseHtml(document, html);
	document.body.appendChild(root);
	return root;
}

// 调试：打印 HTML 头部 + 解析后根的子元素（默认关闭）
function debugParse(_document, _html) { /* no-op */ }
function waitMicro() { return new Promise(r => setTimeout(r, 10)); }
