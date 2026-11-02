process.stdin.resume();process.stdin.on('end',()=>{process.stdout.write(Object.hasOwn(process.env,'SYNTHETIC_ENV_SECRET_TEST')?'LEAK':'SAFE');});
