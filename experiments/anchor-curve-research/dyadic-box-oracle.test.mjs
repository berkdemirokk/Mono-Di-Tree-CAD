import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {inspectDyadic} from './dyadic-box-oracle.mjs';
const p=(x,y)=>({x,y}), line=(id,a,b)=>({id,type:'line',from:a,to:b});
const options={thresholdMm:1,maxDepth:12,maxNodes:10000};
test('strict box exclusion keeps equality and excludes beyond threshold',()=>{
 const edge=line('l',p(0,0),p(10,0));
 assert.deepEqual(inspectDyadic(p(5,1),[edge],options).excludedEdges,[]);
 assert.deepEqual(inspectDyadic(p(5,1.0001),[edge],options).excludedEdges,['l']);
});
test('exact subnormal comparisons and negative zero',()=>{
 const e=line('l',p(-0,Number.MIN_VALUE),p(1,Number.MIN_VALUE));
 assert.deepEqual(inspectDyadic(p(0,0),[e],{...options,thresholdMm:0}).excludedEdges,['l']);
 assert.deepEqual(inspectDyadic(p(0,0),[e],{...options,thresholdMm:Number.MIN_VALUE}).excludedEdges,[]);
});
test('cubic convex hull near, far and boundary endpoints',()=>{
 const e={id:'c',type:'curve',from:p(0,0),cp1:p(0,3),cp2:p(3,3),to:p(3,0)};
 assert.deepEqual(inspectDyadic(p(0,0),[e],{...options,thresholdMm:0}).excludedEdges,[]);
 assert.deepEqual(inspectDyadic(p(9,9),[e],options).excludedEdges,['c']);
});
test('zero and exhausted global budget remain unresolved including unvisited edges',()=>{
 const a=line('a',p(0,0),p(10,0)),b=line('b',p(0,1),p(10,1));
 const r=inspectDyadic(p(5,0),[a,b],{...options,thresholdMm:0,maxNodes:1});
 assert.equal(r.nodes,1);assert.deepEqual(r.unresolved.map(x=>x.reason),['overlap_or_budget','not_examined_node_budget']);
 assert.equal(inspectDyadic(p(0,0),[line('z',p(0,0),p(0,0))],options).unresolved[0].reason,'zero_length_edge');
});
test('out-of-range and nonfinite inputs fail without numeric overflow',()=>{
 for(const x of [Infinity,NaN,1e13,Number.MAX_VALUE])assert.throws(()=>inspectDyadic(p(x,0),[],options));
 assert.throws(()=>inspectDyadic(p(0,0),[],{...options,thresholdMm:-1}));
});
test('optional pinned synthetic fixture retains all shared endpoint edges', {skip:!process.env.ANCHOR_SYNTHETIC_FIXTURE},()=>{
 const bytes=readFileSync(process.env.ANCHOR_SYNTHETIC_FIXTURE);
 assert.equal(createHash('sha256').update(bytes).digest('hex'),'8567578d27a0ed76d3518f86ad06e1c673a096739d23799e81ab4db069fa94f8');
 const fixture=JSON.parse(bytes.toString('utf8').replace(/^\uFEFF/,''));
 assert.equal(fixture.parts.length,2);assert.equal(fixture.parts.reduce((n,p)=>n+p.anchors.length,0),3);
 for(const part of fixture.parts)for(const anchor of part.anchors){
   const r=inspectDyadic(anchor.anchor,part.edges,{thresholdMm:0,maxDepth:12,maxNodes:50000});
   for(const endpoint of anchor.endpointCandidates)assert.ok(!r.excludedEdges.includes(endpoint.edgeId));
 }
});
