import {runExperimentalJob} from './experimental-job.mjs';
const MAX=32768;let chunks=[],length=0,tooLarge=false;
try {
 for await(const chunk of process.stdin){length+=chunk.length;if(length>MAX){tooLarge=true;break;}chunks.push(chunk);}
 if(tooLarge||length===0)throw new Error('invalid');
 const text=new TextDecoder('utf-8',{fatal:true}).decode(Buffer.concat(chunks));
 const preview=JSON.parse(runExperimentalJob(text));
 const output=JSON.stringify({protocol:'tekstilcad.experimental.preview-stdio.v1',ok:true,preview});
 if(Buffer.byteLength(output,'utf8')>1024*1024)throw new Error('large');
 process.stdout.write(output);
} catch { process.stdout.write('{"protocol":"tekstilcad.experimental.preview-stdio.v1","ok":false,"error":"INVALID_REQUEST"}');process.exitCode=2; }
