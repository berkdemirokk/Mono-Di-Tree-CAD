using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
if(args.Length!=5)throw new ArgumentException("Synthetic paths required.");
Console.InputEncoding=new UTF8Encoding(false,true);byte[] request=Encoding.UTF8.GetBytes(await Console.In.ReadToEndAsync());
async Task Expect<T>(Func<Task> action) where T:Exception {try{await action();throw new Exception("Expected failure missing");}catch(T){}}
Process Start(string script,int timeout) {
    var psi=new ProcessStartInfo(args[0]){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
    foreach(var a in new[]{args[1],args[2],script,timeout.ToString(System.Globalization.CultureInfo.InvariantCulture)})psi.ArgumentList.Add(a);
    return Process.Start(psi)??throw new InvalidOperationException();
}
async Task Cleanup(Process p){if(!p.HasExited){p.Kill(entireProcessTree:true);await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}}
async Task<int> Exit(Process p){await p.WaitForExitAsync();return p.ExitCode;}
async Task Send(Process p){await p.StandardInput.BaseStream.WriteAsync(request);p.StandardInput.Close();}
async Task Direct(string mode)
{
    var script=Path.Combine(args[4], mode is "cancel" or "timeout" or "truncated-write" ? "large-valid-worker.mjs" : mode=="partial" ? "truncated-worker.mjs" : mode=="fail" ? "echo-crash.mjs" : "../stdio-worker.mjs");
    using var p=Start(Path.GetFullPath(script),10000);try {
        var stderr=p.StandardError.ReadToEndAsync();await Send(p);
        using var cancel=new CancellationTokenSource();var stream=new GatedPipe(p.StandardOutput.BaseStream);
        var consumption=FinalPreviewConsumer.ReadAsync(stream,Exit(p),request,mode=="timeout"?TimeSpan.FromMilliseconds(1200):TimeSpan.FromSeconds(10),cancel.Token);
        if(mode is "slow" or "cancel" or "timeout" or "truncated-write") {
            await stream.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(8));await Task.Delay(100);
            if(consumption.IsCompleted)throw new Exception("Partial response accepted");
            if(mode is "cancel" or "timeout") {if(p.HasExited)throw new Exception("Write-in-progress not observed");if(mode=="cancel")cancel.Cancel();await Expect<OperationCanceledException>(async()=>await consumption);}
            else if(mode=="truncated-write") {if(p.HasExited)throw new Exception("Write-in-progress not observed");p.Kill(entireProcessTree:true);stream.Continue.TrySetResult();await Expect<InvalidDataException>(async()=>await consumption);}
            else {stream.Continue.TrySetResult();if((await consumption).Length==0)throw new Exception("Empty result");}
        } else {stream.Continue.TrySetResult();await Expect<InvalidDataException>(async()=>await consumption);}
        await Cleanup(p);if(!p.HasExited)throw new Exception("Probe remains alive");_ = await stderr;
    } finally {await Cleanup(p);}
}
async Task ChildTree()
{
    var id=Convert.ToHexString(SHA256.HashData(request)).ToLowerInvariant()[..32];var baseDir="work/breanna-probe/";var childPath=baseDir+"atomic-child-"+id+".pid";var rootPath=baseDir+"atomic-root-"+id+".pid";
    if(File.Exists(childPath)||File.Exists(rootPath))throw new IOException("Sentinel collision");
    using var p=Start(Path.Combine(args[4],"orphan.mjs"),10000);Process? child=null;
    try {
        var stderr=p.StandardError.ReadToEndAsync();await Send(p);using var cancel=new CancellationTokenSource();var read=FinalPreviewConsumer.ReadAsync(p.StandardOutput.BaseStream,Exit(p),request,TimeSpan.FromSeconds(10),cancel.Token);
        var limit=Stopwatch.StartNew();while((!File.Exists(childPath)||!File.Exists(rootPath))&&limit.Elapsed<TimeSpan.FromSeconds(6))await Task.Delay(20);
        if(!File.Exists(childPath)||!File.Exists(rootPath))throw new Exception("Child sentinel missing");
        child=Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(childPath)));if(child.HasExited)throw new Exception("Child not alive before cancel");
        cancel.Cancel();await Expect<OperationCanceledException>(async()=>await read);await Cleanup(p);await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));if(!p.HasExited)throw new Exception("Launcher survived");_ = await stderr;
    } finally {await Cleanup(p);child?.Dispose();if(File.Exists(childPath))File.Delete(childPath);if(File.Exists(rootPath))File.Delete(rootPath);}
}
foreach(var mode in new[]{"slow","partial","fail","cancel","timeout","truncated-write"})await Direct(mode);await ChildTree();Console.Write("PASS: direct NodeProbe pipe, partial write, timeout, cancellation, errors and job cleanup");
sealed class GatedPipe(Stream source):Stream
{
 bool first=true;public TaskCompletionSource FirstRead{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Continue{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){if(!first)await Continue.Task.WaitAsync(token);var n=await source.ReadAsync(buffer,token);if(n>0&&first){first=false;FirstRead.TrySetResult();}return n;}
 public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}protected override void Dispose(bool d){if(d)source.Dispose();base.Dispose(d);}public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long a,SeekOrigin b)=>throw new NotSupportedException();public override void SetLength(long a)=>throw new NotSupportedException();public override void Write(byte[] a,int b,int c)=>throw new NotSupportedException();
}
