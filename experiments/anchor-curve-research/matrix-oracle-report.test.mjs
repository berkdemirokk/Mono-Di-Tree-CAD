import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,mkdtempSync,writeFileSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {report,validateMatrix} from './matrix-oracle-report.mjs';
const bodies=['synthetic-base','synthetic-scaled','synthetic-distinct'];
const variants=['primary-only','two-darts','shoulder-and-waist','rotated-primary','rotated-two'];
function sample(){return {sourceVersion:'4.10.2',coordinateSpace:'part local mm; placements not applied',sourceHashes:Object.fromEntries(Array.from({length:5},(_,i)=>['h'+i,'a'.repeat(64)])),cases:bodies.flatMap(body=>variants.map(variant=>({body,variant,status:'inspected',measurements:{},options:{},parts:['breanna.front','breanna.back'].map(part=>({part,coordinateSpace:'source part-local mm; no placement transform applied',transferable:false,edges:[{id:part+'/seam/op-1',sourceOpIndex:1}],anchors:(part==='breanna.front'?['armholePitch','bustNotch']:['armholePitch']).map(name=>({name}))}))})))};}
test('matrix validation rejects duplicate, missing, failed and cross-part identities',()=>{
 const data=sample();assert.deepEqual(validateMatrix(data),{caseCount:15,partCount:30,anchorCount:45});
 for(const mutate of [d=>d.cases[1].variant=d.cases[0].variant,d=>d.cases.pop(),d=>d.cases[0].status='failed',d=>d.cases[0].parts[0].coordinateSpace='placed mm',d=>d.cases[0].parts[0].edges[0].id='breanna.back/seam/op-1',d=>d.cases[0].parts[0].anchors.pop(),d=>d.cases[0].parts[0].anchors[1].name='armholePitch']) {
  const copy=structuredClone(data);mutate(copy);assert.throws(()=>validateMatrix(copy));
 }
});
test('different bytes cannot inherit fixture identity',async()=>assert.rejects(report(Buffer.from('{}'),{thresholdMm:0,maxDepth:0,maxNodes:1}),/source hash mismatch/));
test('optional pinned 15-case matrix yields 30 panels and 45 anchor reports', {skip:!process.env.ANCHOR_MATRIX_FIXTURE},async()=>{
 const out=await report(readFileSync(process.env.ANCHOR_MATRIX_FIXTURE),{thresholdMm:0.01,maxDepth:6,maxNodes:256});
 assert.deepEqual(out.counts,{caseCount:15,partCount:30,anchorCount:45});assert.equal(out.results.length,45);
 for(const r of out.results){
  assert.equal(r.inspection.status,'unresolved');const returned=[...r.inspection.excludedEdges,...r.inspection.unresolved.map(x=>x.edgeId)];
  assert.deepEqual([...returned].sort(),[...r.sourceEdgeIds].sort());assert.equal(new Set(returned).size,returned.length);
  assert.equal(r.inspection.maxDepth,6);assert.equal(r.inspection.maxNodes,256);
 }
});
test('caller mutation after invocation cannot replace the validated source snapshot', {skip:!process.env.ANCHOR_MATRIX_FIXTURE},async()=>{
 const original=readFileSync(process.env.ANCHOR_MATRIX_FIXTURE);
 const pending=report(original,{thresholdMm:0,maxDepth:0,maxNodes:1});
 original.fill(0);
 const out=await pending;
 assert.deepEqual(out.counts,{caseCount:15,partCount:30,anchorCount:45});
});
test('CLI creates new output and refuses overwrite without changing it', {skip:!process.env.ANCHOR_MATRIX_FIXTURE},()=>{
 const dir=mkdtempSync(join(tmpdir(),'anchor-matrix-test-')),out=join(dir,'report.json');
 const script=fileURLToPath(new URL('./matrix-oracle-report.mjs',import.meta.url));
 const run=()=>spawnSync(process.execPath,[script,process.env.ANCHOR_MATRIX_FIXTURE,out,'0.01','2','32'],{encoding:'utf8',timeout:60000});
 const first=run();assert.equal(first.status,0,first.stderr);assert.ok(existsSync(out));const before=readFileSync(out);
 const second=run();assert.notEqual(second.status,0);assert.deepEqual(readFileSync(out),before);
 const protectedFile=join(dir,'existing.json');writeFileSync(protectedFile,'preserve');
 const other=spawnSync(process.execPath,[script,process.env.ANCHOR_MATRIX_FIXTURE,protectedFile,'0','0','1'],{encoding:'utf8',timeout:60000});
 assert.notEqual(other.status,0);assert.equal(readFileSync(protectedFile,'utf8'),'preserve');
});
