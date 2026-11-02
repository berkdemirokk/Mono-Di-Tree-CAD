import {draftExperimental} from './draft.mjs';
import {open,realpath,lstat} from 'node:fs/promises';
import {dirname,join,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const sample=Object.freeze({biceps:300,bustFront:500,bustSpan:190,highBust:900,highBustFront:450,hpsToBust:270,hpsToWaistBack:420,hpsToWaistFront:450,waist:740,waistToHips:200,neck:370,shoulderToShoulder:390,shoulderSlope:15,shoulderToWrist:580,wrist:160});
export function buildSyntheticPreview(measurements,options,{unit,toleranceMm,layout}={}) {
 if(unit!=='mm'||!Number.isFinite(toleranceMm)||toleranceMm<=0||toleranceMm>1)throw new Error('Açık mm birimi ve (0,1] mm tolerans gerekli.');
 if(layout!=='local-unplaced')throw new Error('Bilinmeyen veya uygulanmamış yerleşim dönüşümü.');
 if(!measurements||Object.keys(measurements).length!==Object.keys(sample).length||Object.entries(sample).some(([k,v])=>measurements[k]!==v))throw new Error('Yalnız sabit sentetik ölçü örneği desteklenir.');
 const draft=draftExperimental(measurements,options),model=draft.model;
 if(model.parts.length!==3||model.lossManifest.length!==3)throw new Error('Parça veya kayıp listesi geçersiz.');
 let count=0;for(const part of model.parts){if(part.paths.length>64)throw new Error('Yol sınırı aşıldı.');for(const path of part.paths){count+=path.segments.length;if(count>4096)throw new Error('Segment sınırı aşıldı.');}}
 const preview={schema:'tekstilcad.experimental.breanna-preview.v1',classification:'SENTETİK ÖNİZLEME — üretime, kesime ve plotter çıktısına uygun değildir',source:{name:'FreeSewing Breanna',version:'4.10.2',data:'synthetic-only'},unit,toleranceMm,toleranceMeaning:'yalnız kullanıcı bildirimi; topoloji veya çıktı doğruluğu kanıtı değildir',layout,productionApproved:false,transferable:false,cutBoundaryAvailable:false,topologyVerified:false,model,lossManifest:model.lossManifest};
 const json=JSON.stringify(preview,null,2)+'\n';if(Buffer.byteLength(json,'utf8')>1024*1024)throw new Error('Dosya boyutu sınırı aşıldı.');return json;
}
// Explicit opt-in and exclusive creation under this isolated worktree only; never overwrite.
export async function writeNewSyntheticPreview(filename,measurements,options,parameters,{approved}={}) {
 if(approved!==true)throw new Error('Dosya oluşturma onayı gerekli.');
 if(typeof filename!=='string'||!/^sample_[A-Za-z0-9_-]{1,56}\.preview\.json$/.test(filename))throw new Error('Geçersiz yeni dosya adı.');
 const json=buildSyntheticPreview(measurements,options,parameters);
 const base=await realpath(root),work=join(base,'work'),probe=join(work,'breanna-probe'),dir=join(probe,'preview-output');
 for(const folder of [work,probe,dir]){const info=await lstat(folder);if(!info.isDirectory()||info.isSymbolicLink()||(await realpath(folder)).toLowerCase()!==folder.toLowerCase())throw new Error('Güvensiz çıktı klasörü.');}
 const target=join(dir,filename);const handle=await open(target,'wx',0o600);try{await handle.writeFile(json,{encoding:'utf8'});}finally{await handle.close();}return target;
}
