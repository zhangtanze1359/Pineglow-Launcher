#!/usr/bin/env node
/*
 * check-release-consistency.cjs —— 版本号 / 发布说明 / tag 三者的一致性检查。
 *
 * 由来（2026-11-17 retro 第 4 条）：
 *   主线 csproj 写 <Version>1.0.0</Version>，release-notes 里已经有 v1.0.1..v1.0.3，
 *   发布分支上还有 v1.0.4；三个来源没有任何机器校验。
 *   做 v1.0.4 时任务计划照"主线版本"的字面写了「1.0.0 → 1.0.4」，
 *   而发布分支的基座其实是 1.0.3 —— 靠人眼才发现。
 *
 * 判定（只咬住能确定错的那一类）：
 *   - csproj 版本有 release-notes 说明            → OK
 *   - csproj 版本**没有**说明，且比最新说明还新    → FAIL（发了版却没写说明）
 *   - csproj 版本落后于最新说明                    → WARN（主线落后发布线是常态，但必须说出口：
 *                                                   发布要从 tag 切，不要照主线版本号推理）
 *
 * 用法：
 *   node scripts/check-release-consistency.cjs
 *   node scripts/check-release-consistency.cjs --json
 *
 * 退出码：0 一致（可有 WARN）/ 1 不一致。
 */

'use strict';

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const ROOT = path.resolve(__dirname, '..');
const CSPROJ = path.join(ROOT, 'dev', 'PineglowLauncher', 'Pineglow.Launcher.csproj');
const NOTES_DIR = path.join(ROOT, 'release-notes');

/** 发布说明从哪个版本开始必须随 tag 同行（v1.0.0 时代仓库里还没有 release-notes/）。 */
const NOTES_REQUIRED_SINCE = [1, 0, 1];
const notesSinceLabel = () => 'v' + NOTES_REQUIRED_SINCE.join('.');

function parseVersion(text) {
  const parts = String(text).trim().replace(/^v/i, '').split('.');
  if (parts.length !== 3 || parts.some((p) => !/^\d+$/.test(p))) return null;
  return parts.map(Number);
}

function compare(a, b) {
  for (let i = 0; i < 3; i++) {
    if (a[i] !== b[i]) return a[i] - b[i];
  }
  return 0;
}

function readCsprojVersion() {
  if (!fs.existsSync(CSPROJ)) return { error: '找不到 csproj：' + path.relative(ROOT, CSPROJ) };
  const text = fs.readFileSync(CSPROJ, 'utf8');
  // 只看 <Version>1.0.4</Version>，不碰 AssemblyVersion/FileVersion/PackageVersion
  const m = text.match(/<Version>([^<]+)<\/Version>/);
  if (!m) return { error: 'csproj 里没有 <Version> 元素' };
  const parsed = parseVersion(m[1]);
  return parsed ? { raw: m[1].trim(), parsed } : { error: 'csproj 的 <Version> 不是合法三段版本号：' + m[1] };
}

function readReleaseNotes() {
  if (!fs.existsSync(NOTES_DIR)) return [];
  return fs
    .readdirSync(NOTES_DIR)
    .filter((f) => /^v[\d.]+\.[^.]+md$/.test(f) || /^v[\d.]+\.[md]*$/.test(f))
    .filter((f) => /^v\d+(\.\d+)*\.md$/.test(f))
    .map((f) => ({ file: f, parsed: parseVersion(f.replace(/\.md$/, '')) }))
    .filter((n) => n.parsed !== null)
    .sort((a, b) => compare(a.parsed, b.parsed));
}

function readTags() {
  const r = spawnSync('git', ['tag', '--list', 'v*'], { cwd: ROOT, encoding: 'utf8', windowsHide: true });
  if (r.error || r.status !== 0) return [];
  return (r.stdout || '')
    .split(/\r?\n/)
    .map((t) => t.trim())
    .filter(Boolean)
    .map((raw) => ({ raw, parsed: parseVersion(raw.replace(/^v/, '')) }))
    .filter((t) => t.parsed !== null)
    .sort((a, b) => compare(a.parsed, b.parsed));
}

/**
 * tag 自带的发布说明要在 **tag 自己的树里**查，不看当前分支的工作树：
 * 发布说明常常只在发布分支上（main 要等发布时合回），用工作树查会误报。
 */
function tagHasOwnNotes(tag) {
  const r = spawnSync('git', ['cat-file', '-e', tag + ':release-notes/' + tag + '.md'], {
    cwd: ROOT,
    encoding: 'utf8',
    windowsHide: true,
  });
  return !r.error && r.status === 0;
}

function main() {
  const json = process.argv.includes('--json');
  const problems = [];
  const warnings = [];

  const version = readCsprojVersion();
  if (version.error) {
    problems.push(version.error);
  }

  const notes = readReleaseNotes();
  if (!notes.length) {
    problems.push('release-notes/ 里没有任何版本说明');
  }

  const latestNote = notes.length ? notes[notes.length - 1] : null;
  const tags = readTags();
  const latestTag = tags.length ? tags[tags.length - 1].raw : null;
  const noteName = (n) => n.file.replace(/\.md$/, '');

  if (version.parsed && latestNote) {
    const hasNote = notes.some((n) => compare(n.parsed, version.parsed) === 0);
    const newest = latestNote.parsed;

    if (hasNote && compare(version.parsed, newest) === 0) {
      // 一致
    } else if (compare(version.parsed, newest) > 0) {
      problems.push(
        `csproj 版本 v${version.raw} 比最新发布说明 ${noteName(latestNote)} 还新，但缺 release-notes/v${version.raw}.md`
      );
    } else if (!hasNote) {
      // 落后于发布线且当前版本没有说明：这在本仓库是常态（主线长期停在发布的某个旧点），
      // 但必须说出口，否则"主线版本号"会被当成发布版本。
      warnings.push(
        `当前分支 csproj 是 v${version.raw}，最新发布说明是 ${noteName(latestNote)}：` +
          '发布要从 tag 切分支，不要照当前 <Version> 推理（v1.0.4 就踩在这上面：计划写了 1.0.0→1.0.4，基座其实是 1.0.3）'
      );
    }
  }

  // 每个 tag 都必须带着自己的 release-notes（在它自己的树里查，不受当前分支影响）。
  // 从 NOTES_REQUIRED_SINCE 起算：v1.0.0 发布时仓库里还没有 release-notes/ 目录，
  // 那是一次性的历史缺口，不为它改历史（只记下来）。
  for (const tag of tags) {
    if (!tagHasOwnNotes(tag.raw)) {
      if (compare(tag.parsed, NOTES_REQUIRED_SINCE) < 0) {
        warnings.push(`${tag.raw} 没有 release-notes/${tag.raw}.md（早于 ${notesSinceLabel()}，历史缺口，不追溯）`);
      } else {
        problems.push(`${tag.raw} 这个 tag 的树里没有 release-notes/${tag.raw}.md`);
      }
    }
  }

  const result = {
    csprojVersion: version.raw || null,
    latestNote: latestNote ? latestNote.file : null,
    latestTag: latestTag || null,
    problems,
    warnings,
  };

  if (json) {
    console.log(JSON.stringify(result, null, 2));
  } else {
    console.log('[release-consistency] csproj=' + (result.csprojVersion || '?') +
      ' · 最新说明=' + (result.latestNote || '无') + ' · 最新 tag=' + (result.latestTag || '无'));
    for (const w of warnings) console.log('  ⚠ ' + w);
    for (const p of problems) console.log('  ✘ ' + p);
    if (!problems.length) console.log('  OK：版本号与发布说明一致');
  }

  process.exit(problems.length ? 1 : 0);
}

main();
