import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {inspectAnchor} from './anchor-diagnostic.mjs';
const limits={thresholdMm:0.001,parameterWidth:1/4096,maxDepth:16,maxNodes:20000};
const line=(id,a,b)=>({id,type:'line',from:a,to:b});
const curve=(id,a,b,c,d)=>({id,type:'curve',from:a,cp1:b,cp2:c,to:d});
test('line interior and endpoint retain candidate intervals',()=>{
 const edges=[line('a',{x:0,y:0},{x:10,y:0}),line('b',{x:10,y:0},{x:10,y:10})];
 const r=inspectAnchor({x:10,y:0},edges,limits);
 assert.equal(r.status,'unresolved');assert.deepEqual(new Set(r.candidates.map(x=>x.edgeId)),new Set(['a','b']));
 assert.ok(inspectAnchor({x:5,y:0},edges,limits).candidates.some(x=>x.edgeId==='a'));
});
test('cubic with two visits retains both parameter neighborhoods',()=>{
 const e=curve('loop',{x:0,y:0},{x:3,y:3},{x:-3,y:3},{x:0,y:0});
 const r=inspectAnchor({x:0,y:0},[e],limits);
 assert.ok(r.candidates.some(x=>x.parameter[0]===0));assert.ok(r.candidates.some(x=>x.parameter[1]===1));
});
test('zero edge and budget exhaustion fail closed',()=>{
 const z=line('zero',{x:0,y:0},{x:0,y:0});
 assert.equal(inspectAnchor({x:0,y:0},[z],limits).unresolved[0].reason,'zero_length_edge');
 const r=inspectAnchor({x:5,y:0},[line('x',{x:0,y:0},{x:10,y:0})],{...limits,maxNodes:1});
 assert.equal(r.unresolved[0].reason,'budget_or_precision_limit');
});
test('explicit threshold rejects a distant line, never claims certification',()=>{
 const e=line('x',{x:0,y:0},{x:10,y:0});
 assert.equal(inspectAnchor({x:5,y:0.01},[e],limits).candidates.length,0);
 assert.ok(inspectAnchor({x:5,y:0.01},[e],{...limits,thresholdMm:0.02}).candidates.length>0);
 assert.throws(()=>inspectAnchor({x:5,y:0},[e],{...limits,thresholdMm:-1}));
});
test('large translated coordinates retain candidate and do not mutate source',()=>{
 const e=line('x',{x:1e9,y:1e9},{x:1e9+10,y:1e9}),before=JSON.stringify(e);
 assert.ok(inspectAnchor({x:1e9+5,y:1e9},[e],limits).candidates.length>0);
 assert.equal(JSON.stringify(e),before);
});
test('optional existing synthetic seam fixture retains source identities', {skip:!process.env.ANCHOR_SYNTHETIC_FIXTURE},()=>{
 const fixture=JSON.parse(readFileSync(process.env.ANCHOR_SYNTHETIC_FIXTURE,'utf8'));
 const parts=Array.isArray(fixture.parts)?fixture.parts:[];
 assert.ok(parts.length>0);
 for(const part of parts) for(const anchor of part.anchors??[]) {
   const r=inspectAnchor(anchor.anchor,part.edges,limits);
   assert.equal(r.status,'unresolved');
   assert.ok(r.candidates.every(c=>part.edges.some(e=>e.id===c.edgeId)));
 }
});
