import {readFileSync} from 'node:fs';
import {writeFileSync,existsSync} from 'node:fs';import {spawn} from 'node:child_process';
const mode=process.argv[2];let s='';for await(const chunk of process.stdin)s+=chunk;
if(mode==='delay')await new Promise(r=>setTimeout(r,5000));
if(mode==='overflow'){process.stdout.write(Buffer.alloc(2_100_000,65));await new Promise(r=>setTimeout(r,10000));process.exit(0);}
if(mode==='orphan'){const child=spawn(process.execPath,['-e','process.stdout.write("READY");process.send("ready");setInterval(()=>process.stdout.write("."),100)'],{detached:true,windowsHide:true,stdio:['ignore',1,2,'ipc']});await new Promise((resolve,reject)=>{child.once('message',resolve);child.once('error',reject);child.once('exit',()=>reject(Error('child exited before ready')));});child.disconnect();child.unref();writeFileSync(process.argv[3],JSON.stringify({parent:process.pid,child:child.pid}));await new Promise(resolve=>{const t=setInterval(()=>{if(existsSync(process.argv[3]+'.release')){clearInterval(t);resolve();}},10);});process.exit(0);}
if(mode==='invalid'){process.stdout.write('{bad');process.exit(0)}
const fixture=JSON.parse(readFileSync(new URL('../wpf-bodice-boundary/synthetic-fixture.json',import.meta.url)));const output=fixture.output;
const id=mode==='wrong'?'synthetic-scaled':JSON.parse(s).id;
const response=JSON.stringify({scope:'fake',networkIsolation:'not-isolated',dependencyClosure:'unresolved',runtime:{},modules:[],unavailable:[],result:{id,approved:output.approved,unit:mode==='bad-unit'?'cm':output.unit,coordinateSpace:output.coordinateSpace,parts:output.parts}});
process.stdout.write(mode==='duplicate'?response.replace('"id":"synthetic-base"','"id":"synthetic-base","id":"synthetic-base"'):response);
