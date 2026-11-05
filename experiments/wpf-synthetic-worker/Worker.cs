using System.Diagnostics;
using System.IO;
using System.Text;
using BodiceWpfReader;
namespace SyntheticBridge;
internal sealed record Command(string Exe,IReadOnlyList<string> Args);
internal sealed class Worker
{
    internal static Command Real => new(@"C:\Users\pc\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe",new[]{"--permission","--allow-fs-read=*","--no-addons",@"C:\Projects\TekstilCAD\work\synthetic-worker-isolation-1105\experiments\synthetic-worker-isolation\worker.mjs"});
    static async Task<byte[]> ReadBounded(Stream stream,int max,CancellationToken token){using var b=new MemoryStream();var buf=new byte[8192];while(true){int n=await stream.ReadAsync(buf,token);if(n==0)return b.ToArray();if(b.Length+n>max)throw new InvalidDataException("output limit");b.Write(buf,0,n);}}
    internal static async Task<Preview> Run(Command command,string id,TimeSpan timeout,CancellationToken cancel)
    {
        if(!Envelope.ValidId(id))throw new InvalidDataException("unknown synthetic scenario");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel);deadline.CancelAfter(timeout);
        var start=new ProcessStartInfo(command.Exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in command.Args)start.ArgumentList.Add(arg);
        using var p=new Process{StartInfo=start};if(!p.Start())throw new InvalidOperationException("worker unavailable");
        try {
            await p.StandardInput.WriteAsync($"{{\"id\":\"{id}\"}}".AsMemory(),deadline.Token);p.StandardInput.Close();
            var stdout=ReadBounded(p.StandardOutput.BaseStream,2_000_000,deadline.Token);var stderr=ReadBounded(p.StandardError.BaseStream,16_384,deadline.Token);
            await Task.WhenAll(stdout,stderr,p.WaitForExitAsync(deadline.Token));if(p.ExitCode!=0 || stderr.Result.Length!=0)throw new InvalidDataException("worker failed");return Envelope.Read(stdout.Result,id);
        }finally {if(!p.HasExited){try{p.Kill(true);}catch(InvalidOperationException){}try{await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}catch(TimeoutException){}}}
    }
}
// Requests are invalidated on selection change, cancellation, or a newer generation.
internal sealed class Coordinator
{
    readonly object gate=new();long epoch;CancellationTokenSource? active;
    internal void Cancel(){lock(gate){epoch++;active?.Cancel();active=null;}}
    internal async Task<bool> Start(string id,Func<CancellationToken,Task<Preview>> run,Action<Preview> publish)
    {
        var token=new CancellationTokenSource();long mine;lock(gate){epoch++;active?.Cancel();active=token;mine=epoch;}
        try{var result=await run(token.Token);lock(gate){if(token.IsCancellationRequested||mine!=epoch)return false;publish(result);return true;}}
        catch(OperationCanceledException){return false;}finally{lock(gate){if(ReferenceEquals(active,token))active=null;token.Dispose();}}
    }
}
