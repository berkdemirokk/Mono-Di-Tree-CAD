import { validateLinearBoundary } from './safety.mjs';
const finitePoint=p=>p&&Number.isFinite(p.x)&&Number.isFinite(p.y);
const same=(a,b)=>a.x===b.x&&a.y===b.y;
const mid=(a,b)=>({x:(a.x+b.x)/2,y:(a.y+b.y)/2});
function distanceToSegment(p,a,b){const dx=b.x-a.x,dy=b.y-a.y,l=dx*dx+dy*dy;if(!Number.isFinite(l)||l===0)return Infinity;const t=Math.max(0,Math.min(1,((p.x-a.x)*dx+(p.y-a.y)*dy)/l));return Math.hypot(p.x-(a.x+t*dx),p.y-(a.y+t*dy));}
// Screening only: even a passing polyline is not a proof of the original curve topology.
export function screenCubicBoundary(segments,{unit,toleranceMm,maxSegments}={}) {
 if(unit!=='mm'||!Number.isFinite(toleranceMm)||toleranceMm<=0||toleranceMm>1||!Number.isInteger(maxSegments)||maxSegments<3||maxSegments>4096)throw new Error('Açık mm, tolerans (0,1] ve 3..4096 segment sınırı gerekli.');
 if(!Array.isArray(segments)||segments.length<3||segments.length>4096||segments[0].type!=='move'||segments.at(-1).type!=='close')throw new Error('Geçersiz sınır.');
 let current=segments[0].to;if(!finitePoint(current))throw new Error('Geçersiz nokta.');const flat=[{type:'move',to:{...current}}];
 function append(p){if(!finitePoint(p)||same(current,p))throw new Error('Sıfır veya geçersiz parça.');if(flat.length>=maxSegments)throw new Error('Segment sınırı aşıldı.');flat.push({type:'line',to:{...p}});current=p;}
 function split(a,b,c,d,depth){const error=Math.max(distanceToSegment(b,a,d),distanceToSegment(c,a,d));if(error<=toleranceMm){append(d);return;}if(depth>=20)throw new Error('Tolerans sağlanamadı.');const ab=mid(a,b),bc=mid(b,c),cd=mid(c,d),abc=mid(ab,bc),bcd=mid(bc,cd),p=mid(abc,bcd);split(a,ab,abc,p,depth+1);split(p,bcd,cd,d,depth+1);}
 for(const s of segments.slice(1,-1)){if(s.type==='line')append(s.to);else if(s.type==='cubic'){if(!finitePoint(s.cp1)||!finitePoint(s.cp2)||!finitePoint(s.to)||same(current,s.to))throw new Error('Geçersiz veya sıfır eğri.');split(current,s.cp1,s.cp2,s.to,0);}else throw new Error('Bilinmeyen işlem.');}
 if(same(current,flat[0].to))throw new Error('Kapanışta sıfır kenar.');
 flat.push({type:'close'});validateLinearBoundary(flat);
 return {classification:'screened-only',topologyProven:false,transferable:false,unit,toleranceMm,segmentCount:flat.length-1};
}
// Applies an explicitly supplied SVG affine matrix to a diagnostic path; no metadata is transferred.
export function transformDiagnosticSegments(segments,{unit,matrix}={}) {
 if(unit!=='mm'||!Array.isArray(matrix)||matrix.length!==6||matrix.some(v=>!Number.isFinite(v)))throw new Error('Açık mm ve sonlu dönüşüm matrisi gerekli.');
 const [a,b,c,d,e,f]=matrix,det=a*d-b*c;if(!Number.isFinite(det)||Math.abs(det)<1e-12)throw new Error('Tekil veya belirsiz dönüşüm.');
 const map=p=>{if(!finitePoint(p))throw new Error('Geçersiz nokta.');const q={x:a*p.x+c*p.y+e,y:b*p.x+d*p.y+f};if(!finitePoint(q))throw new Error('Taşan koordinat.');return q;};
 if(!Array.isArray(segments)||segments.length>4096)throw new Error('Geçersiz segmentler.');
 return segments.map(s=>s.type==='close'?{type:'close'}:s.type==='move'||s.type==='line'?{type:s.type,to:map(s.to)}:s.type==='cubic'?{type:'cubic',cp1:map(s.cp1),cp2:map(s.cp2),to:map(s.to)}:(()=>{throw new Error('Bilinmeyen işlem.');})());
}
