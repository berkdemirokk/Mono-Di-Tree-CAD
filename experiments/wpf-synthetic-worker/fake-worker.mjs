import {readFileSync} from 'node:fs';
const mode=process.argv[2];let s='';for await(const chunk of process.stdin)s+=chunk;
if(mode==='delay')await new Promise(r=>setTimeout(r,5000));
if(mode==='invalid'){process.stdout.write('{bad');process.exit(0)}
const fixture=JSON.parse(readFileSync(new URL('../wpf-bodice-boundary/synthetic-fixture.json',import.meta.url)));const output=fixture.output;
const id=mode==='wrong'?'synthetic-scaled':JSON.parse(s).id;
const response=JSON.stringify({scope:'fake',networkIsolation:'not-isolated',dependencyClosure:'unresolved',runtime:{},modules:[],unavailable:[],result:{id,approved:output.approved,unit:mode==='bad-unit'?'cm':output.unit,coordinateSpace:output.coordinateSpace,parts:output.parts}});
process.stdout.write(mode==='duplicate'?response.replace('"id":"synthetic-base"','"id":"synthetic-base","id":"synthetic-base"'):response);
