using System.Diagnostics;

// Experiment only: NodeProbe owns its Windows job. This owner owns the launcher and streams.
internal sealed class SyntheticPreviewOwner : IAsyncDisposable
{
    private readonly object sync=new();private readonly CancellationTokenSource lifetime=new();
    private Task<byte[]>? operation;private Task? closing;private bool disposed;
    // Test hook runs after the real cleanup stage; never receives request or response data.
    internal Action<string>? AfterCleanupStageForTest { get; set; }
    internal Task<byte[]> RunAsync(string dotnet,string runner,string node,string worker,byte[] request,TimeSpan timeout)
    {
        if(!Path.IsPathFullyQualified(dotnet)||!Path.IsPathFullyQualified(runner)||!Path.IsPathFullyQualified(node)||!Path.IsPathFullyQualified(worker))throw new ArgumentException("Mutlak yollar gerekli.");
        if(request is null||request.Length is 0 or >32768)throw new ArgumentException("Geçersiz istek boyutu.");
        if(timeout<=TimeSpan.Zero||timeout>TimeSpan.FromSeconds(60))throw new ArgumentOutOfRangeException(nameof(timeout));
        lock(sync){if(disposed)throw new ObjectDisposedException(nameof(SyntheticPreviewOwner));if(operation is not null)throw new InvalidOperationException("Sahip yalnız tek iş çalıştırır.");operation=ExecuteAsync(dotnet,runner,node,worker,request.ToArray(),timeout,lifetime.Token,AfterCleanupStageForTest);return operation;}
    }
    internal void Cancel() { lock(sync) { if(disposed)throw new ObjectDisposedException(nameof(SyntheticPreviewOwner));lifetime.Cancel(); } }
    public ValueTask DisposeAsync()
    {
        lock(sync){if(closing is null){disposed=true;closing=CloseAsync(operation);}return new ValueTask(closing);}
    }
    private async Task CloseAsync(Task<byte[]>? active)
    {
        bool failed=false;
        try{lifetime.Cancel();}catch(Exception){failed=true;}
        try{if(active is not null)await active;}catch(CleanupException){failed=true;}catch(Exception){}
        try{lifetime.Dispose();}catch(Exception){failed=true;}
        if(failed)throw new CleanupException();
    }
    private static async Task<byte[]> ExecuteAsync(string dotnet,string runner,string node,string worker,byte[] request,TimeSpan timeout,CancellationToken stop,Action<string>? afterCleanupStage)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop);deadline.CancelAfter(timeout);deadline.Token.ThrowIfCancellationRequested();
        var psi=new ProcessStartInfo(dotnet){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        psi.Environment.Clear();psi.Environment["SystemRoot"]=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach(var arg in new[]{runner,node,worker,Math.Min(60000,(int)Math.Ceiling(timeout.TotalMilliseconds)).ToString(System.Globalization.CultureInfo.InvariantCulture)})psi.ArgumentList.Add(arg);
        var process=Process.Start(psi)??throw new InvalidOperationException("Deney başlatılamadı.");
        async Task<int> Exit(){await process.WaitForExitAsync();return process.ExitCode;}
        async Task Send(){try{await process.StandardInput.BaseStream.WriteAsync(request,deadline.Token);await process.StandardInput.BaseStream.FlushAsync(deadline.Token);}finally{try{process.StandardInput.Close();}catch(Exception){throw new CleanupException();}}}
        async Task Drain(){var bytes=new byte[4096];long total=0;int n;while((n=await process.StandardError.BaseStream.ReadAsync(bytes,deadline.Token))>0){total+=n;if(total>65536)throw new InvalidDataException("Deney hata akışı sınırı.");}}
        var send=Send();var drain=Drain();var result=FinalPreviewConsumer.ReadAsync(process.StandardOutput.BaseStream,Exit(),request,timeout,deadline.Token);
        var tasks=new Task[]{send,drain,result};
        try {
            var pending=tasks.ToList();while(pending.Count>0){var completed=await Task.WhenAny(pending).WaitAsync(deadline.Token);await completed;pending.Remove(completed);}
            deadline.Token.ThrowIfCancellationRequested();return await result;
        } finally {
            await RunCleanupAsync(new (string,Func<Task>)[] {
                ("cancel",()=>{deadline.Cancel();return Task.CompletedTask;}),
                ("stdin",()=>{process.StandardInput.Close();return Task.CompletedTask;}),
                ("kill",()=>{if(!process.HasExited){try{process.Kill(entireProcessTree:true);}catch(InvalidOperationException) when(process.HasExited){}}return Task.CompletedTask;}),
                ("wait",async()=>await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5))),
                ("streams",async()=>{try{await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));}catch(TimeoutException){throw;}catch(Exception){if(tasks.Any(t=>t.Exception?.Flatten().InnerExceptions.Any(e=>e is FinalPreviewConsumer.ConsumerStreamCleanupException)==true))throw new CleanupException();} }),
                ("dispose",()=>{process.Dispose();return Task.CompletedTask;})
            },afterCleanupStage);
        }
    }
    internal static async Task RunCleanupAsync(IEnumerable<(string Name,Func<Task> Action)> stages,Action<string>? afterStage=null)
    {
        bool failed=false;
        foreach(var (name,action) in stages) {
            try { await action();afterStage?.Invoke(name); } catch(Exception) { failed=true; }
        }
        if(failed)throw new CleanupException();
    }
    internal sealed class CleanupException:Exception {internal CleanupException():base("Deney süreci ve akışlarının kapanışı doğrulanamadı."){} }
}
