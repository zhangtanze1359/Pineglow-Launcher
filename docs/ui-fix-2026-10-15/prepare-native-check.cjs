const fs=require('fs'),path=require('path');
const dir=__dirname;let source=fs.readFileSync(path.join(dir,'../ui-integration-2026-10-15/verify-redesign.cjs'),'utf8');
const owned=JSON.parse(fs.readFileSync(path.join(dir,'native/owned-process.json'),'utf8').replace(/^\uFEFF/,''));
source=source.replace("process.env.PINEGLOW_VERIFY_DEBUG||'49377'",`process.env.PINEGLOW_VERIFY_DEBUG||'${owned.debugPort}'`).replace("const root=__dirname,report=", "const root=path.join(__dirname,'native-check'),report=");
fs.mkdirSync(path.join(dir,'native-check'),{recursive:true});
fs.writeFileSync(path.join(dir,'verify-native.cjs'),source);
