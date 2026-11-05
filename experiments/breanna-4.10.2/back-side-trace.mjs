import { createHash } from 'node:crypto';

// Version-specific source observation: back.mjs builds seam .line(waist).line(armhole)
// and stores backSideSeamLength as the distance of those points. Name/coordinate
// matching in the evidence is a candidate, not a verified operation provenance.
export const MATRIX_SHA256='a68c9d9184ccbe40b0eaeb8e18c58ab75c4a16b5ef03fd9e26d2866837330c1b';
export const BACK_SHA256='2eb13d29c5549ef18cc8180ed751f4d50635a91e408526c9fbed3de3e5d9edb0';
const digest=b=>createHash('sha256').update(b).digest('hex');
const point=p=>p && Number.isFinite(p.x) && Number.isFinite(p.y);
const has=(names,name)=>Array.isArray(names) && names.length===1 && names[0]===name;

export function traceCase(c) {
 const unresolved=reason=>({status:'unresolved',reason,candidate:null});
 if(!c || c.status!=='inspected' || typeof c.body!=='string' || !c.body.trim() || typeof c.variant!=='string' || !c.variant.trim() || !c.options || typeof c.options!=='object' || Array.isArray(c.options) || typeof c.options.waistDart!=='boolean' || typeof c.options.shoulderDart!=='boolean') return unresolved('missing case or options');
 if(!Array.isArray(c.parts)) return unresolved('missing parts');
 const parts=c.parts.filter(p=>p?.part==='breanna.back');
 if(parts.length!==1 || !Array.isArray(parts[0].edges)) return unresolved('back part ambiguous');
 const edges=parts[0].edges;
 if(edges.some(e=>!e || typeof e.id!=='string' || !Number.isSafeInteger(e.sourceOpIndex) || e.sourceOpIndex<0 || e.id!==`breanna.back/seam/op-${e.sourceOpIndex}`)) return unresolved('invalid edge identity');
 if(new Set(edges.map(e=>e.id)).size!==edges.length) return unresolved('duplicate edge identity');
 const matches=edges.filter(e=>e.type==='line' && has(e.fromNames,'waist') && has(e.toNames,'armhole'));
 if(matches.length!==1) return unresolved('endpoint match missing or ambiguous');
 const e=matches[0];
 if(!point(e.from)||!point(e.to) || (e.from.x===e.to.x && e.from.y===e.to.y) || e.exactZero!==false) return unresolved('invalid or degenerate edge');
 const length=Math.hypot(e.to.x-e.from.x,e.to.y-e.from.y);
 if(!Number.isFinite(length)||length===0) return unresolved('invalid length');
 return {status:'unresolved',reason:'source-consistent candidate only; operation provenance, seam correspondence and physical meaning unverified',candidate:{part:'breanna.back',edgeId:e.id,sourceOpIndex:e.sourceOpIndex,direction:'waist-to-armhole',from:{...e.from},to:{...e.to},lengthMm:length,lengthEvidence:'binary64 diagnostic, not certified'}};
}

export function tracePinnedMatrix(input) {
 const bytes=Buffer.from(input); // private snapshot before both hash and parse
 if(digest(bytes)!==MATRIX_SHA256) throw new Error('unexpected matrix hash');
 const matrix=JSON.parse(bytes.toString('utf8'));
 if(matrix.sourceVersion!=='4.10.2' || matrix.sourceHashes?.['node_modules/@freesewing/breanna/src/back.mjs']!==BACK_SHA256 || matrix.coordinateSpace!=='part local mm; placements not applied' || !Array.isArray(matrix.cases)) throw new Error('unexpected provenance');
 const seen=new Set();
 const cases=matrix.cases.map(c=>{
   const key=JSON.stringify([c.body,c.variant]); if(seen.has(key)) throw new Error('duplicate draft'); seen.add(key);
   return {body:c.body,variant:c.variant,options:structuredClone(c.options),...traceCase(c)};
 });
 return {scope:'synthetic research only',matrixSha256:MATRIX_SHA256,sourceVersion:matrix.sourceVersion,backSourceSha256:BACK_SHA256,coordinateSpace:matrix.coordinateSpace,cases,geometryExportAllowed:false,productionAllowed:false,deviceAllowed:false};
}
