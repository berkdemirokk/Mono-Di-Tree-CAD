// Synthetic diagnostics only. No result is a topology proof or permission to transfer.
const roles=['breanna.front','breanna.back','breanna.sleeve'];
const same=(a,b)=>a.x===b.x&&a.y===b.y;
const point=p=>{if(!p||!Number.isFinite(p.x)||!Number.isFinite(p.y)||Math.abs(p.x)>1e6||Math.abs(p.y)>1e6)throw Error('Invalid point');return {x:p.x,y:p.y}};
const mid=(a,b)=>({x:(a.x+b.x)/2,y:(a.y+b.y)/2});
function distance(p,a,b){const dx=b.x-a.x,dy=b.y-a.y,l=dx*dx+dy*dy;if(l===0)return Math.hypot(p.x-a.x,p.y-a.y);const t=Math.max(0,Math.min(1,((p.x-a.x)*dx+(p.y-a.y)*dy)/l));return Math.hypot(p.x-a.x-t*dx,p.y-a.y-t*dy)}
function orientation(a,b,c){return (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x)}
function hit(a,b,c,d){const cross=(u,v,w,z)=>{const x=orientation(u,v,w),y=orientation(u,v,z);return (x<=0&&y>=0)||(x>=0&&y<=0)};return Math.max(Math.min(a.x,b.x),Math.min(c.x,d.x))<=Math.min(Math.max(a.x,b.x),Math.max(c.x,d.x))&&Math.max(Math.min(a.y,b.y),Math.min(c.y,d.y))<=Math.min(Math.max(a.y,b.y),Math.max(c.y,d.y))&&cross(a,b,c,d)&&cross(c,d,a,b)}
export function inspectTopology(parts,{unit,toleranceMm,maxEdges=2048}={}){
 if(unit!=='mm'||!Number.isFinite(toleranceMm)||toleranceMm<=0||toleranceMm>1||!Number.isInteger(maxEdges)||maxEdges<3||maxEdges>2048)throw Error('Invalid limits');
 if(!Array.isArray(parts)||parts.length!==3||new Set(parts.map(p=>p?.name)).size!==3||parts.some(p=>!roles.includes(p?.name)))throw Error('Invalid piece identity');
 const results=parts.map(part=>{
  const seams=part.paths?.filter(p=>p.kind==='sewing-boundary');if(!seams||seams.length!==1)throw Error('Invalid contour count');const s=seams[0].segments;
  if(!Array.isArray(s)||s.length<3||s.length>4096||s[0]?.type!=='move'||s.at(-1)?.type!=='close'||s.slice(1,-1).some(x=>x.type==='move'||x.type==='close'))throw Error('Open or multiple contours');
  const start=point(s[0].to);let current=start;const edges=[];let maxError=0,zeroLines=0,zeroCubics=0,cubicCount=0;
  function edge(a,b,error){if(edges.length>=maxEdges)throw Error('Flattening budget exceeded');edges.push({a,b,error});maxError=Math.max(maxError,error)}
  function flatten(a,b,c,d,depth){const err=Math.max(distance(b,a,d),distance(c,a,d));if(!Number.isFinite(err))throw Error('Invalid error bound');if(err<=toleranceMm){edge(a,d,err);return}if(depth>=20)throw Error('Flattening depth exceeded');const ab=mid(a,b),bc=mid(b,c),cd=mid(c,d),abc=mid(ab,bc),bcd=mid(bc,cd),m=mid(abc,bcd);flatten(a,ab,abc,m,depth+1);flatten(m,bcd,cd,d,depth+1)}
  for(const op of s.slice(1,-1)){if(op.type==='line'){const end=point(op.to);if(same(current,end))zeroLines++;edge(current,end,0);current=end}else if(op.type==='cubic'){const b=point(op.cp1),c=point(op.cp2),end=point(op.to);if(same(current,b)&&same(current,c)&&same(current,end))zeroCubics++;cubicCount++;flatten(current,b,c,end,0);current=end}else throw Error('Unsupported command')}
  const closingLength=Math.hypot(current.x-start.x,current.y-start.y);if(closingLength>0)edge(current,start,0);
  const nonzero=edges.filter(e=>!same(e.a,e.b));let intersections=0,nearPairs=0;for(let i=0;i<nonzero.length;i++)for(let j=i+1;j<nonzero.length;j++){if(j===i+1||(i===0&&j===nonzero.length-1))continue;const a=nonzero[i],b=nonzero[j];if(hit(a.a,a.b,b.a,b.b)){intersections++;continue}const sep=Math.min(distance(a.a,b.a,b.b),distance(a.b,b.a,b.b),distance(b.a,a.a,a.b),distance(b.b,a.a,a.b));if(sep<=a.error+b.error)nearPairs++}
  return {name:part.name,contours:1,explicitClose:true,closingLengthMm:closingLength,zeroLines,zeroCubics,cubicCount,flattenedEdges:edges.length,maxControlDistanceMm:maxError,polylineIntersectionPairs:intersections,nearPairsWithinError:nearPairs};
 });return {classification:'diagnostic-only',topologyProven:false,transferable:false,productionApproved:false,unit,toleranceMm,results};
}
