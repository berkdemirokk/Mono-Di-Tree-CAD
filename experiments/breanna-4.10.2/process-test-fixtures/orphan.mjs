import {spawn} from 'node:child_process';import {existsSync,writeFileSync} from 'node:fs';
const path='work/breanna-probe/atomic-sentinel.pid',root='work/breanna-probe/atomic-root.pid';writeFileSync(root,String(process.pid),{flag:'wx'});
const code=`require('fs').writeFileSync(${JSON.stringify(path)},String(process.pid),{flag:'wx'});setInterval(()=>{},1000)`;
const child=spawn(process.execPath,['-e',code],{detached:true,stdio:['ignore',1,2],windowsHide:true});
child.once('spawn',()=>{child.unref();const timer=setInterval(()=>{if(existsSync(path)){clearInterval(timer);process.exit(0)}},20);setTimeout(()=>process.exit(3),5000).unref();});
