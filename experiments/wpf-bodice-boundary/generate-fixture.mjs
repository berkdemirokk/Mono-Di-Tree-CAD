import {calculate} from '../bodice-review/server.mjs';import {writeFileSync} from 'node:fs';
const measurements={biceps:300,bustFront:500,bustSpan:190,highBust:900,highBustFront:450,hpsToBust:270,hpsToWaistBack:420,hpsToWaistFront:450,waist:740,waistToHips:200,neck:370,shoulderToShoulder:390,shoulderSlope:15,shoulderToWrist:580,wrist:160};
writeFileSync(new URL('./synthetic-fixture.json',import.meta.url),JSON.stringify({measurements,output:calculate({unit:'mm',confirmed:true,measurements})},null,2),{flag:'wx'});
