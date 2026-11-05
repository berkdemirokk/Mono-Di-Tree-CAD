import test from 'node:test';import assert from 'node:assert/strict';
import {mkdtemp,writeFile,mkdir,rm,symlink,readFile} from 'node:fs/promises';import {tmpdir} from 'node:os';import path from 'node:path';
import {parseLockedPackages,inspectPackage,report} from './dependency-blockers.mjs';
const lock="lockfileVersion: '9.0'\n\npackages:\n\n  example@1.0.0:\n    resolution: {integrity: sha512-YWJj}\n\nsnapshots:\n";
const entry={name:'example',version:'1.0.0',integrity:'sha512-YWJj'};
test('lock parser rejects missing, duplicate and unsupported entries',()=>{
 assert.deepEqual(parseLockedPackages(lock),[entry]);assert.throws(()=>parseLockedPackages(lock.replace('sha512-YWJj','sha1-bad')));assert.throws(()=>parseLockedPackages(lock.replace('snapshots:',"  example@1.0.0:\n    resolution: {integrity: sha512-YWJj}\n\nsnapshots:")));
});
test('20 actual lock packages and historical records stay unresolved without claiming authorization',async()=>{
 const lockText=await readFile(new URL('./pnpm-lock.yaml',import.meta.url),'utf8');assert.equal(parseLockedPackages(lockText).length,20);
 const root=await mkdtemp(path.join(tmpdir(),'dep-empty-'));try{const r=await report({lockText,packageRoot:root});assert.equal(r.packages.length,20);assert.ok(r.packages.every(p=>p.status==='unresolved'));assert.equal(r.executionAuthorized,false);assert.equal(r.distributionAuthorized,false);assert.equal(r.personalDataAuthorized,false);}finally{await rm(root,{recursive:true,force:true});}
});
test('synthetic package identifies missing notice, wrong version and observed file hashes',async()=>{
 const root=await mkdtemp(path.join(tmpdir(),'dep-fake-'));try{
  const dir=path.join(root,'example');await mkdir(dir);await writeFile(path.join(dir,'package.json'),JSON.stringify({name:'example',version:'9.0.0',license:'MIT'}));await writeFile(path.join(dir,'data.txt'),'synthetic');
  const p=await inspectPackage(root,entry,[{name:'example',version:'1.0.0',integrity:'sha512-YWJj',license:'MIT'}],[{name:'example',match:true}]);
  assert.ok(p.issues.includes('installed identity differs from lock'));assert.ok(p.issues.includes('package license text absent'));assert.ok(p.installedManifest.entries.some(e=>e.path==='data.txt'&&e.sha256.length===64));assert.equal(p.status,'unresolved');
  await writeFile(path.join(dir,'LICENSE'),'synthetic notice, not legal evidence');const q=await inspectPackage(root,entry);assert.equal(q.noticeFiles.length,1);assert.equal(q.status,'unresolved');
 }finally{await rm(root,{recursive:true,force:true});}
});
test('extra package is explicitly reported and never authorizes execution',async()=>{
 const root=await mkdtemp(path.join(tmpdir(),'dep-extra-'));try{await mkdir(path.join(root,'example'));await writeFile(path.join(root,'example','package.json'),JSON.stringify({name:'example',version:'1.0.0'}));await mkdir(path.join(root,'unexpected'));
 const r=await report({lockText:lock,packageRoot:root,expectedCount:1});assert.ok(r.rootIssues.includes('unexpected root entry: unexpected'));assert.equal(r.executionAuthorized,false);
 }finally{await rm(root,{recursive:true,force:true});}
});
test('linked package is refused without following it',async t=>{
 const root=await mkdtemp(path.join(tmpdir(),'dep-link-'));try{const target=path.join(root,'target');await mkdir(target);try{await symlink(target,path.join(root,'example'),'dir');}catch(e){if(e.code==='EPERM'){t.skip('directory symlink unavailable');return;}throw e;}const p=await inspectPackage(root,entry);assert.equal(p.installedManifest,null);assert.ok(p.issues.includes('installed package missing, unstable, unsafe or unreadable'));}finally{await rm(root,{recursive:true,force:true});}
});
