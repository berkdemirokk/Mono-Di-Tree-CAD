using System.Diagnostics;
using System.Text;
using System.Text.Json;
if(args.Length!=2)throw new ArgumentException("Node and experiment directory required.");
var node=Path.GetFullPath(args[0]);var dir=Path.GetFullPath(args[1]);var request=await Console.In.ReadToEndAsync();
async Task Expect<T>(Func<Task> action) where T:Exception { try { await action();throw new Exception("Beklenen hata oluşmadı."); } catch(T){} }
var output=await ExperimentalPreviewProcess.RunAsync(node,Path.Combine(dir,"stdio-worker.mjs"),request,TimeSpan.FromSeconds(20));
using(var json=JsonDocument.Parse(output)) if(!json.RootElement.GetProperty("ok").GetBoolean())throw new Exception("Yanıt yok.");
await Expect<InvalidDataException>(async()=>await ExperimentalPreviewProcess.RunAsync(node,Path.Combine(dir,"stdio-worker.mjs"),"{}",TimeSpan.FromSeconds(10)));
await Expect<InvalidDataException>(async()=>await ExperimentalPreviewProcess.RunAsync(node,Path.Combine(dir,"process-probe","crash.mjs"),request,TimeSpan.FromSeconds(10)));
await Expect<InvalidDataException>(async()=>await ExperimentalPreviewProcess.RunAsync(node,Path.Combine(dir,"process-probe","unsafe.mjs"),request,TimeSpan.FromSeconds(10)));
var watch=Stopwatch.StartNew();await Expect<OperationCanceledException>(async()=>await ExperimentalPreviewProcess.RunAsync(node,Path.Combine(dir,"process-probe","hang.mjs"),request,TimeSpan.FromMilliseconds(300)));if(watch.Elapsed>TimeSpan.FromSeconds(8))throw new Exception("Zaman aşımı geç uygulandı.");
using(var cancel=new CancellationTokenSource(300))await Expect<OperationCanceledException>(async()=>await ExperimentalPreviewProcess.RunAsync(node,Path.Combine(dir,"process-probe","hang.mjs"),request,TimeSpan.FromSeconds(10),cancel.Token));
await Expect<InvalidDataException>(async()=>await ExperimentalPreviewProcess.RunAsync(node,Path.Combine(dir,"process-probe","large.mjs"),request,TimeSpan.FromSeconds(10)));
var flood=Stopwatch.StartNew();await Expect<InvalidDataException>(async()=>await ExperimentalPreviewProcess.RunAsync(node,Path.Combine(dir,"process-probe","flood-before-read.mjs"),new string('x',32768),TimeSpan.FromSeconds(10)));if(flood.Elapsed>TimeSpan.FromSeconds(5))throw new Exception("Yanıt sınırı zamanında uygulanmadı.");
try { await ExperimentalPreviewProcess.RunAsync(node,Path.Combine(dir,"process-probe","orphan-pipes.mjs"),request,TimeSpan.FromMilliseconds(500));throw new Exception("Başarısız yanıt kabul edildi."); } catch(Exception ex) when(ex is OperationCanceledException or InvalidDataException) {}
await Task.Delay(3200); // Synthetic child self-terminates; this is not proof of descendant cleanup.
const string marker="SYNTHETIC_SECRET_TEST_529";try { await ExperimentalPreviewProcess.RunAsync(node,Path.Combine(dir,"process-probe","echo-crash.mjs"),marker,TimeSpan.FromSeconds(10));throw new Exception("Hata bekleniyordu."); } catch(InvalidDataException ex) { if(ex.ToString().Contains(marker,StringComparison.Ordinal))throw new Exception("Hata metnine veri sızdı."); }
await Expect<ArgumentException>(async()=>await ExperimentalPreviewProcess.RunAsync(node,Path.Combine(dir,"stdio-worker.mjs"),new string('x',32769),TimeSpan.FromSeconds(10)));
Console.Write("OK: successful preview, invalid input, crash, timeout, cancellation, output/input bounds");
