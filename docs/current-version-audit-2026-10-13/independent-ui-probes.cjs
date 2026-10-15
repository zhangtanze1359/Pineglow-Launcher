'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const root = process.argv[2] || 'D:\\松光启动器';
const source = fs.readFileSync(path.join(root, 'ui/js/launcher.js'), 'utf8');
function extract(start, end) {
  const a = source.indexOf(start);
  const b = source.indexOf(end, a + start.length);
  if (a < 0 || b < 0) throw new Error('Cannot locate production function: ' + start);
  return source.slice(a, b);
}
(async () => {
  const checks = [];
  const ctx = {
    SG: { api: {
      getRemoteVersions: async () => { throw new Error('CONTROLLED_UPSTREAM_UNAVAILABLE'); },
      getDownloads: async () => [{ name: 'retained-download', statusLabel: 'downloading', progress: 20 }]
    } },
    t: key => key, esc: x => String(x == null ? '' : x), dlVersionFilter: 'release'
  };
  vm.createContext(ctx);
  vm.runInContext(extract('function versionRowHtml(', 'function bindDownloads('), ctx);
  const html = await ctx.renderDownloads();
  checks.push({id:'CV-04', expected:'request failure distinguished from successful empty list',
    failedAsEmpty:html.includes('dl.noVersions'), retainedTask:html.includes('retained-download'),
    status: !html.includes('dl.noVersions') && html.includes('retained-download') ? 'PASS' : 'FAIL'});
  const news = { NEWS_META: {validatedAt:'0001-01-01T00:00:00+00:00'}, t:key=>key };
  vm.createContext(news);
  vm.runInContext(extract('function fmtDate(', 'function fmtPlayTime(') +
    extract('function newsValidatedText(', 'function newsList('), news);
  const text = news.newsValidatedText();
  checks.push({id:'CV-05', expected:'unset validation timestamp displayed as unknown', actual:text,
    status:text === 'news.unknownTime' ? 'PASS' : 'FAIL'});
  console.log(JSON.stringify({sourceRoot:root,checks},null,2));
  process.exitCode = checks.some(c=>c.status==='FAIL') ? 1 : 0;
})().catch(e=>{console.error(e);process.exitCode=2;});
