// Synthetic research only. IEEE-754 boxes are not outward-rounded certificates.
const finite = p => p && Number.isFinite(p.x) && Number.isFinite(p.y);
const mid = (a,b) => ({x:a.x/2+b.x/2,y:a.y/2+b.y/2});
function split(p) {
  const layers=[p];
  while(layers.at(-1).length>1) { const q=layers.at(-1);layers.push(q.slice(1).map((v,i)=>mid(q[i],v))); }
  return [layers.map(q=>q[0]),layers.map(q=>q.at(-1)).reverse()];
}

// Input edges follow inspectSeam's part-local mm records; source is never modified.
export function inspectAnchor(anchor, edges, {thresholdMm, parameterWidth, maxDepth, maxNodes}) {
  if(!finite(anchor)||!Array.isArray(edges)||!Number.isFinite(thresholdMm)||thresholdMm<0||
     !Number.isFinite(parameterWidth)||parameterWidth<=0||parameterWidth>1||
     !Number.isInteger(maxDepth)||maxDepth<0||maxDepth>40||!Number.isInteger(maxNodes)||maxNodes<1||maxNodes>100000)
    throw new Error('invalid explicit limits or input');
  let nodes=0;const candidates=[],unresolved=[];
  for(const edge of edges) {
    if(typeof edge.id!=='string'||!edge.id||!finite(edge.from)||!finite(edge.to)||!['line','curve','close'].includes(edge.type)) throw new Error('invalid edge');
    const points=edge.type==='curve'?[edge.from,edge.cp1,edge.cp2,edge.to]:[edge.from,edge.to];
    if(points.some(p=>!finite(p)))throw new Error('invalid control point');
    if(points.every(p=>p.x===points[0].x&&p.y===points[0].y)) { unresolved.push({edgeId:edge.id,reason:'zero_length_edge'});continue; }
    const stack=[{p:points,a:0,b:1,depth:0}];let edgeLimit=false;
    while(stack.length) {
      if(++nodes>maxNodes) { edgeLimit=true;break; }
      const {p,a,b,depth}=stack.pop();
      const xs=p.map(q=>q.x-anchor.x),ys=p.map(q=>q.y-anchor.y);
      if([...xs,...ys].some(v=>!Number.isFinite(v))) { edgeLimit=true;break; }
      const dx=Math.max(0,Math.min(...xs),-Math.max(...xs));
      const dy=Math.max(0,Math.min(...ys),-Math.max(...ys));
      // Include a floating-point guard margin; no claim of a rigorous error bound.
      const margin=Number.EPSILON*64*Math.max(1,Math.abs(anchor.x),Math.abs(anchor.y),...p.flatMap(q=>[Math.abs(q.x),Math.abs(q.y)]));
      if(Math.hypot(dx,dy)>thresholdMm+margin)continue;
      if(b-a<=parameterWidth) { candidates.push({edgeId:edge.id,parameter:[a,b]});continue; }
      if(depth>=maxDepth) { edgeLimit=true;continue; }
      const [left,right]=split(p),m=(a+b)/2;
      stack.push({p:right,a:m,b,depth:depth+1},{p:left,a,b:m,depth:depth+1});
    }
    if(edgeLimit)unresolved.push({edgeId:edge.id,reason:'budget_or_precision_limit'});
  }
  return {status:'unresolved',purpose:'synthetic diagnostic candidates only; not certified, not a sewing or production decision',thresholdMm,parameterWidth,nodes,candidates,unresolved};
}
