using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectWriteLeaseCancellationTests
{
    [Fact]
    public void CancellationInterruptsContendedLeaseAndDoesNotPreventLaterSave()
    {
        var path=Path.Combine(AppContext.BaseDirectory,$"lease-cancel-{Guid.NewGuid():N}.tcad");
        using var locked=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        using var waiting=new ManualResetEventSlim();using var cancellation=new CancellationTokenSource();
        Task? holder=null;Task<bool>? waiter=null;
        try
        {
            ProjectStorage.Save(new ProjectDocument { Name="Original" },path);
            var expected=ProjectStorage.CaptureVersion(path);var bytes=File.ReadAllBytes(path);
            holder=Task.Run(()=>
            {
                using var lease=ProjectStorage.AcquireWriteLease(Path.GetFullPath(path));
                locked.Set();
                if(!release.Wait(TimeSpan.FromSeconds(20)))throw new TimeoutException();
            });
            Assert.True(locked.Wait(TimeSpan.FromSeconds(5)));
            waiter=Task.Run(()=>
            {
                try { using var lease=ProjectStorage.AcquireWriteLease(Path.GetFullPath(path),cancellation.Token,()=>waiting.Set());return false; }
                catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { return true; }
            });
            Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
            cancellation.Cancel();
            Assert.True(waiter.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(waiter.Result);
            Assert.Equal(bytes,File.ReadAllBytes(path));
            using(var saveCancellation=new CancellationTokenSource())
            using(var saveStarted=new ManualResetEventSlim())
            {
                var save=Task.Run(()=>
                {
                    saveStarted.Set();
                    try { ProjectStorage.SaveIfUnchanged(new ProjectDocument { Name="Cancelled" },path,expected,saveCancellation.Token);return false; }
                    catch(OperationCanceledException) when(saveCancellation.IsCancellationRequested) { return true; }
                });
                Assert.True(saveStarted.Wait(TimeSpan.FromSeconds(5)));
                saveCancellation.Cancel();
                Assert.True(save.Wait(TimeSpan.FromSeconds(5)) && save.Result);
                Assert.Equal(bytes,File.ReadAllBytes(path));
            }
            release.Set();Assert.True(holder.Wait(TimeSpan.FromSeconds(5)));
            ProjectStorage.SaveIfUnchanged(new ProjectDocument { Name="After" },path,expected);
            Assert.Equal("After",ProjectStorage.Load(path).Name);
        }
        finally
        {
            cancellation.Cancel();release.Set();
            if(holder is not null)holder.GetAwaiter().GetResult();
            if(waiter is not null)waiter.GetAwaiter().GetResult();
            if(File.Exists(path))File.Delete(path);
        }
    }

    [Fact]
    public void AlreadyCancelledLeaseNeverAcquires()
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>ProjectStorage.AcquireWriteLease(Path.GetFullPath("cancelled.tcad"),cancellation.Token));
    }
}
