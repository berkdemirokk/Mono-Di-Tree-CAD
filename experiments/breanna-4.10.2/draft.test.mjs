import test from 'node:test';
import assert from 'node:assert/strict';
import { draftExperimental, measurementNames } from './draft.mjs';
const sample={biceps:300,bustFront:500,bustSpan:190,highBust:900,highBustFront:450,hpsToBust:270,hpsToWaistBack:420,hpsToWaistFront:450,waist:740,waistToHips:200,neck:370,shoulderToShoulder:390,shoulderSlope:15,shoulderToWrist:580,wrist:160};
test('sentetik ölçülerle ön arka ve kol için sonlu noktalar ve SVG üretir',()=>{
 const result=draftExperimental(sample);
 for(const name of ['breanna.front','breanna.back','breanna.sleeve']) assert.ok(result.partNames.includes(name));
 assert.ok(result.pointCount>0); assert.match(result.svg,/<path\b/); assert.match(result.status,/onaysız/);
});
test('her eksik ölçüyü geometri üretmeden reddeder',()=>{
 for(const key of measurementNames) { const input={...sample}; delete input[key]; assert.throws(()=>draftExperimental(input),new RegExp(key)); }
});
test('sıfır uzunluk, NaN, sonsuzluk ve geçersiz açı reddedilir',()=>{
 for(const input of [{...sample,waist:0},{...sample,neck:NaN},{...sample,wrist:Infinity},{...sample,shoulderSlope:90}]) assert.throws(()=>draftExperimental(input),RangeError);
});
