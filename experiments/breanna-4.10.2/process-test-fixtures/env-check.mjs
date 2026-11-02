if(Object.hasOwn(process.env,'SYNTHETIC_ENV_SECRET_TEST')) {process.exitCode=7;} else {await import('../stdio-worker.mjs');}
