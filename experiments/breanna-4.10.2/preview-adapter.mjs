import {draftExperimental,measurementNames} from './draft.mjs';
const validSnippets=new Set(['logo','bustNotch','armholePitch','frontNotch','backNotch']);
const validAttrs=new Set(['class','marker-start','marker-end','data-text','data-text-class']);
export function checkKnownOmissions(losses) {
 if(!Array.isArray(losses)||losses.length!==3)throw new Error('Eksik kayıp manifesti.');
 for(const item of losses){if(item.omittedPartAttributes.length)throw new Error('Bilinmeyen parça özniteliği.');for(const name of item.omittedSnippets)if(!validSnippets.has(name))throw new Error(`Bilinmeyen işaret: ${name}`);for(const attrs of Object.values(item.omittedPathAttributes))for(const name of attrs)if(!validAttrs.has(name))throw new Error(`Bilinmeyen yol özniteliği: ${name}`);}
}
export function createPreview(input) {
 if(!input||Object.keys(input).some(k=>!['unit','angleUnit','layout','toleranceMm','measurements','options'].includes(k))||input.unit!=='mm'||input.angleUnit!=='degree'||input.layout!=='local-unplaced'||!Number.isFinite(input.toleranceMm)||input.toleranceMm<=0||input.toleranceMm>1)throw new Error('Birim, derece, yerel yerleşim ve açık tolerans gerekli.');
 const m=input.measurements;if(!m||typeof m!=='object'||Object.keys(m).length!==measurementNames.length||measurementNames.some(k=>!Object.hasOwn(m,k)))throw new Error('Tam 15 açık ölçü gerekli.');
 if(!Number.isFinite(m.highBustFront)||!Number.isFinite(m.highBust)||m.highBustFront>=m.highBust)throw new Error('Ön üst göğüs toplam üst göğüsten küçük olmalı.');
 if(!Number.isFinite(m.hpsToBust)||!Number.isFinite(m.hpsToWaistFront)||m.hpsToBust>=m.hpsToWaistFront)throw new Error('Göğüs yüksekliği ön bel uzunluğundan küçük olmalı.');
 const result=draftExperimental(m,input.options);checkKnownOmissions(result.model.lossManifest);
 const serial=JSON.stringify({schema:'tekstilcad.experimental.bodice-request.v1',warning:'DENEYSEL ÖNİZLEME; üretim, kesim ve plotter için uygun değildir',input,preview:result.model});
 if(Buffer.byteLength(serial,'utf8')>1024*1024)throw new Error('Önizleme boyut sınırı.');return serial;
}
// Recreate instead of trusting stored geometry or authorization flags. No file IO.
export function reopenPreview(text) {
 if(typeof text!=='string'||Buffer.byteLength(text,'utf8')>1024*1024)throw new Error('Geçersiz önizleme boyutu.');
 const stored=JSON.parse(text);
 if(!stored || typeof stored!=='object' || Array.isArray(stored) || Object.keys(stored).length!==4 || !['schema','warning','input','preview'].every(k=>Object.hasOwn(stored,k)) || stored.schema!=='tekstilcad.experimental.bodice-request.v1' || stored.warning!=='DENEYSEL ÖNİZLEME; üretim, kesim ve plotter için uygun değildir') throw new Error('Geçersiz kayıt şeması veya uyarı.');
 const recreated=createPreview(stored.input);const safe=JSON.parse(recreated);
 if(JSON.stringify(stored.preview)!==JSON.stringify(safe.preview))throw new Error('Kayıtlı geometri kaynağı ile uyuşmuyor.');return recreated;
}
