'use strict';
// Independent, ordinary offline runner contract probes. No product files are edited.
const fs=require('fs'),path=require('path'),cp=require('child_process'),os=require('os'),crypto=require('crypto');
const base=__dirname, inputState=JSON.parse(fs.readFileSync(path.join(base,'independent-inputs.json'),'utf8'));
const runner=path.join(inputState.executionRoot,'scripts/run-goal-validation.ps1');
const temp=fs.mkdtempSync(path.join(os.tmpdir(),'codex-runner-contract-'));
const ps='C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe';
const env={...process.env,PSModulePath:'C:/Windows/System32/WindowsPowerShell/v1.0/Modules;C:/Program Files/WindowsPowerShell/Modules'};
const sha=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex').toUpperCase();
const cases=[];
function fixture(id){
 const dir=path.join(temp,id),run=path.join(dir,'run');fs.mkdirSync(run,{recursive:true});
 const source=path.join(dir,'source.txt');fs.writeFileSync(source,'frozen input');
 const manifest=path.join(dir,'input.json');fs.writeFileSync(manifest,JSON.stringify({schemaVersion:1,sourceRoot:dir,executionRoot:dir,inputs:[{path:'source.txt',sizeBytes:12,sha256:sha(source)}]}));
 const child=path.join(dir,'child.cjs');fs.writeFileSync(child,`const fs=require('fs');const path=require('path');const arg=process.argv[2];const out=process.argv[3];const rows=(p,f,s)=>{let o='',n=0;for(let i=0;i<p;i++)o+='<UnitTestResult testName="t'+(n++)+'" outcome="Passed" />';for(let i=0;i<f;i++)o+='<UnitTestResult testName="t'+(n++)+'" outcome="Failed" />';for(let i=0;i<s;i++)o+='<UnitTestResult testName="t'+(n++)+'" outcome="NotExecuted" />';return o;};if(arg==='write'){fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,'<TestRun><ResultSummary><Counters total="3" executed="3" passed="3" failed="0" notExecuted="0"/></ResultSummary><Results>'+rows(3,0,0)+'</Results></TestRun>');}console.log('child ran');`);
 // REBIND (this round): the runner keeps a fresh attempt directory per run, so the child is told
 // where to write through the documented {attemptDir} placeholder. Expectations are unchanged.
 const stage={id:'unit',file:process.execPath,argv:[child,'write','{attemptDir}/unit.trx'],cwd:run,produces:['unit.trx'],expect:{trx:'unit.trx',minTests:3,maxSkipped:0}};
 return {id,dir,run,source,manifest,child,stage,defs:path.join(dir,'stages.json')};
}
function invoke(f,resume=false,only=''){
 fs.writeFileSync(f.defs,JSON.stringify({schemaVersion:1,runId:'same-run-id',stages:f.stages||[f.stage]}));
 const args=['-NoProfile','-ExecutionPolicy','Bypass','-File',runner,'-InputManifest',f.manifest,'-RunDir',f.run,'-StagesFile',f.defs];if(resume)args.push('-Resume');if(only)args.push('-Only',only);
 const p=cp.spawnSync(ps,args,{encoding:'utf8',env,timeout:30000});
 const vpath=path.join(f.run,'runner-verdict.json');const v=fs.existsSync(vpath)?JSON.parse(fs.readFileSync(vpath,'utf8')):null;
 const n=fs.readdirSync(f.dir).filter(x=>/^invocation-/.test(x)).length;
 fs.writeFileSync(path.join(f.dir,`invocation-${n}.json`),JSON.stringify({args,exitCode:p.status,stdout:p.stdout,stderr:p.stderr,verdict:v},null,2));
 return {exitCode:p.status,verdict:v};
}
function record(f,expected,result,details){cases.push({id:f.id,expected,actual:result,details,fixture:f.dir});}
{
 const f=fixture('actual-input-changed-manifest-unchanged');invoke(f);fs.writeFileSync(f.source,'changed source');
 record(f,'Reject input drift; no REUSED or PASS',invoke(f,true),{actualSourceSha:sha(f.source),manifestBytesUnchanged:true});
}
{
 const f=fixture('stale-trx');f.stage.argv=[f.child,'noop','{attemptDir}/unit.trx'];const stale=path.join(f.dir,'stale-unit.trx');fs.writeFileSync(stale,'<TestRun><ResultSummary><Counters total="3" executed="3" passed="3" failed="0" notExecuted="0"/></ResultSummary></TestRun>');f.stage.expect={trx:stale,minTests:3,maxSkipped:0};
 record(f,'Reject existing TRX untouched by child',invoke(f),{childDoesNotWriteTrx:true,staleTrx:stale});
}
{
 const f=fixture('inconsistent-counters');fs.writeFileSync(f.child,`const fs=require('fs'),path=require('path');const out=process.argv[3];fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,'<TestRun><ResultSummary><Counters total="3" executed="0" passed="0" failed="0" notExecuted="0"/></ResultSummary></TestRun>');`);
 record(f,'Reject zero executed/passed tests and inconsistent counters',invoke(f),{});
}
{
 const f=fixture('produces-list-changed');invoke(f);f.stage.produces.push('missing-new-output.bin');
 record(f,'Refuse reuse after required outputs change',invoke(f,true),{newArtifactExists:false});
}
{
 const f=fixture('resume-changed-manifest-same-run-id');invoke(f);fs.appendFileSync(f.manifest,'\n');
 const prior=fs.readFileSync(path.join(f.run,'stages/unit.json'),'utf8');const result=invoke(f,true);
 record(f,'New runId and preserve prior completed record/evidence',result,{oldStageRecordRetained:fs.readdirSync(path.join(f.run,'stages')).some(x=>x!=='unit.json'&&fs.readFileSync(path.join(f.run,'stages',x),'utf8')===prior),stageFiles:fs.readdirSync(path.join(f.run,'stages'))});
}
{
 const f=fixture('empty-stages');f.stages=[];record(f,'Reject zero-stage validation',invoke(f),{});
}
{
 const f=fixture('only-unknown');record(f,'Reject unknown Only selection',invoke(f,false,'missing-id'),{});
}
fs.writeFileSync(path.join(base,'runner-disproof-results.json'),JSON.stringify({runner,fixtureRoot:temp,cases},null,2));
console.log(JSON.stringify(cases.map(x=>({id:x.id,exitCode:x.actual.exitCode,verdict:x.actual.verdict?.verdict,stages:x.actual.verdict?.stageCount,reused:x.actual.verdict?.stages[0]?.reused,details:x.details})),null,2));
