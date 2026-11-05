// Pinned synthetic matrix only. No garment, sewing or production approval.
import {readFileSync,writeFileSync,openSync,readSync,closeSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {fileURLToPath} from 'node:url';
const SOURCE_SHA='a68c9d9184ccbe40b0eaeb8e18c58ab75c4a16b5ef03fd9e26d2866837330c1b';
const ORACLE_SHA='ee91c77a7561ac1614392f47101e8492935398c2dcfdc96829b6a8718fc859ec';
const sha=b=>createHash('sha256').update(b).digest('hex');
function boundedRead(path) {
 const fd=openSync(path,'r');try {
  const chunks=[];let total=0;const block=Buffer.alloc(65536);
  for(;;) { const n=readSync(fd,block,0,block.length,null);if(!n)break;total+=n;if(total>20_000_000)throw Error('source size limit');chunks.push(Buffer.from(block.subarray(0,n))); }
  return Buffer.concat(chunks,total);
 } finally { closeSync(fd); }
}
const bodies=['synthetic-base','synthetic-scaled','synthetic-distinct'];
const variants=['primary-only','two-darts','shoulder-and-waist','rotated-primary','rotated-two'];
const coordinate='source part-local mm; no placement transform applied';
export function validateMatrix(data) {
 if(!data||data.sourceVersion!=='4.10.2'||data.coordinateSpace!=='part local mm; placements not applied'||!data.sourceHashes||!Array.isArray(data.cases)||data.cases.length!==15)throw Error('invalid matrix header');
 const hashes=Object.entries(data.sourceHashes);
 if(hashes.length!==5||hashes.some(([k,v])=>!k||typeof v!=='string'||!/^[a-f0-9]{64}$/.test(v)))throw Error('invalid source hashes');
 const seen=new Set();let partCount=0,anchorCount=0;
 for(const c of data.cases) {
  const key=JSON.stringify([c.body,c.variant]);
  if(!bodies.includes(c.body)||!variants.includes(c.variant)||seen.has(key)||c.status!=='inspected'||!c.measurements||!c.options||!Array.isArray(c.parts)||c.parts.length!==2)throw Error('invalid case');
  seen.add(key);const partIds=new Set();
  for(const p of c.parts) {
   if(!['breanna.front','breanna.back'].includes(p.part)||partIds.has(p.part)||p.coordinateSpace!==coordinate||p.transferable!==false||!Array.isArray(p.edges)||!p.edges.length||!Array.isArray(p.anchors))throw Error('invalid part');
   partIds.add(p.part);partCount++;const ids=new Set();
   for(const e of p.edges) {
    if(typeof e.id!=='string'||!Number.isInteger(e.sourceOpIndex)||e.sourceOpIndex<0||e.id!==`${p.part}/seam/op-${e.sourceOpIndex}`||ids.has(e.id))throw Error('invalid edge identity');ids.add(e.id);
   }
   const expected=p.part==='breanna.front'?['armholePitch','bustNotch']:['armholePitch'];
   if(p.anchors.length!==expected.length||new Set(p.anchors.map(a=>a.name)).size!==expected.length||p.anchors.some(a=>!expected.includes(a.name)))throw Error('invalid anchor identity');
   anchorCount+=p.anchors.length;
  }
 }
 if(seen.size!==15||partCount!==30||anchorCount!==45)throw Error('incomplete matrix');
 return {caseCount:seen.size,partCount,anchorCount};
}
export async function report(bytes,limits) {
 if(!Buffer.isBuffer(bytes)||bytes.length>20_000_000)throw Error('source hash mismatch');
 const snapshot=Buffer.from(bytes);
 const capturedLimits={thresholdMm:limits.thresholdMm,maxDepth:limits.maxDepth,maxNodes:limits.maxNodes};
 if(sha(snapshot)!==SOURCE_SHA)throw Error('source hash mismatch');
 const oracleBytes=readFileSync(fileURLToPath(new URL('./dyadic-box-oracle.mjs',import.meta.url)));
 if(sha(oracleBytes)!==ORACLE_SHA)throw Error('oracle hash mismatch');
 // Import precisely the bytes whose digest was checked, not the mutable path.
 const {inspectDyadic}=await import('data:text/javascript;base64,'+oracleBytes.toString('base64'));
 const data=JSON.parse(snapshot.toString('utf8').replace(/^\uFEFF/,''));const counts=validateMatrix(data);
 const results=[];
 for(const c of data.cases)for(const part of c.parts)for(const anchor of part.anchors) {
  const inspection=inspectDyadic(anchor.anchor,part.edges,capturedLimits);
  const returned=[...inspection.excludedEdges,...inspection.unresolved.map(x=>x.edgeId)];
  const expected=part.edges.map(e=>e.id);
  if(returned.length!==expected.length||new Set(returned).size!==expected.length||returned.some(id=>!expected.includes(id)))throw Error('incomplete oracle edge result');
  results.push({body:c.body,variant:c.variant,sourceVersion:data.sourceVersion,partId:part.part,coordinateSpace:part.coordinateSpace,anchorName:anchor.name,sourceEdgeIds:part.edges.map(e=>e.id),inspection});
 }
 return {scope:'synthetic only; no sewing role, fit, CAD or production approval',sourceSha256:SOURCE_SHA,oracleSha256:ORACLE_SHA,sourceHashes:data.sourceHashes,limits:capturedLimits,counts,results};
}
if(process.argv[1]&&fileURLToPath(import.meta.url)===process.argv[1]) {
 if(process.argv.length!==7)throw Error('usage: node matrix-oracle-report.mjs input.json new-output.json threshold-mm max-depth max-nodes');
 const [input,output,threshold,depth,nodes]=process.argv.slice(2);
 const bytes=boundedRead(input);const result=await report(bytes,{thresholdMm:Number(threshold),maxDepth:Number(depth),maxNodes:Number(nodes)});
 writeFileSync(output,JSON.stringify(result,null,2)+'\n',{flag:'wx'});
}
