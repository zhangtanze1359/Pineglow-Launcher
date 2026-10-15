'use strict';
const fs=require('fs'),vm=require('vm');
const s=fs.readFileSync('D:/松光启动器/scripts/verify-ui-pages-live.cjs','utf8');
const a=s.indexOf('const UPSTREAM_NETWORK_EVIDENCE');
const b=s.indexOf('/* ---------------------------------------------------------------- 报告脱敏',a);
if(a<0||b<0)throw new Error('Production classification block missing');
const ctx={};vm.createContext(ctx);vm.runInContext(s.slice(a,b),ctx);
const message='无法连接 Minecraft 版本下载源，已自动尝试官方和镜像地址。请检查网络后重试。';
const escaped=JSON.stringify({error:message}).replace(/[\u007f-\uffff]/g,c=>'\\u'+c.charCodeAt(0).toString(16).padStart(4,'0'));
const cases=[
  {id:'dotnet-escaped-chinese-network-error',body:escaped,expected:true},
  {id:'windows-localized-dns-error',body:JSON.stringify({error:'不知道这样的主机。 (api.modrinth.com:443)'}),expected:true},
  {id:'unknown-internal-error',body:JSON.stringify({error:'内部数据结构异常'}),expected:false}
];
const checks=cases.map(c=>{const actual=ctx.evaluateFailureBody(c.body).upstreamNetwork;return{id:c.id,expected:c.expected,actual,status:actual===c.expected?'PASS':'FAIL'};});
console.log(JSON.stringify({scope:'actual production classifier with serializer-shaped bodies',checks},null,2));
process.exitCode=checks.some(c=>c.status==='FAIL')?1:0;
