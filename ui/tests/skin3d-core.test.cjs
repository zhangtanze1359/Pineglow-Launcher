'use strict';
const assert = require('node:assert/strict');
const core = require('../js/skin3d-core.js');

function test(name, fn) { fn(); process.stdout.write(`PASS ${name}\n`); }

test('Classic and Slim expose independent modern left limbs and every overlay', () => {
	for (const model of ['classic', 'slim']) {
		const map = core.regions(model, false);
		for (const key of ['bodyOverlay','rarmOverlay','larmOverlay','rlegOverlay','llegOverlay']) assert.ok(map[key], `${model}.${key}`);
		assert.notDeepEqual(map.larm, map.rarm); assert.notDeepEqual(map.lleg, map.rleg);
	}
	assert.equal(core.maps.classic.rarm.front[2], 4);
	assert.equal(core.maps.slim.rarm.front[2], 3);
	assert.equal(core.maps.slim.larm.front[2], 3);
});

test('Legacy 64x32 mirrors limbs and disables missing overlay layers', () => {
	const map = core.regions('classic', true);
	assert.deepEqual(map.larm, map.rarm); assert.deepEqual(map.lleg, map.rleg);
	for (const key of ['bodyOverlay','rarmOverlay','larmOverlay','rlegOverlay','llegOverlay']) assert.equal(map[key], null);
});

test('Skin dimensions accept only 64x64 and 64x32', () => {
	assert.equal(core.validDimensions(64,64), true); assert.equal(core.validDimensions(64,32), true);
	assert.equal(core.validDimensions(32,64), false); assert.equal(core.validDimensions(128,128), false);
});

test('RequestGate makes the last of 100 switches win and rejects callbacks after dispose', () => {
	const gate = new core.RequestGate(); let first;
	for (let i=0;i<100;i++) { const seq=gate.next(); if(i===0) first=seq; }
	assert.equal(gate.accepts(first), false); assert.equal(gate.accepts(100), true);
	gate.dispose(); assert.equal(gate.accepts(100), false);
});

test('Model normalization defaults unknown values to classic', () => {
	assert.equal(core.normalizeModel('SLIM'),'slim'); assert.equal(core.normalizeModel('alex'),'classic'); assert.equal(core.normalizeModel(null),'classic');
});
