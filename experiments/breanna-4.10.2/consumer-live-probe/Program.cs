using System.Diagnostics;
using System.Text;
if(args.Length!=5)throw new ArgumentException("Synthetic executable paths required.");
Console.InputEncoding=new UTF8Encoding(false,true);var request=Encoding.UTF8.GetBytes(await Console.In.ReadToEndAsync());
async Task Expect<T>(Func<Task> action) where T:Exception {try{await action();throw new Exception("Expected error missing");}catch(T){}}
async Task Scenario(string mode)
{
    var psi=new ProcessStartInfo(args[0]){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
    foreach(var a in new[]{args[1],mode,args[2],args[3],args[0],args[4]})psi.ArgumentList.Add(a);
    using var p=Process.Start(psi)??throw new InvalidOperationException();
    try {
        var stderr=p.StandardError.ReadToEndAsync();await p.StandardInput.BaseStream.WriteAsync(request);p.StandardInput.Close();
        async Task<int> Exit(){await p.WaitForExitAsync();return p.ExitCode;}
        using var cancel=new CancellationTokenSource();var stream=new SignaledStream(p.StandardOutput.BaseStream);
        var task=FinalPreviewConsumer.ReadAsync(stream,Exit(),request,TimeSpan.FromSeconds(10),cancel.Token);
        if(mode is "slow" or "cancel") {await stream.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(8));if(task.IsCompleted)throw new Exception("Partial response accepted");if(mode=="cancel")cancel.Cancel();}
        if(mode=="slow") {var result=await task;if(result.Length==0)throw new Exception("Empty result");}
        else if(mode=="cancel") await Expect<OperationCanceledException>(async()=>await task);
        else await Expect<InvalidDataException>(async()=>await task);
        if(!p.HasExited){p.Kill(entireProcessTree:true);await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}
        if((await stderr).Length!=0)throw new Exception("Unexpected child stderr");
    } finally {if(!p.HasExited){p.Kill(entireProcessTree:true);await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}}
}
foreach(var mode in new[]{"slow","latefail","partial","partialfail","cancel"})await Scenario(mode);
Console.Write("PASS: real process stream slow, failed exit, partial response and cancellation");
sealed class SignaledStream(Stream source):Stream
{
 public TaskCompletionSource FirstRead {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
 public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){int n=await source.ReadAsync(buffer,token);if(n>0)FirstRead.TrySetResult();return n;}
 protected override void Dispose(bool disposing){if(disposing)source.Dispose();base.Dispose(disposing);}public override int Read(byte[] a,int b,int c)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long a,SeekOrigin b)=>throw new NotSupportedException();public override void SetLength(long a)=>throw new NotSupportedException();public override void Write(byte[] a,int b,int c)=>throw new NotSupportedException();
}
