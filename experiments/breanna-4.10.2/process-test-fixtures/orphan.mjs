import {spawn} from 'node:child_process';import {existsSync,writeFileSync} from 'node:fs';import {createHash} from 'node:crypto';
let input='';for await(const x of process.stdin)input+=x;const id=createHash('sha256').update(input).digest('hex').slice(0,32);
const path=`work/breanna-probe/atomic-child-${id}.pid`,root=`work/breanna-probe/atomic-root-${id}.pid`;writeFileSync(root,String(process.pid),{flag:'wx'});
const code=`require('fs').writeFileSync(${JSON.stringify(path)},String(process.pid),{flag:'wx'});setInterval(()=>{},1000)`;
const child=spawn(process.execPath,['-e',code],{detached:true,stdio:['ignore',1,2],windowsHide:true});child.once('spawn',()=>{child.unref();const timer=setInterval(()=>{if(existsSync(path)){clearInterval(timer);process.exit(0)}},20);setTimeout(()=>process.exit(3),5000).unref();});
