using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
if(args.Length!=4)throw new ArgumentException("Synthetic absolute paths required.");
Console.InputEncoding=new UTF8Encoding(false,true);var request=Encoding.UTF8.GetBytes(await Console.In.ReadToEndAsync());
var dotnet=Path.GetFullPath(args[0]);var runner=Path.GetFullPath(args[1]);var node=Path.GetFullPath(args[2]);var experiment=Path.GetFullPath(args[3]);
string Script(string path)=>Path.GetFullPath(Path.Combine(experiment,path));
async Task Expect<T>(Func<Task> a) where T:Exception {try{await a();throw new Exception("Beklenen hata oluşmadı.");}catch(T){}}
await using(var owner=new SyntheticPreviewOwner()) {var bytes=await owner.RunAsync(dotnet,runner,node,Script("stdio-worker.mjs"),request,TimeSpan.FromSeconds(8));if(bytes.Length==0)throw new Exception("Önizleme boş.");}
await using(var owner=new SyntheticPreviewOwner()) {await Expect<InvalidDataException>(async()=>await owner.RunAsync(dotnet,runner,node,Script("stdio-worker.mjs"),Encoding.UTF8.GetBytes("{}"),TimeSpan.FromSeconds(8)));}
await using(var owner=new SyntheticPreviewOwner()) {await Expect<OperationCanceledException>(async()=>await owner.RunAsync(dotnet,runner,node,Script("process-test-fixtures/hang.mjs"),request,TimeSpan.FromMilliseconds(350)));}
await using(var owner=new SyntheticPreviewOwner()) {var run=owner.RunAsync(dotnet,runner,node,Script("process-test-fixtures/hang.mjs"),request,TimeSpan.FromSeconds(8));await Task.Delay(250);owner.Cancel();await Expect<OperationCanceledException>(async()=>await run);}
await using(var owner=new SyntheticPreviewOwner()) {owner.Cancel();await Expect<OperationCanceledException>(async()=>await owner.RunAsync(dotnet,runner,node,Script("stdio-worker.mjs"),request,TimeSpan.FromSeconds(8)));}
var raceOwner=new SyntheticPreviewOwner();var raceRun=raceOwner.RunAsync(dotnet,runner,node,Script("process-test-fixtures/hang.mjs"),request,TimeSpan.FromSeconds(8));await Task.Delay(200);
var race=Enumerable.Range(0,16).Select(i=>Task.Run(async()=>{if(i%2==0){try{raceOwner.Cancel();}catch(ObjectDisposedException){}}else await raceOwner.DisposeAsync();})).ToArray();await Task.WhenAll(race);await Expect<OperationCanceledException>(async()=>await raceRun);await raceOwner.DisposeAsync();
var id=Convert.ToHexString(SHA256.HashData(request)).ToLowerInvariant()[..32];var dir="work/breanna-probe/atomic-case-"+id;var cp=Path.Combine(dir,"child.pid");var rp=Path.Combine(dir,"root.pid");var release=Path.Combine(dir,"release");
if(Directory.Exists(dir))throw new IOException("Sentinel çakışması");Directory.CreateDirectory(dir);
var treeOwner=new SyntheticPreviewOwner();Process? child=null;try {
    var running=treeOwner.RunAsync(dotnet,runner,node,Script("process-test-fixtures/orphan-synced.mjs"),request,TimeSpan.FromSeconds(12));
    var watch=Stopwatch.StartNew();while((!File.Exists(cp)||!File.Exists(rp))&&watch.Elapsed<TimeSpan.FromSeconds(6))await Task.Delay(20);
    if(!File.Exists(cp)||!File.Exists(rp))throw new Exception("Sentinel eksik.");
    child=Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(cp)));using var root=Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(rp)));if(child.HasExited||root.HasExited)throw new Exception("Başlangıç süreci erken çıktı.");
    await File.WriteAllTextAsync(release,"release");await root.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));if(child.HasExited)throw new Exception("Çocuk erken çıktı.");
    var closes=Enumerable.Range(0,12).Select(_=>Task.Run(async()=>await treeOwner.DisposeAsync().AsTask())).ToArray();await Task.WhenAll(closes);await Expect<OperationCanceledException>(async()=>await running);await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await treeOwner.DisposeAsync();try{_ = treeOwner.RunAsync(dotnet,runner,node,Script("stdio-worker.mjs"),request,TimeSpan.FromSeconds(1));throw new Exception("Kapalı sahip işi kabul etti.");}catch(ObjectDisposedException){}
} finally {await treeOwner.DisposeAsync();child?.Dispose();foreach(var p in new[]{cp,rp,release})if(File.Exists(p))File.Delete(p);Directory.Delete(dir);}
Console.Write("PASS: owned lifecycle success, failure, timeout, cancel, concurrent dispose and root-exited child cleanup");
