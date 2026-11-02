import { Breanna } from '@freesewing/breanna';
export const measurementNames = Object.freeze(['biceps','bustFront','bustSpan','highBust','highBustFront','hpsToBust','hpsToWaistBack','hpsToWaistFront','waist','waistToHips','neck','shoulderToShoulder','shoulderSlope','shoulderToWrist','wrist']);
const definitions=Breanna.patternConfig.options;
export const optionDefinitions=Object.freeze(Object.fromEntries(Object.entries(definitions).filter(([,v])=>v && typeof v==='object').map(([k,v])=>[k,Object.freeze({ ...v })])));
function validateOptions(options) {
 if (!options || typeof options!=='object' || Array.isArray(options)) throw new TypeError('Tüm model ve bolluk seçenekleri açıkça gerekli.');
 const bad=[];
 for(const [key,d] of Object.entries(optionDefinitions)) {
  const v=options[key];
  if ('pct' in d) { if(typeof v!=='number'||!Number.isFinite(v)||v<d.min/100||v>d.max/100) bad.push(key); }
  else if ('bool' in d) { if(typeof v!=='boolean') bad.push(key); }
  else if ('list' in d) { if(!d.list.includes(v)) bad.push(key); }
  else bad.push(key);
 }
 const extras=Object.keys(options).filter(k=>!(k in optionDefinitions));
 if(bad.length||extras.length) throw new RangeError(`Eksik/geçersiz seçenek: ${[...bad,...extras].join(', ')}`);
 return Object.fromEntries(Object.keys(optionDefinitions).map(k=>[k,options[k]]));
}
const allowedParts=['breanna.front','breanna.back','breanna.sleeve'];
function copyPoint(p) { if(!p || !Number.isFinite(p.x)||!Number.isFinite(p.y)) throw new Error('Sonlu olmayan nokta.'); return {x:p.x,y:p.y}; }
export function toIntermediate(parts) {
 const output=[]; const losses=[];
 const knownParts=new Set([...allowedParts,'breanna.base','breanna.frontBase','breanna.sleevecap']);
 for(const name of Object.keys(parts||{})) if(!knownParts.has(name)) throw new Error(`Bilinmeyen parça: ${name}`);
 for(const name of allowedParts) {
  const part=parts?.[name]; if(!part) throw new Error(`Eksik parça: ${name}`);
  losses.push({part:name, omittedSnippets:Object.keys(part.snippets||{}), omittedPointNames:Object.keys(part.points||{}), omittedPartAttributes:Object.keys(part.attributes?.list||{}), omittedPathAttributes:Object.fromEntries(Object.entries(part.paths||{}).map(([k,v])=>[k,Object.keys(v.attributes?.list||{})])), layoutTransform:"not inspected or applied", topology:"unverified"});
  const paths=[];
  for(const [key,path] of Object.entries(part.paths||{})) {
   let kind;
   if(key==='seam' && !path.hidden) kind='sewing-boundary';
   else if(key==='saBase' && path.hidden) kind='construction';
   else if(key==='sleevecap' && path.hidden) kind='construction';
   else if(['secondaryBustDartHint','primaryBustDartHint','waistDartHint'].includes(key) && !path.hidden) kind='internal-guide';
   else if(['__macro_cutonfold_cutonfold_line','__macro_grainline_grainline_line','__macro_scalebox_scalebox_imperial','__macro_scalebox_scalebox_metric'].includes(key) && !path.hidden) kind='annotation';
   else throw new Error(`Sınıflandırılamayan yol: ${name}/${key}`);
   const segments=[];
   for(const op of path.ops) {
    if(op.type==='noop') continue;
    if(op.type==='move'||op.type==='line') segments.push({type:op.type,to:copyPoint(op.to)});
    else if(op.type==='curve') segments.push({type:'cubic',cp1:copyPoint(op.cp1),cp2:copyPoint(op.cp2),to:copyPoint(op.to)});
    else if(op.type==='close') segments.push({type:'close'});
    else throw new Error(`Desteklenmeyen yol işlemi: ${op.type}`);
   }
   if(!segments.length || segments[0].type!=='move' || segments.slice(1).some(s=>s.type==='move') || segments.slice(0,-1).some(s=>s.type==='close')) throw new Error(`Geçersiz yol bağlantısı: ${name}/${key}`);
   if(kind==='sewing-boundary' && segments.at(-1).type!=='close') throw new Error(`Açık sınır: ${name}/${key}`);
   paths.push({name:key,kind,segments});
  }
  if(paths.filter(p=>p.kind==='sewing-boundary').length!==1) throw new Error(`Dikiş sınırı belirsiz: ${name}`);
  output.push({name,paths});
 }
 return {purpose:'diagnostic-only',transferable:false,unit:'mm',curve:'cubic-bezier',cutBoundaryAvailable:false,lossManifest:losses,parts:output};
}
export function draftExperimental(measurements, options) {
 if (!measurements || typeof measurements !== 'object' || Array.isArray(measurements)) throw new TypeError('Ölçü nesnesi gerekli.');
 const bad = measurementNames.filter(k => typeof measurements[k] !== 'number' || !Number.isFinite(measurements[k]) || (k === 'shoulderSlope' ? measurements[k] <= -90 || measurements[k] >= 90 : measurements[k] <= 0));
 if (bad.length) throw new RangeError(`Eksik/geçersiz ölçü: ${bad.join(', ')}`);
 const selected=validateOptions(options);
 const clean=Object.fromEntries(measurementNames.map(k=>[k,measurements[k]]));
 const pattern=new Breanna({ measurements:clean,options:selected });pattern.draft();
 const model=toIntermediate(pattern.parts?.[0]);
 const svg=pattern.render();if(typeof svg!=='string'||!svg.includes('<svg')||/(?:NaN|Infinity)/.test(svg))throw new Error('Geçersiz SVG.');
 return {status:'DENEYSEL — üretime onaysız; uzman kontrolü ve numune dikimi gerekir',measurements:clean,options:selected,model,svg};
}
