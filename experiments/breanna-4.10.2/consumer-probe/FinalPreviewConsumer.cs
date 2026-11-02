// Isolated synthetic experiment. Returns no output until successful exit, EOF and complete validation.
internal static class FinalPreviewConsumer
{
    internal static async Task<byte[]> ReadAsync(Stream stdout,Task<int> exitCode,byte[] request,TimeSpan timeout,CancellationToken cancellation=default)
    {
        ArgumentNullException.ThrowIfNull(stdout);ArgumentNullException.ThrowIfNull(exitCode);ArgumentNullException.ThrowIfNull(request);
        if(timeout<=TimeSpan.Zero||timeout>TimeSpan.FromSeconds(60))throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);deadline.CancelAfter(timeout);
        try {
            async Task<byte[]> ReadBounded(){using var bytes=new MemoryStream();var buf=new byte[8192];while(true){int n=await stdout.ReadAsync(buf,deadline.Token);if(n==0)return bytes.ToArray();if(bytes.Length+n>1024*1024)throw new InvalidDataException("Geçersiz deneysel yanıt.");bytes.Write(buf,0,n);}}
            async Task CheckExit(){if(await exitCode.WaitAsync(deadline.Token)!=0)throw new InvalidDataException("Önizleme işlemi başarısız.");}
            var read=ReadBounded();var exit=CheckExit();
            try {
                var pending=new List<Task>{read,exit};while(pending.Count>0){var done=await Task.WhenAny(pending).WaitAsync(deadline.Token);await done;pending.Remove(done);}
                var response=await read;PreviewResponseValidator.Validate(request,response);deadline.Token.ThrowIfCancellationRequested();return response;
            } catch {deadline.Cancel();throw;}
        } finally {
            try{await stdout.DisposeAsync();}catch(Exception){throw new ConsumerStreamCleanupException();}
        }
    }
    internal sealed class ConsumerStreamCleanupException:Exception {internal ConsumerStreamCleanupException():base("Deneysel çıktı akışı kapanışı doğrulanamadı."){} }
}
