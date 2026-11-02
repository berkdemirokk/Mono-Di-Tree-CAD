using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal static class ExperimentalPreviewProcess
{
    private static readonly UTF8Encoding StrictUtf8 = new(false,true);
    public static async Task<string> RunAsync(string nodeExe,string script,string request,TimeSpan timeout,CancellationToken cancellation=default)
    {
        if(!Path.IsPathFullyQualified(nodeExe)||!Path.IsPathFullyQualified(script)) throw new ArgumentException("Mutlak program ve betik yolu gerekli.");
        if(timeout<=TimeSpan.Zero||timeout>TimeSpan.FromSeconds(60))throw new ArgumentOutOfRangeException(nameof(timeout));
        var bytes=StrictUtf8.GetBytes(request);
        if(bytes.Length==0||bytes.Length>32768)throw new ArgumentException("İstek boyutu geçersiz.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(timeout);deadline.Token.ThrowIfCancellationRequested();
        var psi=new ProcessStartInfo(nodeExe) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
        psi.ArgumentList.Add(script);
        using var process=new Process { StartInfo=psi };
        if(!process.Start())throw new InvalidOperationException("Deneysel önizleme işlemi başlatılamadı.");
        try {
            var output=ReadBoundedAsync(process.StandardOutput.BaseStream,1024*1024,deadline.Token);
            var errors=DrainAsync(process.StandardError.BaseStream,deadline.Token);
            await process.StandardInput.BaseStream.WriteAsync(bytes,deadline.Token);process.StandardInput.Close();
            var exited=process.WaitForExitAsync(deadline.Token);
            await await Task.WhenAny(output,errors,exited);
            await Task.WhenAll(output,errors,exited);
            if(process.ExitCode!=0)throw new InvalidDataException("Önizleme işlemi başarısız.");
            string result;try{result=StrictUtf8.GetString(await output);}catch(DecoderFallbackException){throw new InvalidDataException("Geçersiz UTF-8 yanıtı.");}
            using var json=JsonDocument.Parse(result,new JsonDocumentOptions{MaxDepth=64});var root=json.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Count()!=3||!root.TryGetProperty("protocol",out var version)||version.GetString()!="tekstilcad.experimental.preview-stdio.v1"||!root.TryGetProperty("ok",out var ok)||ok.ValueKind!=JsonValueKind.True||!root.TryGetProperty("preview",out var preview)||!preview.TryGetProperty("schema",out var schema)||schema.GetString()!="tekstilcad.experimental.bodice-request.v1"||!preview.TryGetProperty("warning",out var warning)||warning.GetString()!="DENEYSEL ÖNİZLEME; üretim, kesim ve plotter için uygun değildir"||!preview.TryGetProperty("preview",out var model)||!model.TryGetProperty("transferable",out var transfer)||transfer.ValueKind!=JsonValueKind.False||!model.TryGetProperty("cutBoundaryAvailable",out var cut)||cut.ValueKind!=JsonValueKind.False)throw new InvalidDataException("Güvenli önizleme yanıtı değil.");
            return result;
        } finally { if(!process.HasExited){try{process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}} }
    }
    private static async Task<byte[]> ReadBoundedAsync(Stream stream,int limit,CancellationToken ct) { using var buffer=new MemoryStream();var chunk=new byte[8192];int n;while((n=await stream.ReadAsync(chunk,ct))>0){if(buffer.Length+n>limit)throw new InvalidDataException("Yanıt boyutu sınırı aşıldı.");buffer.Write(chunk,0,n);}return buffer.ToArray(); }
    private static async Task DrainAsync(Stream stream,CancellationToken ct) { var chunk=new byte[4096];while(await stream.ReadAsync(chunk,ct)>0){} }
}
