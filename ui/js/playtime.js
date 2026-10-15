/* Real play sessions only. Shared pure calculations are also usable from node:test. */
(function (root) {
'use strict';
const dayKey = d => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
function parseDay(value) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value || '')) return null;
  const [y,m,d] = value.split('-').map(Number), date = new Date(y,m-1,d);
  return dayKey(date) === value ? date : null;
}
function rangeFor(kind, now, from, to) {
  const end = new Date(now.getFullYear(),now.getMonth(),now.getDate());
  let start = new Date(end);
  if (kind === 'week') start.setDate(start.getDate() - (start.getDay()+6)%7);
  else if (kind === 'month') start.setDate(1);
  else if (kind === '7' || kind === '30') start.setDate(start.getDate() - Number(kind)+1);
  else if (kind === 'custom') {
    start = parseDay(from); const last = parseDay(to);
    if (!start || !last || start > last) throw new Error('invalidRange');
    // Calendar days, not elapsed hours (DST can change a day's length).
    const count = (Date.UTC(last.getFullYear(),last.getMonth(),last.getDate())-Date.UTC(start.getFullYear(),start.getMonth(),start.getDate()))/86400000+1;
    if (count > 120) throw new Error('longRange');
    end.setTime(last.getTime());
  }
  end.setDate(end.getDate()+1);
  return { start, end };
}
function splitSessions(sessions) {
  const parts=[];
  for (const [index,session] of (sessions || []).entries()) {
    let start = new Date(session.startedAt), end = new Date(session.endedAt);
    if (!session.startedAt || !session.endedAt || !Number.isFinite(+start) || !Number.isFinite(+end) || end <= start) continue;
    // Ignore malformed outliers. No invented dates or duration fallback.
    if (+end - +start > 366*86400000) continue;
    while (start < end) {
      const midnight = new Date(start.getFullYear(),start.getMonth(),start.getDate()+1);
      const until = new Date(Math.min(+midnight,+end));
      parts.push({ ...session, sessionKey:session.id||`${index}:${session.startedAt}`, day:dayKey(start), seconds:(until-start)/1000 });
      start = until;
    }
  }
  return parts;
}
// Squarified area layout. Empty/zero dimensions are valid inputs.
function squarify(items,width,height) {
  const positive=items.filter(x=>Number.isFinite(x.value)&&x.value>0);
  if (!(width>0 && height>0) || !positive.length) return [];
  const sum=positive.reduce((n,x)=>n+x.value,0), scale=width*height/sum;
  const todo=positive.map(x=>({...x,area:x.value*scale})).sort((a,b)=>b.area-a.area);
  const out=[]; let x=0,y=0,w=width,h=height,row=[];
  const worst=(r,side)=>{
    if (!r.length || side<=0) return Infinity;
    const s=r.reduce((n,a)=>n+a.area,0), hi=Math.max(...r.map(a=>a.area)),lo=Math.min(...r.map(a=>a.area));
    return Math.max(side*side*hi/(s*s),s*s/(side*side*lo));
  };
  const place=()=>{
    const area=row.reduce((n,a)=>n+a.area,0);
    if (w>=h) { const rw=area/h; let ry=y; row.forEach(a=>{const rh=a.area/rw;out.push({...a,x,y:ry,w:rw,h:rh});ry+=rh;});x+=rw;w=Math.max(0,w-rw); }
    else { const rh=area/w;let rx=x;row.forEach(a=>{const rw=a.area/rh;out.push({...a,x:rx,y,w:rw,h:rh});rx+=rw;});y+=rh;h=Math.max(0,h-rh); }
    row=[];
  };
  while(todo.length) { const next=todo[0],side=Math.min(w,h);if(!row.length||worst([...row,next],side)<=worst(row,side)){row.push(todo.shift());}else place(); }
  if(row.length)place(); return out;
}
// Longest means the session's duration inside the selected calendar range.
function summarizeParts(parts) {
 const sessions=new Map(),days=new Set();let seconds=0;
 for(const p of parts){seconds+=p.seconds;days.add(p.day);sessions.set(p.sessionKey,(sessions.get(p.sessionKey)||0)+p.seconds);}
 return {seconds,count:sessions.size,days:days.size,longest:Math.max(0,...sessions.values())};
}
function buildSessionTree(parts) {
 const instances=new Map();
 for(const p of parts){
  const id=p.instanceId||p.instanceName||'unknown';
  if(!instances.has(id))instances.set(id,{id,name:p.instanceName||p.instanceId||'—',parts:[],dates:new Map()});
  const g=instances.get(id);g.parts.push(p);
  if(!g.dates.has(p.day))g.dates.set(p.day,[]);
  g.dates.get(p.day).push(p);
 }
 return [...instances.values()].map(g=>{
  const summary=summarizeParts(g.parts);
  return {id:g.id,name:g.name,seconds:summary.seconds,count:summary.count,days:[...g.dates].sort(([a],[b])=>b.localeCompare(a)).map(([day,sessions])=>({day,...summarizeParts(sessions),sessions:sessions.slice().sort((a,b)=>+new Date(a.startedAt)-+new Date(b.startedAt))}))};
 }).sort((a,b)=>b.seconds-a.seconds||String(a.id).localeCompare(String(b.id)));
}
const core={dayKey,parseDay,rangeFor,splitSessions,squarify,summarizeParts,buildSessionTree};
if(typeof module==='object'&&module.exports)module.exports=core;
if(!root || !root.SG)return;
const SG=root.SG, esc=s=>String(s==null?'':s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const tr=(key,vars)=>SG.t('pt.'+key,vars);
let state={range:'month',from:'',to:'',day:null,month:new Date(),stats:null,treeOpen:new Map()};
const duration=(s,full=false)=>{s=Math.max(0,Math.floor(s||0));const m=Math.floor(s/60),h=Math.floor(m/60);if(full&&SG.locale==='zh')return h?`${h} 小时${m%60?' '+m%60+' 分':''}`:m?`${m} 分`:`${s} 秒`;return h?`${h}h${m%60?m%60+'m':''}`:m?`${m}m`:`${s}s`;};
function statsHtml(summary,label) {
 return `<div class="home-stats"><div class="home-stat">${esc(label)} · ${tr('rangeTotal')}<b>${duration(summary.seconds,true)}</b></div><div class="home-stat">${tr('count')}<b>${summary.count}</b></div><div class="home-stat">${tr('activeDays')}<b>${summary.days}</b></div><div class="home-stat">${tr('longest')}<b>${duration(summary.longest,true)}</b></div></div>`;
}
const branchIcon='<svg viewBox="0 0 16 16" aria-hidden="true"><path d="m6 4 4 4-4 4"/></svg>';
function sessionTime(part) {
 const start=parseDay(part.day),end=new Date(start);end.setDate(end.getDate()+1);
 const from=new Date(Math.max(+start,+new Date(part.startedAt))),to=new Date(Math.min(+end,+new Date(part.endedAt)));
 const time=d=>d.toLocaleTimeString(SG.locale==='zh'?'zh-CN':'en-GB',{hour:'2-digit',minute:'2-digit',hour12:false});
 return `${time(from)} — ${+to===+end?'24:00':time(to)}`;
}
function treeHtml(groups,colorIds) {
 const branchOpen=(key,initial)=>state.treeOpen.has(key)?state.treeOpen.get(key):initial;
 const total=groups.reduce((n,g)=>n+g.seconds,0),count=groups.reduce((n,g)=>n+g.count,0);
 return `<div class="pt-tree-root"><span class="pt-tree-root-icon" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M12 8v5M5 17v-4h14v4"/><rect x="9" y="2" width="6" height="6" rx="1"/><rect x="2" y="17" width="6" height="5" rx="1"/><rect x="16" y="17" width="6" height="5" rx="1"/></svg></span><span>${tr('treeRoot')}<small>${tr('treeCount',{n:count})}</small></span><b>${duration(total,true)}</b></div><ul class="pt-tree-list">${groups.map((g,groupIndex)=>{
  const ik=JSON.stringify(['instance',g.id]),index=colorIds.indexOf(g.id)%4;
  return `<li class="pt-tree-instance" data-tree-color="${index}"><details data-pt-branch="${esc(ik)}" ${branchOpen(ik,groupIndex===0)?'open':''}><summary class="pt-tree-node pt-instance-node">${branchIcon}<i class="pt-tree-dot" aria-hidden="true"></i><span class="pt-tree-label" title="${esc(g.name)}">${esc(g.name)}<small>${tr('treeCount',{n:g.count})}</small></span><b>${duration(g.seconds)}</b></summary><ul>${g.days.map((day,n)=>{
   const dk=JSON.stringify(['day',g.id,day.day]);
   return `<li><details data-pt-branch="${esc(dk)}" ${branchOpen(dk,n===0)?'open':''}><summary class="pt-tree-node pt-date-node" data-readout="${esc(day.day+' · '+g.name+' · '+duration(day.seconds))}">${branchIcon}<span class="pt-tree-label">${day.day}<small>${tr('treeCount',{n:day.count})}</small></span><b>${duration(day.seconds)}</b></summary><ul>${day.sessions.map((part,i)=>{
    const time=sessionTime(part),readout=part.day+' · '+g.name+' · '+time+' · '+duration(part.seconds,true);
    return `<li><button type="button" class="pt-tree-node pt-session-node" data-pt-day="${part.day}" data-readout="${esc(readout)}" aria-label="${esc(readout+' · '+tr('treeSelectDate'))}"><span class="pt-tree-leaf" aria-hidden="true"></span><span class="pt-tree-label">${tr('treeRecord',{n:i+1})}<small>${esc(time)}</small></span><b>${duration(part.seconds)}</b></button></li>`;
   }).join('')}</ul></details></li>`;
  }).join('')}</ul></details></li>`;
 }).join('')}</ul>`;
}
async function render() {
 try {state.stats=await SG.api.getStats();}
 catch(e){return `<div class="ui-card"><div class="content-error" role="alert"><h2>${tr('loadFailed')}</h2><p>${esc(e.message)}</p><button class="ui-button" data-pt-retry>${SG.t('content.retry')}</button></div></div>`;}
 return `<div class="playtime-view"><section class="ui-card pt-summary" id="ptSummary"></section><div class="pt-grid"><section class="ui-card" id="ptChartCard"><div class="ui-card-head"><div><h2>${tr('chart')}</h2><p class="ui-muted">${tr('hint')}</p></div><div class="pt-tree-actions"><button type="button" data-pt-expand="true">${tr('treeExpand')}</button><button type="button" data-pt-expand="false">${tr('treeCollapse')}</button></div></div><div class="pt-controls" role="group" aria-label="${tr('range')}">${['week','month','7','30','custom'].map(k=>`<button data-pt-range="${k}" aria-pressed="${state.range===k}">${tr('range.'+k)}</button>`).join('')}<div class="pt-custom" ${state.range==='custom'?'':'hidden'}><label>${tr('from')} <input type="date" id="ptFrom" value="${esc(state.from)}"></label><label>${tr('to')} <input type="date" id="ptTo" value="${esc(state.to)}"></label><button data-pt-apply>${tr('apply')}</button></div><button data-pt-clear ${state.day?'':'hidden'}>${tr('clear')}</button></div><p id="ptError" role="alert" class="ui-muted"></p><p class="pt-range-label" id="ptRangeLabel"></p><div class="play-tree" id="ptTreemap" role="group" aria-label="${tr('chart')}"></div><p class="pt-readout" id="ptReadout" role="status">${tr('hint')}</p></section><section class="ui-card pt-calendar"><div class="ui-card-head"><div><h2>${tr('calendar')}</h2><p class="ui-muted">${tr('calendarNote')}</p></div></div><div class="cal-head"><button class="ui-button" data-pt-month="-1" aria-label="${tr('previous')}">‹</button><span id="ptMonth"></span><button class="ui-button" data-pt-month="1" aria-label="${tr('next')}">›</button></div><div class="cal-grid" id="ptCalendar"></div><div class="cal-summary" id="ptMonthSummary"></div></section></div><details class="pt-history ui-muted"><summary>${tr('recordingInfo')}</summary><p>${tr('historyNote')}</p><p>${tr('total')}: ${duration(state.stats.totalPlaySeconds,true)}</p></details></div>`;
}
function bind(el) {
 el.querySelector('[data-pt-retry]')?.addEventListener('click',async()=>{el.innerHTML=await render();bind(el);});
 if(!el.querySelector('#ptTreemap'))return;
 const parts=splitSessions((state.stats||{}).sessions);
 const redraw=()=>{
  let range;try{range=rangeFor(state.range,new Date(),state.from,state.to);el.querySelector('#ptError').textContent='';}catch(e){el.querySelector('#ptError').textContent=tr(e.message);return;}
  el.querySelector('#ptRangeLabel').textContent=state.day||`${dayKey(range.start)} — ${dayKey(new Date(+range.end-1))}`;
  const selected=parts.filter(p=>{const d=parseDay(p.day);return d>=range.start&&d<range.end&&(!state.day||p.day===state.day);});
  el.querySelector('#ptSummary').innerHTML=statsHtml(summarizeParts(selected),state.day||tr('range.'+state.range));
  const chart=el.querySelector('#ptTreemap');
  try {
   if(!selected.length){chart.innerHTML=`<div class="pt-empty">${tr('empty')}</div>`;return;}
   const colorIds=[...new Set(parts.map(p=>p.instanceId||p.instanceName||'unknown'))];
   chart.innerHTML=treeHtml(buildSessionTree(selected),colorIds);
  }catch(e){chart.innerHTML=`<div class="pt-empty" role="alert">${tr('drawFailed')} ${esc(e.message)}</div>`;}

 };
 const calendar=()=>{
  const m=state.month,year=m.getFullYear(),month=m.getMonth();
  el.querySelector('#ptMonth').textContent=m.toLocaleDateString(SG.locale==='zh'?'zh-CN':'en',{year:'numeric',month:'long'});
  const totals={};parts.forEach(p=>totals[p.day]=(totals[p.day]||0)+p.seconds);
  const days=new Date(year,month+1,0).getDate(),max=Math.max(1,...Array.from({length:days},(_,i)=>totals[dayKey(new Date(year,month,i+1))]||0));
  let html=tr('weekdays').split(',').map(d=>`<span class="cal-weekday">${esc(d)}</span>`).join('');
  html+='<span></span>'.repeat((new Date(year,month,1).getDay()+6)%7);
  for(let d=1;d<=days;d++){const key=dayKey(new Date(year,month,d)),s=totals[key]||0;html+=`<button class="cal-cell" ${s?`data-pt-day="${key}"`:'disabled'} aria-pressed="${state.day===key}" aria-label="${key} · ${duration(s)}"><i class="cal-bar" style="height:${s/max*100}%"></i><span>${d}${s?`<small>${duration(s)}</small>`:''}</span></button>`;}
  el.querySelector('#ptCalendar').innerHTML=html;
  const values=Object.entries(totals).filter(([key])=>key.startsWith(`${year}-${String(month+1).padStart(2,'0')}-`));
  el.querySelector('#ptMonthSummary').innerHTML=`<span>${tr('monthTotal')}<b>${duration(values.reduce((n,[,s])=>n+s,0),true)}</b></span><span>${tr('activeDays')}<b>${values.length}</b></span>`;
 };
 const refresh=()=>{redraw();calendar();el.querySelector('[data-pt-clear]').hidden=!state.day;};
 el.onclick=e=>{
  const b=e.target.closest('button');if(!b)return;
  if(b.dataset.ptRange){state.range=b.dataset.ptRange;state.day=null;el.querySelectorAll('[data-pt-range]').forEach(x=>x.setAttribute('aria-pressed',String(x===b)));el.querySelector('.pt-custom').hidden=state.range!=='custom';if(state.range==='custom'&&!state.from){const r=rangeFor('30',new Date());state.from=dayKey(r.start);state.to=dayKey(new Date());el.querySelector('#ptFrom').value=state.from;el.querySelector('#ptTo').value=state.to;}refresh();}
  if(b.hasAttribute('data-pt-apply')){state.from=el.querySelector('#ptFrom').value;state.to=el.querySelector('#ptTo').value;state.day=null;refresh();}
  if(b.hasAttribute('data-pt-clear')){state.day=null;refresh();}
  if(b.dataset.ptDay){state.day=state.day===b.dataset.ptDay?null:b.dataset.ptDay;refresh();}
  if(b.dataset.ptExpand){const open=b.dataset.ptExpand==='true';el.querySelectorAll('[data-pt-branch]').forEach(node=>{state.treeOpen.set(node.dataset.ptBranch,open);node.open=open;});}
  if(b.dataset.ptMonth){state.month=new Date(state.month.getFullYear(),state.month.getMonth()+Number(b.dataset.ptMonth),1);calendar();}
 };
 const read=e=>{const c=e.target.closest('[data-readout]');if(c)el.querySelector('#ptReadout').textContent=c.dataset.readout;};
 el.onpointerover=read;
 if(el._ptReadHandler)el.removeEventListener('focusin',el._ptReadHandler);
 el._ptReadHandler=read;el.addEventListener('focusin',read);
 refresh();
 if(el._ptToggleHandler)el.removeEventListener('toggle',el._ptToggleHandler,true);
 el._ptToggleHandler=e=>{const node=e.target;if(node.matches&&node.matches('[data-pt-branch]')&&node.isConnected&&el.contains(node))state.treeOpen.set(node.dataset.ptBranch,node.open);};
 el.addEventListener('toggle',el._ptToggleHandler,true);
}

SG.playtime={render,bind,dispose(){},core};
})(typeof window==='undefined'?null:window);
