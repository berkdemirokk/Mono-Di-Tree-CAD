using System.Text;
using System.Text.Json;
Console.InputEncoding=new UTF8Encoding(false,true);
using var data=JsonDocument.Parse(await Console.In.ReadToEndAsync());
var req=Encoding.UTF8.GetBytes(data.RootElement.GetProperty("request").GetString()!);var good=Encoding.UTF8.GetBytes(data.RootElement.GetProperty("response").GetString()!);
async Task Expect<T>(Func<Task> a) where T:Exception {try{await a();throw new Exception("Expected error missing");}catch(T){}}
var same=await FinalPreviewConsumer.ReadAsync(new MemoryStream(good),Task.FromResult(0),req,TimeSpan.FromSeconds(5));if(!same.AsSpan().SequenceEqual(good))throw new Exception("Mismatch");
await Expect<InvalidDataException>(async()=>await FinalPreviewConsumer.ReadAsync(new MemoryStream(good),Task.FromResult(3),req,TimeSpan.FromSeconds(5)));
await Expect<InvalidDataException>(async()=>await FinalPreviewConsumer.ReadAsync(new MemoryStream(good[..^1]),Task.FromResult(0),req,TimeSpan.FromSeconds(5)));
await Expect<InvalidDataException>(async()=>await FinalPreviewConsumer.ReadAsync(new MemoryStream(new byte[1024*1024+1]),Task.FromResult(0),req,TimeSpan.FromSeconds(5)));
using(var canceled=new CancellationTokenSource()){canceled.Cancel();await Expect<OperationCanceledException>(async()=>await FinalPreviewConsumer.ReadAsync(new MemoryStream(good),Task.FromResult(0),req,TimeSpan.FromSeconds(5),canceled.Token));}
var gate=new GateStream(good);var pending=FinalPreviewConsumer.ReadAsync(gate,Task.FromResult(0),req,TimeSpan.FromSeconds(5));await gate.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(2));if(pending.IsCompleted)throw new Exception("Partial bytes released before EOF");gate.Release.TrySetResult();await pending;
var badGate=new GateStream(good);var failure=FinalPreviewConsumer.ReadAsync(badGate,Task.FromResult(4),req,TimeSpan.FromSeconds(5));await Expect<InvalidDataException>(async()=>await failure);if(!badGate.Disposed)throw new Exception("Failure did not dispose stream");
var slow=new GateStream(good);await Expect<OperationCanceledException>(async()=>await FinalPreviewConsumer.ReadAsync(slow,Task.FromResult(0),req,TimeSpan.FromMilliseconds(100)));if(!slow.Disposed)throw new Exception("Timeout did not dispose stream");
try{await FinalPreviewConsumer.ReadAsync(new FaultDisposeStream(good),Task.FromResult(0),req,TimeSpan.FromSeconds(5));throw new Exception("Akış temizlik hatası kayboldu.");}catch(FinalPreviewConsumer.ConsumerStreamCleanupException ex){if(ex.ToString().Contains("SYNTHETIC_STREAM_SECRET",StringComparison.Ordinal))throw new Exception("Akış hatasına veri sızdı.");}
Console.Write("PASS: complete response and exit, rejection, EOF delay, cancellation, timeout and bounds");
sealed class GateStream(byte[] bytes):Stream
{
 int position;public bool Disposed {get;private set;} public TaskCompletionSource Delivered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>bytes.Length;public override long Position {get=>position;set=>throw new NotSupportedException();}
 public override async ValueTask<int> ReadAsync(Memory<byte> destination,CancellationToken token=default){if(position<bytes.Length){var count=Math.Min(destination.Length,bytes.Length-position);bytes.AsMemory(position,count).CopyTo(destination);position+=count;if(position==bytes.Length)Delivered.TrySetResult();return count;}await Release.Task.WaitAsync(token);return 0;}
 protected override void Dispose(bool disposing){Disposed=true;base.Dispose(disposing);}public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long x)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
}

sealed class FaultDisposeStream(byte[] bytes):MemoryStream(bytes)
{
 public override ValueTask DisposeAsync()=>ValueTask.FromException(new IOException("SYNTHETIC_STREAM_SECRET"));
}