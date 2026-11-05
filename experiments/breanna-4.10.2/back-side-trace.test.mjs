import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { traceCase,tracePinnedMatrix } from './back-side-trace.mjs';

const fixture=()=>({status:'inspected',body:'synthetic',variant:'one',options:{waistDart:false,shoulderDart:false},parts:[{part:'breanna.back',edges:[{id:'breanna.back/seam/op-4',sourceOpIndex:4,type:'line',fromNames:['waist'],toNames:['armhole'],from:{x:0,y:4},to:{x:3,y:0},exactZero:false}]}]});
test('reports a directed source-consistent candidate without approving a seam',()=>{
 const r=traceCase(fixture());assert.equal(r.status,'unresolved');assert.equal(r.candidate.lengthMm,5);assert.equal(r.candidate.direction,'waist-to-armhole');
});
test('missing, duplicate, reversed, nonfinite and degenerate candidates remain unresolved',()=>{
 for(const edit of [c=>delete c.options.waistDart,c=>c.parts.push(c.parts[0]),c=>c.parts[0].edges.push({...c.parts[0].edges[0]}),c=>c.parts[0].edges[0].fromNames=['armhole'],c=>c.parts[0].edges[0].to.x=NaN,c=>c.parts[0].edges[0].to={...c.parts[0].edges[0].from},c=>c.parts[0].edges[0].fromNames=['waist','other'],c=>c.parts[0].edges[0].id='wrong']) {
  const c=fixture();edit(c);const r=traceCase(c);assert.equal(r.status,'unresolved');assert.equal(r.candidate,null);
 }
});
test('pinned synthetic matrix retains draft and options with closed gates',()=>{
 const path=process.env.BREANNA_MATRIX_EVIDENCE;
 if(!path) throw new Error('Set BREANNA_MATRIX_EVIDENCE to the pinned synthetic matrix-evidence.json');
 const bytes=readFileSync(path), r=tracePinnedMatrix(bytes);
 assert.equal(r.cases.length,15);assert.equal(new Set(r.cases.map(c=>JSON.stringify([c.body,c.variant]))).size,15);
 assert.ok(r.cases.every(c=>c.status==='unresolved' && c.candidate?.direction==='waist-to-armhole'));
 assert.ok(r.cases.some(c=>c.options.waistDart) && r.cases.some(c=>!c.options.waistDart));
 assert.equal(r.geometryExportAllowed,false);assert.equal(r.productionAllowed,false);assert.equal(r.deviceAllowed,false);
 bytes[0]^=1;assert.throws(()=>tracePinnedMatrix(bytes),/hash/);
});
