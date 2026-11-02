import test from 'node:test';import assert from 'node:assert/strict';import {validateLinearBoundary,requireTransferable} from './safety.mjs';
const path=points=>[{type:'move',to:points[0]},...points.slice(1).map(to=>({type:'line',to})),{type:'close'}];
test('sıfır kenar ve kendini kesen sınır reddedilir',()=>{assert.throws(()=>validateLinearBoundary(path([{x:0,y:0},{x:4,y:0},{x:4,y:0},{x:0,y:4}])));assert.throws(()=>validateLinearBoundary(path([{x:0,y:0},{x:4,y:4},{x:0,y:4},{x:4,y:0}])));});
test('eğri topolojisi ve aktarım doğrulanmadan reddedilir',()=>{assert.throws(()=>validateLinearBoundary([{type:'move',to:{x:0,y:0}},{type:'cubic',to:{x:1,y:1}},{type:'line',to:{x:2,y:0}},{type:'line',to:{x:1,y:-1}},{type:'close'}]));assert.throws(()=>requireTransferable(),/Aktarım reddedildi/);});
test('basit doğrusal dörtgen kabul edilir',()=>assert.equal(validateLinearBoundary(path([{x:0,y:0},{x:4,y:0},{x:4,y:3},{x:0,y:3}])),true));
