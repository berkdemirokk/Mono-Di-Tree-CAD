import {about} from '@freesewing/breanna';
import {previewFromCSharpEnvelope,readMeasurementEnvelope} from './csharp-envelope.mjs';
const schema='tekstilcad.experimental.breanna-job.v1';
const fixedKeys=['schema','engine','measurementEnvelope','options','unit','angleUnit','toleranceMm','layout'];
function exactKeys(value,keys) {return value!==null&&typeof value==='object'&&!Array.isArray(value)&&Object.keys(value).length===keys.length&&keys.every(k=>Object.hasOwn(value,k));}
export function createExperimentalJob(measurementEnvelope,options,{unit,angleUnit,toleranceMm,layout}={}) {
 readMeasurementEnvelope(measurementEnvelope);
 if(about.version!=='4.10.2')throw new Error('Yüklü model sürümü eşleşmiyor.');
 const job={schema,engine:{model:'breanna',version:'4.10.2'},measurementEnvelope,options,unit,angleUnit,toleranceMm,layout};
 const text=JSON.stringify(job);if(Buffer.byteLength(text,'utf8')>32768)throw new Error('Deney belgesi boyut sınırı.');
 // Exercise full validation before returning a document; not a production approval.
 runExperimentalJob(text);return text;
}
export function runExperimentalJob(text) {
 if(typeof text!=='string'||Buffer.byteLength(text,'utf8')>32768)throw new Error('Geçersiz belge boyutu.');
 let job;try{job=JSON.parse(text);}catch{throw new Error('Geçersiz belge JSON.');}
 if(JSON.stringify(job)!==text||!exactKeys(job,fixedKeys)||job.schema!==schema||!exactKeys(job.engine,['model','version'])||job.engine.model!=='breanna'||job.engine.version!=='4.10.2'||about.version!=='4.10.2'||job.unit!=='mm'||job.angleUnit!=='degree'||job.layout!=='local-unplaced'||!Number.isFinite(job.toleranceMm)||job.toleranceMm<=0||job.toleranceMm>1) throw new Error('Bilinmeyen, eksik veya geçersiz deney belgesi alanı.');
 return previewFromCSharpEnvelope(job.measurementEnvelope,job.options,{toleranceMm:job.toleranceMm,layout:job.layout});
}
