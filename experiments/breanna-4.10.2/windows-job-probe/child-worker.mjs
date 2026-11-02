import {spawn} from 'node:child_process';
const child=spawn(process.execPath,['-e','console.log("CHILD_READY");setInterval(()=>{},1000)'],{stdio:['ignore',1,2],windowsHide:true,detached:true});
child.once('spawn',()=>{console.log('CHILD='+child.pid);child.unref();if(process.argv[2]==='keep'||process.argv[2]==='cancel')setInterval(()=>{},1000);else setTimeout(()=>process.exit(0),1500);});
