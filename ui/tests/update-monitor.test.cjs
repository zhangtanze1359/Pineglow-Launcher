'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../js/update-monitor.js'), 'utf8');
const newer = version => ({ enabled: true, updateAvailable: true, currentVersion: '1.0.0', latestVersion: version, notes: '修复启动\n新增自动提醒' });
const flush = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
function setup(check) {
	let now = 0, id = 0, calls = 0;
	const tasks = new Map(), events = new Map(), shown = [], results = [];
	const target = {
		hidden: false,
		addEventListener(k, fn) { events.set(k, fn); },
		removeEventListener(k) { events.delete(k); },
	};
	const clock = {
		setTimeout(fn, ms) { tasks.set(++id, { fn, due: now + ms }); return id; },
		setInterval(fn, ms) { tasks.set(++id, { fn, due: now + ms, interval: ms }); return id; },
		clearTimeout(id) { tasks.delete(id); }, clearInterval(id) { tasks.delete(id); },
	};
	const context = { window: { SG: {} } };
	context.SG = context.window.SG;
	vm.runInNewContext(source, context);
	let available = true;
	const monitor = context.SG.createUpdateMonitor({
		clock, now: () => now, document: target, window: target,
		check: async () => { calls++; return check ? check(calls) : newer('1.1.0'); },
		onResult: result => results.push(result),
		notify: result => { if (!available) return false; shown.push(result); return true; },
	});
	return {
		monitor, target, shown, results, events,
		calls: () => calls,
		busy(value) { available = !value; },
		async event(name) { events.get(name)?.(); await flush(); },
		async advance(ms) {
			const end = now + ms;
			while (true) {
				const next = [...tasks].filter(([, t]) => t.due <= end).sort((a, b) => a[1].due - b[1].due)[0];
				if (!next) break;
				const [key, task] = next; now = task.due;
				if (task.interval) task.due += task.interval; else tasks.delete(key);
				task.fn(); await flush();
			}
			now = end; await flush();
		},
	};
}
test('StartupAndPeriodicChecks_NotifyOncePerVersionWithReleaseNotes', async () => {
	const s = setup(); s.monitor.start(); s.monitor.start();
	await s.advance(2999); assert.equal(s.calls(), 0);
	await s.advance(1); assert.equal(s.calls(), 1); assert.equal(s.shown.length, 1);
	assert.equal(s.shown[0].notes, '修复启动\n新增自动提醒');
	await s.advance(600000); assert.equal(s.calls(), 2); assert.equal(s.shown.length, 1);
});
test('NewVersionAfterDismissal_IsNotified', async () => {
	const s = setup(call => newer(call === 1 ? '1.1.0' : '1.2.0')); s.monitor.start();
	await s.advance(3000); await s.advance(600000);
	assert.deepEqual(s.shown.map(r => r.latestVersion), ['1.1.0', '1.2.0']);
});
test('HiddenWindow_SkipsChecksAndChecksOnForegroundWithThrottle', async () => {
	const s = setup(); s.target.hidden = true; s.monitor.start();
	await s.advance(600000); assert.equal(s.calls(), 0);
	 s.target.hidden = false; await s.event('visibilitychange'); assert.equal(s.calls(), 1);
	await s.event('focus'); assert.equal(s.calls(), 1);
	await s.advance(60000); await s.event('focus'); assert.equal(s.calls(), 2);
});
test('NetworkFailure_RetriesWithoutFalseNotification', async () => {
	const s = setup(call => { if (call === 1) throw new Error('offline'); return newer('1.1.0'); }); s.monitor.start();
	await s.advance(3000); assert.equal(s.shown.length, 0);
	await s.advance(60000); await s.event('online'); assert.equal(s.shown.length, 1);
});
test('BusinessDialog_DefersNotificationWithoutReplacingIt', async () => {
	const s = setup(); s.busy(true); s.monitor.start(); await s.advance(3000);
	assert.equal(s.shown.length, 0); s.busy(false); await s.advance(5000);
	assert.equal(s.shown.length, 1); assert.equal(s.calls(), 1);
});
test('ConcurrentManualAndAutomaticChecks_ShareOneRequest', async () => {
	let resolve; const s = setup(() => new Promise(r => { resolve = r; })); s.monitor.start();
	await s.advance(3000); const manual = s.monitor.check({ notify: false });
	assert.equal(s.calls(), 1); resolve(newer('1.1.0')); await manual;
	assert.equal(s.results.length, 1); assert.equal(s.shown.length, 1);
});
test('ManualCheck_DoesNotOpenAutomaticDialogAndRetainsErrors', async () => {
	const s = setup(); s.monitor.start(); await s.monitor.check({ notify: false });
	assert.equal(s.shown.length, 0); await s.advance(600000); assert.equal(s.shown.length, 0);
	const failed = setup(() => { throw new Error('offline'); });
	await assert.rejects(failed.monitor.check({ notify: false }), /offline/);
});
test('UpToDateDisabledAndErrorResponses_DoNotNotify', async () => {
	for (const result of [{ enabled: false }, { enabled: true, updateAvailable: false }, { ...newer('2.0.0'), error: 'timeout' }]) {
		const s = setup(() => result); s.monitor.start(); await s.advance(3000); assert.equal(s.shown.length, 0);
	}
});
test('Stop_IgnoresLateResponsesAndRemovesTimersAndListeners', async () => {
	let resolve; const s = setup(() => new Promise(r => { resolve = r; })); s.monitor.start();
	await s.advance(3000); s.monitor.stop(); resolve(newer('1.1.0')); await flush();
	await s.advance(1200000); assert.equal(s.calls(), 1); assert.equal(s.shown.length, 0);
	assert.equal(s.results.length, 0); assert.equal(s.events.size, 0);
});
