process.stdin.resume();let input='';process.stdin.setEncoding('utf8');process.stdin.on('data',x=>input+=x);process.stdin.on('end',()=>{process.stderr.write(input);process.exitCode=7;});
