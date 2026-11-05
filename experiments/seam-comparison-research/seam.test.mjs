import test from 'node:test';
import assert from 'node:assert/strict';
import { diagnose } from './seam.mjs';

function fixture() {
  const source = {version:'synthetic-v1', hash:'synthetic-hash', draft:'draft-1'};
  const side = (piece, length) => ({piece, draft:source.draft, halfPiece:true, direction:'forward', dartTransform:'not-applicable', edges:[{id:'e1',piece,draft:source.draft,sourceVersion:source.version,sourceHash:source.hash,length}]});
  return {unit:'mm',measurements:{frontArc:350,backArc:370,total:720,frontEase:10,backEase:20},source,role:'frontWaist',sides:[side('frontUpper',[180,181]),side('frontLower',[185,186])],tolerance:2};
}
test('declared asymmetric half widths and interval difference are diagnostic only', () => {
  const result = diagnose(fixture());
  assert.deepEqual(result.halfWaistWidthsMm,[180,195]);
  assert.deepEqual(result.comparison.differenceMm,[-6,-4]);
  assert.equal(result.comparison.disjointBeyondDeclaredTolerance,true);
  assert.equal(result.productionApproved,false);
  assert.ok(Object.isFrozen(result.sides[0].edges[0].length));
});
test('centimeters and millimeters produce the same diagnostic', () => {
  const mm = fixture(), cm = fixture(); cm.unit='cm';
  for (const k of Object.keys(cm.measurements)) cm.measurements[k]/=10;
  cm.tolerance/=10; for (const s of cm.sides) s.edges[0].length=s.edges[0].length.map(x=>x/10);
  assert.deepEqual(diagnose(cm).comparison,diagnose(mm).comparison);
  assert.deepEqual(diagnose(cm).halfWaistWidthsMm,diagnose(mm).halfWaistWidthsMm);
});
test('missing explicit evidence and inconsistent arcs stay unresolved', () => {
  for (const mutate of [v=>delete v.source.hash,v=>delete v.sides[0].direction,v=>delete v.sides[0].dartTransform,v=>delete v.tolerance,v=>v.measurements.total++,v=>v.sides[1].edges=[],v=>v.sides[0].halfPiece=false,v=>delete v.sides[0].edges[0].sourceHash]) {
    const v=fixture(); mutate(v); const r=diagnose(v); assert.equal(r.status,'unresolved'); assert.equal(r.comparison,null);
  }
});
test('different draft, role, source, or duplicate edge is rejected', () => {
  for (const mutate of [v=>v.sides[0].draft='other',v=>v.sides[0].edges[0].draft='other',v=>v.sides[0].piece='backUpper',v=>v.sides[0].edges[0].sourceHash='other',v=>v.sides[0].edges.push({...v.sides[0].edges[0]})]) {
    const v=fixture(); mutate(v); assert.throws(()=>diagnose(v));
  }
});
test('boundary contact is not disjoint and result snapshots caller data', () => {
  const v=fixture(); v.tolerance=4; const r=diagnose(v);
  assert.equal(r.comparison.disjointBeyondDeclaredTolerance,false);
  v.sides[0].edges[0].length[0]=999; assert.equal(r.sides[0].edges[0].length[0],180);
});
test('nonfinite, overflow, negative and invalid interval never approve', () => {
  for (const mutate of [v=>v.measurements.frontArc=Infinity,v=>v.measurements.frontEase=-1,v=>v.measurements.frontArc=Number.MAX_VALUE,v=>v.sides[0].edges[0].length=[3,2],v=>v.sides[0].edges[0].length=[-1,2]]) {
    const v=fixture(); mutate(v); try { assert.equal(diagnose(v).status,'unresolved'); } catch (e) { assert.ok(e instanceof Error); }
  }
});
