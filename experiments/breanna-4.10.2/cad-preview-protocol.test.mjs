import test from 'node:test';
import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {optionDefinitions} from './draft.mjs';
import {createExperimentalJob} from './experimental-job.mjs';
const dotnet=process.env.TEKSTILCAD_DOTNET||'dotnet', loc=x=>fileURLToPath(new URL(x,import.meta.url));
const opts=Object.fromEntries(Object.entries(optionDefinitions).map(([k,d])=>[k,'pct'in d?d.pct/100:'bool'in d?d.bool:d.dflt]));
test('synthetic worker geometry stays diagnostic and fails closed',()=>{
 const fixture=spawnSync(dotnet,[loc('./bridge-probe/bin/Debug/net10.0/Probe.dll')],{encoding:'utf8',timeout:10000});
 assert.equal(fixture.status,0);
 const job=createExperimentalJob(fixture.stdout,opts,{unit:'mm',angleUnit:'degree',toleranceMm:.1,layout:'local-unplaced'});
 const worker=spawnSync(dotnet,[loc('./windows-node-probe/bin/Debug/net10.0/NodeProbe.dll'),process.execPath,loc('./stdio-worker.mjs'),'8000'],{input:job,encoding:'utf8',timeout:15000,maxBuffer:1048576});
 assert.equal(worker.status,0);
 const probe=spawnSync(dotnet,[loc('./cad-preview-probe/bin/Debug/net10.0/CadPreviewProbe.dll')],{input:JSON.stringify({request:job,response:worker.stdout}),encoding:'utf8',timeout:10000,maxBuffer:1048576});
 assert.equal(probe.status,0,probe.stderr); assert.match(probe.stdout,/^PASS: synthetic affine/); assert.equal(probe.stderr,'');
});
