import {spawn} from 'node:child_process';
const child=spawn(process.execPath,['-e','setTimeout(()=>process.exit(0),3000)'],{stdio:['ignore',1,2],windowsHide:true});child.unref();process.exit(0);
