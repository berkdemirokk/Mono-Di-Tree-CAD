using System.Diagnostics;
using System.IO;
using System.Text;
using BodiceWpfReader;
namespace SyntheticBridge;
internal sealed record Command(string Exe,IReadOnlyList<string> Args);
internal sealed class Worker
{
    internal static Command Real => new(@"C:\Users\pc\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe",new[]{"--permission","--allow-fs-read=*","--no-addons",@"C:\Projects\TekstilCAD\work\synthetic-worker-isolation-1105\experiments\synthetic-worker-isolation\worker.mjs"});
    internal static async Task<Preview> Run(Command command,string id,TimeSpan timeout,CancellationToken cancel)
    {
        if(!Envelope.ValidId(id))throw new InvalidDataException("unknown synthetic scenario");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel);deadline.CancelAfter(timeout);
        var bytes=Encoding.UTF8.GetBytes($"{{\"id\":\"{id}\"}}");var output=await JobRunner.Run(command,bytes,deadline.Token);return Envelope.Read(output,id);
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
