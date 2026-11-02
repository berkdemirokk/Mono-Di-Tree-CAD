import {spawnSync} from 'node:child_process';
const [mode,dotnet,runner,node,worker]=process.argv.slice(2);let chunks=[];for await(const c of process.stdin)chunks.push(c);
const x=spawnSync(dotnet,[runner,node,worker,'8000'],{input:Buffer.concat(chunks),timeout:12000,maxBuffer:1024*1024});if(x.status!==0)process.exit(2);
const bytes=x.stdout;const half=Math.floor(bytes.length/2);process.stdout.write(bytes.subarray(0,half));
if(mode==='slow')await new Promise(r=>setTimeout(r,300));
if(mode==='cancel')await new Promise(r=>setTimeout(r,5000));
if(mode!=='partial'&&mode!=='partialfail')process.stdout.write(bytes.subarray(half));
if(mode==='latefail'||mode==='partialfail')process.exitCode=7;
