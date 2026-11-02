import {runExperimentalJob} from '../experimental-job.mjs';
export async function emit(mode){let chunks=[];for await(const c of process.stdin)chunks.push(c);const job=Buffer.concat(chunks).toString('utf8');const preview=JSON.parse(runExperimentalJob(job));const root={protocol:'tekstilcad.experimental.preview-stdio.v1',ok:true,preview};
 if(mode==='utf8'){process.stdout.write(Buffer.from([0xff]));return;}
 if(mode==='partial'){process.stdout.write('{"protocol":');return;}
 if(mode==='trailing'){process.stdout.write(JSON.stringify(root)+' {}');return;}
 if(mode==='duplicate'){process.stdout.write(JSON.stringify(root).replace('"ok":true','"ok":true,"ok":true'));return;}
 if(mode==='extra')root.extra=true;
 if(mode==='schema')root.preview.schema='unknown';
 if(mode==='transfer')root.preview.preview.transferable=true;
 if(mode==='point')root.preview.preview.parts[0].paths[0].segments[0].to.x=100000001;
 if(mode==='nested')root.preview.preview.parts[0].paths[0].unexpected=true;
 if(mode==='measure')root.preview.input.measurements.waist+=1;
 if(mode==='unknownpath')root.preview.preview.parts[0].paths[0].name='mystery';
 if(mode==='unknownsnippet')root.preview.preview.lossManifest[0].omittedSnippets.push('unrecognized');
 process.stdout.write(JSON.stringify(root));}
