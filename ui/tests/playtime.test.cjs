'use strict';
const test=require('node:test'),assert=require('node:assert/strict');
const {rangeFor,splitSessions,squarify,parseDay,summarizeParts,buildSessionTree}=require('../js/playtime.js');
test('playtime: hierarchy separates instance identities and groups actual sessions by descending date',()=>{
 const p=(id,day,n)=>({instanceId:id,instanceName:'Same name',day,seconds:n,sessionKey:id+day,startedAt:day+'T10:00:00'});
 const tree=buildSessionTree([p('a','2026-10-14',60),p('b','2026-10-15',300),p('a','2026-10-15',120)]);
 assert.deepEqual(tree.map(g=>[g.id,g.seconds,g.days.length]),[['b',300,1],['a',180,2]]);
 assert.deepEqual(tree[1].days.map(d=>d.day),['2026-10-15','2026-10-14']);
 assert.equal(tree[1].days[0].sessions[0].seconds,120);
 assert.deepEqual(buildSessionTree([]),[]);
});
test('playtime: tree conserves cross-midnight duration and counts the original session once',()=>{
 const parts=splitSessions([{instanceId:'a',instanceName:'Fabric',startedAt:new Date(2026,9,14,23,30).toISOString(),endedAt:new Date(2026,9,15,1).toISOString()}]);
 const [group]=buildSessionTree(parts);
 assert.equal(group.seconds,5400);assert.equal(group.count,1);assert.equal(group.days.length,2);
 assert.deepEqual(group.days.map(d=>[d.day,d.seconds,d.count]),[['2026-10-15',3600,1],['2026-10-14',1800,1]]);
 assert.equal(group.days.flatMap(d=>d.sessions).reduce((s,p)=>s+p.seconds,0),group.seconds);
});
test('playtime: range summary counts unique sessions, active days and clipped longest duration',()=>{
 const parts=splitSessions([
  {id:'overnight',startedAt:new Date(2026,9,14,23).toISOString(),endedAt:new Date(2026,9,15,1).toISOString()},
  {id:'day',startedAt:new Date(2026,9,15,2).toISOString(),endedAt:new Date(2026,9,15,2,30).toISOString()}
 ]);
 assert.deepEqual(summarizeParts(parts),{seconds:9000,count:2,days:2,longest:7200});
 assert.deepEqual(summarizeParts(parts.filter(p=>p.day==='2026-10-15')),{seconds:5400,count:2,days:1,longest:3600});
 assert.deepEqual(summarizeParts([]),{seconds:0,count:0,days:0,longest:0});
});
test('playtime: ISO calendar dates reject rollover and custom range preserves 120 day limit',()=>{
 assert.equal(parseDay('2026-02-30'),null);
 assert.equal(parseDay('2026-2-1'),null);
 assert.throws(()=>rangeFor('custom',new Date(),'2026-10-15','2026-10-14'),/invalidRange/);
 assert.throws(()=>rangeFor('custom',new Date(),'2026-01-01','2026-05-01'),/longRange/);
 const r=rangeFor('custom',new Date(),'2026-01-01','2026-04-30');assert.equal(r.start.getDate(),1);assert.equal(r.end.getMonth(),4);assert.equal(r.end.getDate(),1);
});
test('playtime: Monday based week and rolling range include today exactly once',()=>{
 const now=new Date(2026,9,18,15),week=rangeFor('week',now),last7=rangeFor('7',now);
 assert.equal(week.start.getDay(),1);assert.equal(week.start.getDate(),12);assert.equal(week.end.getDate(),19);
 assert.equal(+week.start,+last7.start);assert.equal(+week.end,+last7.end);
});
test('playtime: midnight sessions split actual seconds across dates without inventing history',()=>{
 const startedAt=new Date(2026,9,14,23,45).toISOString(),endedAt=new Date(2026,9,15,0,15).toISOString();
 const p=splitSessions([{instanceId:'a',startedAt,endedAt}]);
 assert.deepEqual(p.map(x=>[x.day,x.seconds]),[['2026-10-14',900],['2026-10-15',900]]);
 assert.deepEqual(splitSessions([{startedAt:null,endedAt},{startedAt,endedAt:startedAt},{startedAt:'invalid',endedAt}]),[]);
});
test('playtime: treemap conserves area, contains rectangles and has no overlaps',()=>{
 for(const [w,h] of [[640,340],[90,500],[1200,1]]){
  const rect=squarify([{id:'a',value:30},{id:'b',value:20},{id:'c',value:10},{id:'z',value:0}],w,h);
  assert.equal(rect.length,3);assert.ok(Math.abs(rect.reduce((n,r)=>n+r.w*r.h,0)-w*h)<.001);
  assert.ok(Math.abs(rect.find(r=>r.id==='a').w*rect.find(r=>r.id==='a').h/(w*h)-.5)<.00001);
  for(const r of rect){assert.ok(r.x>=0&&r.y>=0&&r.x+r.w<=w+.001&&r.y+r.h<=h+.001);}
  for(let i=0;i<rect.length;i++)for(let j=i+1;j<rect.length;j++){const a=rect[i],b=rect[j];assert.ok(Math.min(a.x+a.w,b.x+b.w)-Math.max(a.x,b.x)<.001||Math.min(a.y+a.h,b.y+b.h)-Math.max(a.y,b.y)<.001);}
 }
 assert.deepEqual(squarify([{value:1}],0,0),[]);assert.deepEqual(squarify([{value:-1}],10,10),[]);
});
