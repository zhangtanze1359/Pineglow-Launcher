(function (root, factory) {
	var api = factory();
	if (typeof module === 'object' && module.exports) module.exports = api;
	root.Skin3DCore = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
	'use strict';

	function part(top, bottom, right, front, left, back) {
		return { top: top, bottom: bottom, right: right, front: front, left: left, back: back };
	}
	function layout(x, y, w, h, d) {
		return part([x + d, y, w, d], [x + d + w, y, w, d], [x, y + d, d, h],
			[x + d, y + d, w, h], [x + d + w, y + d, d, h], [x + d + w + d, y + d, w, h]);
	}

	var classic = {
		head: layout(0, 0, 8, 8, 8), hat: layout(32, 0, 8, 8, 8),
		body: layout(16, 16, 8, 12, 4), bodyOverlay: layout(16, 32, 8, 12, 4),
		rarm: layout(40, 16, 4, 12, 4), rarmOverlay: layout(40, 32, 4, 12, 4),
		larm: layout(32, 48, 4, 12, 4), larmOverlay: layout(48, 48, 4, 12, 4),
		rleg: layout(0, 16, 4, 12, 4), rlegOverlay: layout(0, 32, 4, 12, 4),
		lleg: layout(16, 48, 4, 12, 4), llegOverlay: layout(0, 48, 4, 12, 4)
	};
	var slim = Object.assign({}, classic, {
		rarm: layout(40, 16, 3, 12, 4), rarmOverlay: layout(40, 32, 3, 12, 4),
		larm: layout(32, 48, 3, 12, 4), larmOverlay: layout(48, 48, 3, 12, 4)
	});

	function normalizeModel(model) { return String(model || '').toLowerCase() === 'slim' ? 'slim' : 'classic'; }
	function validDimensions(width, height) { return width === 64 && (height === 64 || height === 32); }
	function regions(model, legacy) {
		var value = normalizeModel(model) === 'slim' ? slim : classic;
		if (!legacy) return value;
		return Object.assign({}, value, {
			larm: value.rarm, lleg: value.rleg,
			bodyOverlay: null, rarmOverlay: null, larmOverlay: null,
			rlegOverlay: null, llegOverlay: null
		});
	}
	function RequestGate() { this.sequence = 0; this.disposed = false; }
	RequestGate.prototype.next = function () { return ++this.sequence; };
	RequestGate.prototype.accepts = function (sequence) { return !this.disposed && sequence === this.sequence; };
	RequestGate.prototype.dispose = function () { this.disposed = true; this.sequence++; };

	return { layout: layout, maps: { classic: classic, slim: slim }, normalizeModel: normalizeModel,
		validDimensions: validDimensions, regions: regions, RequestGate: RequestGate };
});
