/* Online update checks: timers and notification policy, independent of page navigation. */
(function () {
'use strict';
window.SG = window.SG || {};
SG.createUpdateMonitor = function (options) {
	const clock = options.clock || window;
	const now = options.now || Date.now;
	const doc = options.document || document;
	const win = options.window || window;
	const intervalMs = 10 * 60 * 1000;
	const foregroundMs = 60 * 1000;
	let started = false, generation = 0, inflight = null, lastAttempt = -Infinity;
	let startupTimer, intervalTimer, retryTimer, pending = null;
	const notified = new Set();
	function present() {
		if (!started || doc.hidden || !pending || notified.has(pending.latestVersion)) return;
		if (options.notify(pending) !== false) {
			notified.add(pending.latestVersion);
			pending = null;
		} else if (!retryTimer) {
			retryTimer = clock.setTimeout(() => { retryTimer = null; present(); }, 5000);
		}
	}
	async function check(config) {
		if (inflight) return inflight;
		const notify = !config || config.notify !== false;
		const currentGeneration = generation;
		lastAttempt = now();
		inflight = (async () => {
			try {
				const result = await options.check();
				if (generation !== currentGeneration) return result;
				if (options.onResult) options.onResult(result);
				if (result && result.enabled && !result.error && result.updateAvailable && result.latestVersion) {
					if (notify) pending = result;
					else { notified.add(result.latestVersion); pending = null; }
					present();
				} else if (result && !result.error) pending = null;
				return result;
			} finally { inflight = null; }
		})();
		return inflight;
	}
	function automatic() {
		if (!started || doc.hidden) return;
		present();
		check().catch(() => {}); // Manual checks retain their error; automatic failures retry quietly.
	}
	function foreground() {
		if (!started || doc.hidden) return;
		present();
		if (now() - lastAttempt >= foregroundMs) automatic();
	}
	return {
		check,
		start() {
			if (started) return;
			started = true;
			startupTimer = clock.setTimeout(automatic, 3000);
			intervalTimer = clock.setInterval(automatic, intervalMs);
			doc.addEventListener('visibilitychange', foreground);
			win.addEventListener('focus', foreground);
			win.addEventListener('online', foreground);
		},
		stop() {
			started = false;
			generation++;
			clock.clearTimeout(startupTimer);
			clock.clearInterval(intervalTimer);
			clock.clearTimeout(retryTimer);
			retryTimer = null;
			pending = null;
			doc.removeEventListener('visibilitychange', foreground);
			win.removeEventListener('focus', foreground);
			win.removeEventListener('online', foreground);
		}
	};
};
})();
