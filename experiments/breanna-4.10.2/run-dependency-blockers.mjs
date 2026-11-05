import {readFile} from 'node:fs/promises';
import {report} from './dependency-blockers.mjs';
// Paths are explicit; no invocation, download or write occurs.
const [packageRoot,runtimePath,runtimeNoticePath]=process.argv.slice(2);
if(!packageRoot)throw Error('Provide installed package root; runtime and notice paths are optional');
const read=(name)=>readFile(new URL(name,import.meta.url),'utf8');
const [lockText,provenance,tarballs]=await Promise.all([read('pnpm-lock.yaml'),read('npm-provenance-4102.json').then(JSON.parse),read('npm-tarball-verification.json').then(JSON.parse)]);
console.log(JSON.stringify(await report({lockText,packageRoot,provenance,tarballs,runtimePath,runtimeNoticePath}),null,2));
