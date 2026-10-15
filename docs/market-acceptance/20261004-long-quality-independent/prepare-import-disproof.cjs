const fs=require('fs'),path=require('path');
const base=__dirname,s=JSON.parse(fs.readFileSync(path.join(base,'independent-inputs.json'),'utf8')).executionRoot;
const orig=fs.readFileSync(path.join(s,'ui/tests/goal-core-flow.test.cjs'),'utf8');
const copy=orig.replace("const ROOT = path.resolve(__dirname, '..', '..');",'const ROOT = '+JSON.stringify(s)+';');
const extra=`
test('INDEPENDENT IMPORT-01 cancel uses the active import operationId', async () => {
 let rejectImport; const pending=new Promise((_r,j)=>{rejectImport=j});
 const h=loadGoalLauncher({importInstance:()=>pending,cancelImport:async()=>({pendingCancel:true,ok:false})});
 mountView(h,'view-home',''); const running=h.T.handleImportFiles([{name:'pack.zip'}]);await settle(4);
 const importCall=h.calls.find(c=>c.name==='importInstance');assert.ok(importCall);
 await activate(h.document.getElementById('impCancel'));
 const cancelCall=h.calls.find(c=>c.name==='cancelImport');assert.ok(cancelCall);
 assert.equal(cancelCall.args[0],importCall.args[2]);rejectImport(new Error('terminal error'));await running;
});
test('INDEPENDENT IMPORT-04 failed cancel followed by ordinary import failure must not claim cancelled', async () => {
 let rejectImport;const pending=new Promise((_r,j)=>{rejectImport=j});
 const h=loadGoalLauncher({importInstance:()=>pending,cancelImport:async()=>{throw new Error('unknown-operation')}});
 mountView(h,'view-home','');const running=h.T.handleImportFiles([{name:'pack.zip'}]);await settle(4);
 await activate(h.document.getElementById('impCancel'));
 assert.ok(!h.toastLog.includes(h.t('import.cancelled')),'precondition: cancel failure itself is correctly reported');
 rejectImport(new Error('ordinary transport failure'));await running;
 console.log('IMPORT-04 terminal toast log: '+JSON.stringify(h.toastLog));
 assert.ok(!h.toastLog.includes(h.t('import.cancelled')),'failed cancel plus unrelated import failure must not claim confirmed cancellation; actual '+JSON.stringify(h.toastLog));
});
test('INDEPENDENT IMPORT-03 pending cancel followed by ordinary import failure must keep outcome unconfirmed', async () => {
 let rejectImport;const pending=new Promise((_r,j)=>{rejectImport=j});
 const h=loadGoalLauncher({importInstance:()=>pending,cancelImport:async()=>({pendingCancel:true,ok:false})});
 mountView(h,'view-home','');const running=h.T.handleImportFiles([{name:'pack.zip'}]);await settle(4);
 await activate(h.document.getElementById('impCancel'));
 assert.ok(h.toastLog.includes(h.t('import.cancelRecorded')));
 rejectImport(new Error('server still working'));await running;
 console.log('IMPORT-03 terminal toast log: '+JSON.stringify(h.toastLog));
 assert.ok(!h.toastLog.includes(h.t('import.cancelled')),'recorded cancellation intent does not prove cancelled when final request fails for an ordinary reason; actual '+JSON.stringify(h.toastLog));
});
`;
fs.writeFileSync(path.join(base,'import-terminal-disproof.test.cjs'),copy+'\n'+extra);
