import {lstat,readFile,readdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
import {createDiagnosticManifest} from './distribution-manifest.mjs';

// Research only: these historical records are assertions, never authentication.
export function parseLockedPackages(text) {
 if(typeof text!=='string'||Buffer.byteLength(text)>2_000_000||!text.startsWith("lockfileVersion: '9.0'"))throw Error('unsupported lock');
 const block=text.split('\npackages:\n')[1]?.split('\nsnapshots:\n')[0];if(!block)throw Error('missing package block');
 const rows=[...block.matchAll(/^  (?:'([^']+)'|([^:\s]+)):\s*\r?\n\s+resolution: \{integrity: (sha512-[A-Za-z0-9+/=]+)\}/gm)];
 const result=rows.map(m=>{const key=m[1]||m[2],i=key.lastIndexOf('@'),name=key.slice(0,i),version=key.slice(i+1);if(i<=0||!/^(@[a-z0-9._-]+\/)?[a-z0-9._-]+$/.test(name)||!/^\d+\.\d+\.\d+$/.test(version))throw Error('unsupported package key');return {name,version,integrity:m[3]};});
 const declared=[...block.matchAll(/^  (?:'([^']+)'|([^:\s]+)):\s*$/gm)];
 if(!result.length||result.length!==declared.length||new Set(result.map(x=>x.name)).size!==result.length)throw Error('missing or duplicate locked package');
 return result;
}
const fileHash=b=>createHash('sha256').update(b).digest('hex');
async function plainFile(p,max=64*1024*1024) {const s=await lstat(p);if(!s.isFile()||s.isSymbolicLink()||s.nlink!==1||s.size>max)throw Error('unsupported file');const b=await readFile(p);if(b.length!==s.size)throw Error('file changed');return b;}
async function plainDirectory(p) {const s=await lstat(p);if(!s.isDirectory()||s.isSymbolicLink())throw Error('linked or non-directory path');}
export async function inspectPackage(root,entry,provenance=[],tarballs=[]) {
 const issues=[];let manifest=null,packageMetadata=null,noticeFiles=[];
 try {
  await plainDirectory(root);const components=entry.name.split('/');let dir=root;
  for(const component of components){dir=path.join(dir,component);await plainDirectory(dir);}
  manifest=JSON.parse(await createDiagnosticManifest(dir));
  const data=await plainFile(path.join(dir,'package.json'),1024*1024);packageMetadata=JSON.parse(data);
  const recorded=manifest.entries.find(x=>x.type==='file'&&x.path==='package.json');
  if(!recorded||recorded.sha256!==fileHash(data))throw Error('package manifest changed after scan');
  if(packageMetadata.name!==entry.name||packageMetadata.version!==entry.version)issues.push('installed identity differs from lock');
  noticeFiles=manifest.entries.filter(x=>x.type==='file'&&/^(license|licence|copying|notice)(?:[._-].*)?$/i.test(path.posix.basename(x.path))).map(x=>({path:x.path,sha256:x.sha256}));
  if(!noticeFiles.some(x=>/^(license|licence|copying)(?:[._-].*)?$/i.test(path.posix.basename(x.path))))issues.push('package license text absent');
 } catch { issues.push('installed package missing, unstable, unsafe or unreadable'); }
 const prov=provenance.filter(p=>p.name===entry.name&&p.version===entry.version);
 const tar=tarballs.filter(p=>p.name===entry.name);
 if(prov.length!==1 || prov[0].integrity!==entry.integrity)issues.push('registry provenance absent, ambiguous or differs from lock');
 if(tar.length!==1||tar[0].match!==true)issues.push('historical tarball match not recorded');
 issues.push('installed files not compared against authenticated archive');
 issues.push('notice completeness and source authority not established');
 return {name:entry.name,version:entry.version,lockIntegrity:entry.integrity,historicalRegistry:prov.length===1?{integrity:prov[0].integrity,license:prov[0].license??null}:null,historicalTarball:tar.length===1?{declaredMatch:tar[0].match}:null,installedManifest:manifest,installedName:packageMetadata?.name??null,installedVersion:packageMetadata?.version??null,declaredLicense:packageMetadata?.license??null,noticeFiles,issues,status:'unresolved'};
}
export async function report({lockText,packageRoot,provenance=[],tarballs=[],runtimePath=null,runtimeNoticePath=null,expectedCount=20}) {
 const locked=parseLockedPackages(lockText);if(locked.length!==expectedCount)throw Error('unexpected locked package count');
 const rootIssues=[];
 try {
  await plainDirectory(packageRoot);const expected=new Set(locked.map(p=>p.name.split('/')[0]));
  for(const item of await readdir(packageRoot,{withFileTypes:true})) {
   if(!expected.has(item.name))rootIssues.push(`unexpected root entry: ${item.name}`);
   if(item.name.startsWith('@') && expected.has(item.name)) {
    const scoped=path.join(packageRoot,item.name);await plainDirectory(scoped);
    const permitted=new Set(locked.filter(p=>p.name.startsWith(item.name+'/')).map(p=>p.name.split('/')[1]));
    for(const child of await readdir(scoped))if(!permitted.has(child))rootIssues.push(`unexpected scoped entry: ${item.name}/${child}`);
   }
  }
 } catch { rootIssues.push('package root or scope unsafe or unreadable'); }
 const packages=[];for(const entry of locked)packages.push(await inspectPackage(packageRoot,entry,provenance,tarballs));
 const runtime={path:runtimePath,sha256:null,notice:null,issues:['binary origin, notice completeness and runtime dependencies unverified'],status:'unresolved'};
 try {if(runtimePath)runtime.sha256=fileHash(await plainFile(runtimePath));else runtime.issues.push('runtime missing');}catch{runtime.issues.push('runtime unsafe or unreadable');}
 try {if(runtimeNoticePath)runtime.notice={path:runtimeNoticePath,sha256:fileHash(await plainFile(runtimeNoticePath,4*1024*1024))};else runtime.issues.push('runtime notice missing');}catch{runtime.issues.push('runtime notice unsafe or unreadable');}
 return {scope:'offline research only; historical records are not authenticated',rootIssues,packages,runtime,executionAuthorized:false,distributionAuthorized:false,personalDataAuthorized:false};
}
