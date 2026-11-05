// Exact arithmetic for encoded binary64 inputs; not an anatomical or sewing oracle.
const MAX_EDGES=1024, MAX_NODES=50000, MAX_DEPTH=32, MAX_ABS=1e12;
const buffer=new ArrayBuffer(8), view=new DataView(buffer);
function units(x) {
  if(typeof x!=='number'||!Number.isFinite(x)||Math.abs(x)>MAX_ABS)throw new Error('unsupported coordinate');
  view.setFloat64(0,x,false);const bits=view.getBigUint64(0,false);
  const exponent=Number((bits>>52n)&2047n), fraction=bits&((1n<<52n)-1n);
  let n=exponent===0?fraction:((1n<<52n)|fraction) << BigInt(exponent-1);
  if(bits>>63n)n=-n;return n;
}
function point(p) { if(!p||typeof p!=='object')throw new Error('invalid point');return [units(p.x),units(p.y)]; }
// All controls share denominator 2^(1074+shift); splitting a degree-n curve
// increases shift by n. Midpoints are represented exactly.
function split(p) {
  const layers=[p];
  while(layers.at(-1).length>1) {
    const q=layers.at(-1);layers.push(q.slice(1).map((v,i)=>[q[i][0]+v[0],q[i][1]+v[1]]));
  }
  const degree=p.length-1;
  const left=layers.map((q,i)=>q[0].map(v=>v<<BigInt(degree-i)));
  const right=layers.map((q,i)=>q.at(-1).map(v=>v<<BigInt(degree-i))).reverse();
  // Above values share 2^(1074+d+degree), which is inconvenient for mixed degrees.
  return [left,right,degree];
}
function excluded(controls,anchor,threshold,shift) {
  const ax=anchor[0]<<BigInt(shift),ay=anchor[1]<<BigInt(shift),t=threshold<<BigInt(shift);
  const xs=controls.map(p=>p[0]-ax),ys=controls.map(p=>p[1]-ay);
  const distance=values=>{let lo=values[0],hi=values[0];for(const v of values){if(v<lo)lo=v;if(v>hi)hi=v;}return lo>0n?lo:hi<0n?-hi:0n;};
  const dx=distance(xs),dy=distance(ys);return dx*dx+dy*dy>t*t;
}
export function inspectDyadic(anchor,edges,{thresholdMm,maxDepth,maxNodes}) {
  const a=point(anchor),t=units(thresholdMm);
  if(t<0n||!Array.isArray(edges)||edges.length>MAX_EDGES||!Number.isInteger(maxDepth)||maxDepth<0||maxDepth>MAX_DEPTH||!Number.isInteger(maxNodes)||maxNodes<1||maxNodes>MAX_NODES)throw new Error('invalid limits');
  const ids=new Set();const captured=edges.map(e=>{
    if(!e||typeof e.id!=='string'||!e.id||e.id.length>128||ids.has(e.id)||!['line','close','curve'].includes(e.type))throw new Error('invalid edge');
    ids.add(e.id);return {id:e.id,p:(e.type==='curve'?[e.from,e.cp1,e.cp2,e.to]:[e.from,e.to]).map(point)};
  });
  let nodes=0;const excludedEdges=[],unresolved=[];
  for(const edge of captured) {
    if(nodes>=maxNodes){unresolved.push({edgeId:edge.id,reason:'not_examined_node_budget'});continue;}
    if(edge.p.every(p=>p[0]===edge.p[0][0]&&p[1]===edge.p[0][1])){unresolved.push({edgeId:edge.id,reason:'zero_length_edge'});continue;}
    let stack=[{p:edge.p,shift:0,depth:0}],pending=false;
    while(stack.length) {
      if(nodes>=maxNodes){pending=true;break;}nodes++;
      const node=stack.pop();
      if(excluded(node.p,a,t,node.shift))continue;
      if(node.depth>=maxDepth){pending=true;continue;}
      const [l,r,degree]=split(node.p);
      stack.push({p:r,shift:node.shift+degree,depth:node.depth+1},{p:l,shift:node.shift+degree,depth:node.depth+1});
    }
    if(pending)unresolved.push({edgeId:edge.id,reason:'overlap_or_budget'});else excludedEdges.push(edge.id);
  }
  return {status:'unresolved',scope:'exact encoded binary64 control-box exclusion only; not a sewing or production decision',thresholdMm,maxDepth,maxNodes,nodes,excludedEdges,unresolved};
}
