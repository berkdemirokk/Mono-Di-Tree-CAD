import {open,lstat,readdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {join} from 'node:path';
const LIMIT_FILES=4096, LIMIT_BYTES=256*1024*1024, MAX_FILE=64*1024*1024, MAX_DEPTH=32;
const validName=n=>/^[A-Za-z0-9_@+.-]{1,200}$/.test(n)&&!/[. ]$/.test(n)&&!/^\.{1,2}$/.test(n)&&!/^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(n);
const fingerprint=s=>[s.dev,s.ino,s.size,s.mtimeNs,s.ctimeNs,s.nlink].map(String).join(':');
function same(a,b){if(fingerprint(a)!==fingerprint(b))throw Error('Kaynak doğrulama sırasında değişti.');}
async function scan(root,hook){let count=0,total=0;const entries=[],folded=new Set();
 async function visit(abs,rel,depth){if(depth>MAX_DEPTH)throw Error('Dizin derinliği sınırı.');const before=await lstat(abs,{bigint:true});if(before.isSymbolicLink())throw Error('Bağlantı veya reparse kabul edilmez.');
  if(before.isDirectory()) {if(rel)entries.push({path:rel,type:'directory'});const names=(await readdir(abs)).sort();for(const name of names){if(!validName(name))throw Error('Güvensiz ad.');const path=rel?`${rel}/${name}`:name,fold=path.toLowerCase();if(folded.has(fold))throw Error('Ad çakışması.');folded.add(fold);await visit(join(abs,name),path,depth+1);}same(before,await lstat(abs,{bigint:true}));return;}
  if(!before.isFile()||before.nlink!==1n||before.size>BigInt(MAX_FILE))throw Error('Desteklenmeyen dosya veya sabit bağlantı.');if(++count>LIMIT_FILES||(total+=Number(before.size))>LIMIT_BYTES)throw Error('Kaynak bütçesi aşıldı.');
  const fd=await open(abs,'r');try{same(before,await fd.stat({bigint:true}));const hash=createHash('sha256'),buffer=Buffer.allocUnsafe(65536);let offset=0;while(true){const {bytesRead}=await fd.read(buffer,0,buffer.length,offset);if(!bytesRead)break;offset+=bytesRead;if(offset>MAX_FILE)throw Error('Dosya büyüdü.');hash.update(buffer.subarray(0,bytesRead));}same(before,await fd.stat({bigint:true}));if(BigInt(offset)!==before.size)throw Error('Eksik dosya.');entries.push({path:rel,type:'file',size:offset,sha256:hash.digest('hex')});}finally{await fd.close();}
  if(hook)await hook(rel);same(before,await lstat(abs,{bigint:true}));
 }
 await visit(root,'',0);entries.sort((a,b)=>a.path<b.path?-1:a.path>b.path?1:0);return {schema:'tekstilcad.experimental.distribution-manifest.v1',hash:'sha256',entries};
}
export async function createDiagnosticManifest(root,{afterFileForTest}={}){const first=await scan(root,afterFileForTest),second=await scan(root);if(JSON.stringify(first)!==JSON.stringify(second))throw Error('Kaynak taramalar arasında değişti.');return JSON.stringify(first);}
export async function verifyDiagnosticManifest(root,manifest,{afterFileForTest}={}){if(typeof manifest!=='string'||Buffer.byteLength(manifest)>4*1024*1024)throw Error('Manifest boyutu.');let expected;try{expected=JSON.parse(manifest);}catch{throw Error('Manifest biçimi.');}if(JSON.stringify(expected)!==manifest||!expected||Object.keys(expected).join(',')!=='schema,hash,entries'||expected.schema!=='tekstilcad.experimental.distribution-manifest.v1'||expected.hash!=='sha256'||!Array.isArray(expected.entries))throw Error('Manifest şeması.');const actual=await createDiagnosticManifest(root,{afterFileForTest});if(actual!==manifest)throw Error('Eksik, ek veya değiştirilmiş içerik.');return {matches:true,authenticated:false,executionAuthorized:false};}
