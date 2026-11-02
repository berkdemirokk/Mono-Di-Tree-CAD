process.stdin.setEncoding("utf8");let s="";process.stdin.on("data",x=>s+=x);process.stdin.on("end",()=>{process.stdout.write(s);process.stderr.write(s);process.exitCode=7;});
