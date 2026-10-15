const fs=require('node:fs'), z=require('node:zlib');
const file=process.argv[2], b=fs.readFileSync(file); let p=0, texts=[];
while(p<b.length){const start=p, magic=b.readUInt32LE(p);p+=4;
if((magic&0xfffffff0)===0x184d2a50){const n=b.readUInt32LE(p);p+=4+n;continue;}
if(magic!==0xfd2fb528)throw Error('Unexpected magic '+start);
const d=b[p++], single=(d>>5)&1, checksum=(d>>2)&1, dict=d&3, fcs=d>>6;
if(!single)p++;p+=[0,1,2,4][dict];p+=[single?1:0,2,4,8][fcs];
let last;do{let h=b[p]|b[p+1]<<8|b[p+2]<<16;p+=3;last=h&1;const kind=(h>>1)&3,size=h>>3;if(kind===3)throw Error('Reserved block');p+=kind===1?1:size;}while(!last);
if(checksum)p+=4;if(p>b.length)throw Error('Overrun');texts.push(z.zstdDecompressSync(b.subarray(start,p)).toString('utf8'));}
let types={},picked=[], planTools=[], results=[];
for(const line of texts.join('').split(/\r?\n/).filter(Boolean)){let r=JSON.parse(line);types[r.type]=(types[r.type]||0)+1;
if(r.type==='session')picked.push(r);
if(r.type==='user/message' && r.data?.source?.kind==='user')picked.push({seq:r.seq,time:r.time,text:r.data.content.filter(x=>x.type==='text').map(x=>x.text).join('\n')});
if(r.type==='agent/inbox/spliced')for(const x of r.data.inserted||[])if(x.source?.kind==='user')picked.push({seq:r.seq,time:r.time,text:x.content?.filter(v=>v.type==='text').map(v=>v.text).join('\n')});
if(r.type==='tool/call' && /^(ask_user_question|exit_plan_mode)$/.test(r.data?.name||''))planTools.push(r);
if(r.type==='tool/result')results.push(r);
}
const ids=new Set(planTools.map(r=>r.data.callId));
console.log(JSON.stringify({frames:texts.length,types,picked,planTools,planResults:results.filter(r=>ids.has(r.data?.message?.toolCallId))},null,2));
