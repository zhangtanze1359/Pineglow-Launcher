'use strict';
const fs=require('fs'),path=require('path'),cp=require('child_process'),os=require('os'),crypto=require('crypto'),assert=require('assert/strict');
const base=__dirname,root=JSON.parse(fs.readFileSync(path.join(base,'independent-inputs.json'),'utf8')).executionRoot;
const runner=path.join(root,'scripts/run-goal-validation.ps1'),temp=fs.mkdtempSync(path.join(os.tmpdir(),'codex-rework-contract-'));
const ps='C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe';
const env={...process.env,PSModulePath:'C:/Windows/System32/WindowsPowerShell/v1.0/Modules;C:/Program Files/WindowsPowerShell/Modules'};
const hash=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex').toUpperCase();
const GOOD='<TestRun><ResultSummary><Counters total="3" executed="3" passed="3" failed="0" notExecuted="0"/></ResultSummary><Results><UnitTestResult outcome="Passed"/><UnitTestResult outcome="Passed"/><UnitTestResult outcome="Passed"/></Results></TestRun>';
const BAD='<TestRun><ResultSummary><Counters total="3" executed="0" passed="0" failed="0" notExecuted="0"/></ResultSummary><Results/></TestRun>';
const childCode=`const fs=require('fs'),p=require('path');const [out,mode,source]=process.argv.slice(2);let code=0;if(mode==='mutate')fs.writeFileSync(source,'changed source');if(mode==='transient'){const marker=p.join(process.cwd(),'transient-marker');if(!fs.existsSync(marker)){fs.writeFileSync(marker,'1');code=7;}}if(mode!=='noop'){fs.mkdirSync(p.dirname(out),{recursive:true});fs.writeFileSync(out,mode==='bad'?${JSON.stringify(BAD)}:${JSON.stringify(GOOD)});}console.log('source='+fs.readFileSync(p.join(process.cwd(),'source.txt'),'utf8'));console.log('child actual exit='+code);process.exit(code);`;
let invocations=0;const cases=[];
function fixture(id){const dir=path.join(temp,id),run=path.join(dir,'run');fs.mkdirSync(run,{recursive:true});const child=path.join(dir,'child.cjs'),source=path.join(dir,'source.txt'),manifest=path.join(dir,'input.json');fs.writeFileSync(child,childCode);fs.writeFileSync(source,'frozen input');const inputs=[source,child].map(p=>({path:path.basename(p),sizeBytes:fs.statSync(p).size,sha256:hash(p)}));fs.writeFileSync(manifest,JSON.stringify({schemaVersion:1,executionRoot:dir,sourceRoot:dir,inputs}));return {id,dir,run,child,source,manifest,defs:path.join(dir,'stages.json'),stage:{id:'unit',file:process.execPath,argv:[child,'{attemptDir}/unit.trx','write'],cwd:dir,produces:['unit.trx'],expect:{trx:'unit.trx',minTests:3,maxSkipped:0}}};}
function invoke(f,{resume=false,only='',executionRoot=f.dir}={}){fs.writeFileSync(f.defs,JSON.stringify({schemaVersion:1,runId:'fixed-id',requiredStages:f.required||['unit'],stages:f.stages||[f.stage]}));const args=['-NoProfile','-ExecutionPolicy','Bypass','-File',runner,'-InputManifest',f.manifest,'-RunDir',f.run,'-StagesFile',f.defs,'-ExecutionRoot',executionRoot];if(resume)args.push('-Resume');if(only)args.push('-Only',only);const p=cp.spawnSync(ps,args,{env,encoding:'utf8',timeout:40000});const vp=path.join(f.run,'runner-verdict.json');const verdict=fs.existsSync(vp)?JSON.parse(fs.readFileSync(vp,'utf8')):null;const result={args,exitCode:p.status,stdout:p.stdout,stderr:p.stderr,verdict};fs.writeFileSync(path.join(f.dir,'invocation-'+(++invocations)+'.json'),JSON.stringify(result,null,2));return result;}
function pass(r){assert.equal(r.exitCode,0,r.stdout+r.stderr);assert.equal(r.verdict?.verdict,'PASS');}
function refuse(r){assert.equal(r.exitCode,2,r.stdout+r.stderr);assert.match(r.stdout,/REFUSED/);}
function saveCase(f,expected,r,ok,details={}){cases.push({id:f.id,expected,accepted:ok,actual:{exitCode:r.exitCode,verdict:r.verdict?.verdict,stdout:r.stdout,reused:r.verdict?.stages?.[0]?.reused},details,fixture:f.dir});}
function treeHashes(dir){const result={};function walk(p){for(const e of fs.readdirSync(p,{withFileTypes:true})){const q=path.join(p,e.name);if(e.isDirectory())walk(q);else result[path.relative(dir,q)]=hash(q);}}if(fs.existsSync(dir))walk(dir);return result;}
function sameOld(before,dir){return Object.entries(before).every(([p,h])=>fs.existsSync(path.join(dir,p))&&hash(path.join(dir,p))===h);}
{
 const f=fixture('unchanged-positive-and-resume');pass(invoke(f));const old=treeHashes(path.join(f.run,'attempts'));const r=invoke(f,{resume:true});pass(r);saveCase(f,'PASS and REUSED without new attempt',r,r.verdict.stages[0].reused===true&&JSON.stringify(old)===JSON.stringify(treeHashes(path.join(f.run,'attempts'))));
}
{
 const f=fixture('actual-input-changed');pass(invoke(f));const before=treeHashes(f.run);fs.writeFileSync(f.source,'changed data');const r=invoke(f,{resume:true});saveCase(f,'REFUSED and old evidence unchanged',r,r.exitCode===2&&sameOld(before,f.run),{oldEvidenceUnchanged:sameOld(before,f.run)});
}
{
 const f=fixture('stale-trx');f.stage.argv[2]='noop';fs.writeFileSync(path.join(f.run,'unit.trx'),GOOD);const r=invoke(f);saveCase(f,'FAIL/TRX_MISSING; old TRX cannot count',r,r.exitCode===1&&r.verdict?.stages[0].reasons.includes('TRX_MISSING'));
}
{
 const f=fixture('zero-executed-counters');f.stage.argv[2]='bad';const r=invoke(f);saveCase(f,'FAIL/ZERO_TESTS',r,r.exitCode===1&&r.verdict?.stages[0].reasons.includes('ZERO_TESTS'));
}
{
 const f=fixture('produces-changed');pass(invoke(f));const before=treeHashes(f.run);f.stage.produces.push('missing.bin');const r=invoke(f,{resume:true});saveCase(f,'REFUSED; old evidence preserved',r,r.exitCode===2&&sameOld(before,f.run));
}
{
 const f=fixture('input-manifest-sha-changed');pass(invoke(f));const before=treeHashes(f.run);fs.appendFileSync(f.manifest,'\n');const r=invoke(f,{resume:true});saveCase(f,'REFUSED; old evidence preserved',r,r.exitCode===2&&sameOld(before,f.run));
}
{
 const f=fixture('empty-stages');f.stages=[];f.required=[];const r=invoke(f);saveCase(f,'REFUSED/NO_STAGES',r,r.exitCode===2&&r.stdout.includes('NO_STAGES'));
}
{
 const f=fixture('unknown-only');const r=invoke(f,{only:'missing-id'});saveCase(f,'REFUSED/ONLY_NOT_FOUND',r,r.exitCode===2&&r.stdout.includes('ONLY_NOT_FOUND'));
}
{
 const f=fixture('failure-resume-original-evidence');f.stage.argv[2]='transient';const first=invoke(f);assert.equal(first.exitCode,1,first.stdout+first.stderr);const original=treeHashes(path.join(f.run,'attempts'));const oldRecord=fs.readFileSync(path.join(f.run,'stages/unit.json'),'utf8');const r=invoke(f,{resume:true});pass(r);const archived=fs.readdirSync(path.join(f.run,'stages')).some(p=>p!=='unit.json'&&fs.readFileSync(path.join(f.run,'stages',p),'utf8')===oldRecord);saveCase(f,'second attempt PASS; old failed attempt and record preserved',r,sameOld(original,path.join(f.run,'attempts'))&&archived,{failedChildExit:first.verdict.stages[0].exitCode,oldEvidenceUnchanged:sameOld(original,path.join(f.run,'attempts')),oldRecordArchived:archived});
}
{
 const f=fixture('legal-partial-then-full');const second={...f.stage,id:'second'};f.stages=[f.stage,second];f.required=['unit','second'];const partial=invoke(f,{only:'unit'});const r=invoke(f,{resume:true});pass(r);saveCase(f,'PARTIAL exit3 then full PASS with first stage reused',r,partial.exitCode===3&&partial.verdict?.verdict==='PARTIAL'&&r.verdict.stages[0].reused===true,{partialExit:partial.exitCode,partialVerdict:partial.verdict?.verdict});
}
{
 const f=fixture('execution-root-misbinding');const other=path.join(f.dir,'other-execution');fs.mkdirSync(other);fs.writeFileSync(path.join(other,'child.cjs'),childCode);fs.writeFileSync(path.join(other,'source.txt'),'different actual input');f.stage.cwd='{executionRoot}';f.stage.argv=['child.cjs','{attemptDir}/unit.trx','write'];const r=invoke(f,{executionRoot:other});saveCase(f,'REFUSED: manifest executionRoot must match supplied executionRoot',r,r.exitCode!==0,{manifestVerifiedRoot:f.dir,actualExecutionRoot:other,stageLog:fs.existsSync(path.join(f.run,'attempts/unit/1/logs/unit.stdout.log'))?fs.readFileSync(path.join(f.run,'attempts/unit/1/logs/unit.stdout.log'),'utf8'):null});
}
{
 const f=fixture('input-mutated-during-run');f.stage.argv=[f.child,'{attemptDir}/unit.trx','mutate',f.source];const r=invoke(f);const listed=JSON.parse(fs.readFileSync(f.manifest,'utf8')).inputs.find(x=>x.path==='source.txt');saveCase(f,'FAIL/REFUSED if stage changes a frozen input before final verdict',r,r.exitCode!==0,{declaredSha:listed.sha256,actualFinalSha:hash(f.source)});
}
{
 const f=fixture('expected-trx-omitted-from-produces');f.stage.produces=[];pass(invoke(f));const record=JSON.parse(fs.readFileSync(path.join(f.run,'stages/unit.json'),'utf8'));fs.writeFileSync(path.join(f.run,record.attemptDir,'unit.trx'),BAD);const r=invoke(f,{resume:true});saveCase(f,'Refuse reuse or rerun when the expected TRX evidence changes',r,r.exitCode!==0||r.verdict?.stages[0]?.reused!==true,{expectedTrxModified:true,declaredProduces:[]});
}
fs.writeFileSync(path.join(base,'runner-independent-contract-results.json'),JSON.stringify({runner,fixtureRoot:temp,cases},null,2));console.log(JSON.stringify(cases.map(c=>({id:c.id,accepted:c.accepted,exitCode:c.actual.exitCode,verdict:c.actual.verdict,reused:c.actual.reused,details:c.details})),null,2));process.exit(cases.every(x=>x.accepted)?0:1);
