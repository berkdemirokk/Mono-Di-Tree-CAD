import {measurementNames} from './draft.mjs';
import {createPreview} from './preview-adapter.mjs';
const number='-?(?:0|[1-9][0-9]*)(?:\\.[0-9]{1,3})?';
// Canonical writer order prevents duplicate/unknown fields before JSON.parse.
const entries=measurementNames.map(k=>`"${k}":${number}`).join(',');
const shape=new RegExp(`^\\{"schema":"tekstilcad\\.experimental\\.breanna-measurements\\.v1","unit":"mm","angleUnit":"degree","measurements":\\{${entries}\\}\\}$`);
export function previewFromCSharpEnvelope(text, options, {toleranceMm,layout}={}) {
 if(typeof text!=='string'||Buffer.byteLength(text,'utf8')>16384||!shape.test(text))throw new Error('Ölçü aktarım şeması, birimi veya boyutu geçersiz.');
 const envelope=JSON.parse(text);return createPreview({unit:envelope.unit,angleUnit:envelope.angleUnit,layout,toleranceMm,measurements:envelope.measurements,options});
}
