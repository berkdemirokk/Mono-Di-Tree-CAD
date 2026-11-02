import {measurementNames} from './draft.mjs';
import {createPreview} from './preview-adapter.mjs';
const number='-?(?:0|[1-9][0-9]*)(?:\\.[0-9]{1,3})?';
// Canonical writer order prevents duplicate/unknown fields before JSON.parse.
const entries=measurementNames.map(k=>`"${k}":${number}`).join(',');
const shape=new RegExp(`^\\{"schema":"tekstilcad\\.experimental\\.breanna-measurements\\.v1","unit":"mm","angleUnit":"degree","measurements":\\{${entries}\\}\\}$`);
export function readMeasurementEnvelope(text) {
 if(typeof text!=='string'||Buffer.byteLength(text,'utf8')>16384||!shape.test(text))throw new Error('Ölçü aktarım şeması, birimi veya boyutu geçersiz.');
 const envelope=JSON.parse(text), m=envelope.measurements;
 for(const key of measurementNames) {
  const v=m[key];
  if(typeof v!=='number'||!Number.isFinite(v)||(key==='shoulderSlope' ? v<=-90||v>=90 : v<=0||v>100000)) throw new RangeError(`Ölçü teknik sınır dışında: ${key}`);
 }
 if(m.highBustFront>=m.highBust)throw new RangeError('Ön üst göğüs toplam üst göğüsten küçük olmalı.');
 if(m.hpsToBust>=m.hpsToWaistFront)throw new RangeError('Göğüs yüksekliği ön bel uzunluğundan küçük olmalı.');
 return envelope;
}
export function previewFromCSharpEnvelope(text, options, {toleranceMm,layout}={}) {
 const envelope=readMeasurementEnvelope(text);return createPreview({unit:envelope.unit,angleUnit:envelope.angleUnit,layout,toleranceMm,measurements:envelope.measurements,options});
}
