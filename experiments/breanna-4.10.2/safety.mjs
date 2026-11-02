// Conservative diagnostic only. Cubic topology is not approximated or certified here.
export function validateLinearBoundary(segments) {
 if(!Array.isArray(segments)||segments.length<5||segments[0].type!=='move'||segments.at(-1).type!=='close') throw new Error('Kapalı sınır gerekli.');
 if(segments.slice(1,-1).some(s=>s.type!=='line')) throw new Error('Eğri veya bilinmeyen işlem: topoloji doğrulanamadı.');
 const points=segments.slice(0,-1).map(s=>s.to);
 if(points.some(p=>!p||!Number.isFinite(p.x)||!Number.isFinite(p.y)))throw new Error('Geçersiz koordinat.');
 const orient=(a,b,c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
 const eps=1e-9;let area=0;const n=points.length;
 for(let i=0;i<n;i++){const a=points[i],b=points[(i+1)%n];if(Math.hypot(a.x-b.x,a.y-b.y)<=eps)throw new Error('Sıfır veya belirsiz kenar.');area+=a.x*b.y-b.x*a.y;}
 for(let i=0;i<n;i++) { const t=orient(points[(i+n-1)%n],points[i],points[(i+1)%n]);if(!Number.isFinite(t)||Math.abs(t)<=eps)throw new Error('Bitişik kenar ilişkisi belirsiz.'); }
 if(!Number.isFinite(area)||Math.abs(area)<=eps)throw new Error('Sıfır veya belirsiz alan.');
 for(let i=0;i<n;i++)for(let j=i+1;j<n;j++){
  if(j===i+1||(i===0&&j===n-1))continue;
  const a=points[i],b=points[(i+1)%n],c=points[j],d=points[(j+1)%n];
  const o=[orient(a,b,c),orient(a,b,d),orient(c,d,a),orient(c,d,b)];
  if(o.some(v=>!Number.isFinite(v)||Math.abs(v)<=eps))throw new Error('Belirsiz kenar ilişkisi.');
  if((o[0]>0)!==(o[1]>0)&&(o[2]>0)!==(o[3]>0))throw new Error('Kendini kesen sınır.');
 }
 return true;
}
export function requireTransferable() { throw new Error('Aktarım reddedildi: çentik/metadata, dönüşüm ve eğri topolojisi doğrulanmadı.'); }
