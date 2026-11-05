import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
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
test('different bytes cannot inherit fixture identity',()=>assert.throws(()=>report(Buffer.from('{}'),{thresholdMm:0,maxDepth:0,maxNodes:1}),/source hash mismatch/));
test('optional pinned 15-case matrix yields 30 panels and 45 anchor reports', {skip:!process.env.ANCHOR_MATRIX_FIXTURE},()=>{
 const out=report(readFileSync(process.env.ANCHOR_MATRIX_FIXTURE),{thresholdMm:0,maxDepth:0,maxNodes:1});
 assert.deepEqual(out.counts,{caseCount:15,partCount:30,anchorCount:45});assert.equal(out.results.length,45);
 for(const r of out.results){assert.equal(r.inspection.status,'unresolved');assert.equal(r.sourceEdgeIds.length,r.inspection.excludedEdges.length+r.inspection.unresolved.length);}
});
