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
            // Dedicated threads keep this synchronization test independent of
            // thread-pool starvation from parallel test collections.
            holder=Task.Factory.StartNew(()=>
            {
                using var lease=ProjectStorage.AcquireWriteLease(Path.GetFullPath(path));
                locked.Set();
                release.Wait();
            },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
            Assert.True(locked.Wait(TimeSpan.FromSeconds(5)));
            waiter=Task.Factory.StartNew(()=>
            {
                try { using var lease=ProjectStorage.AcquireWriteLease(Path.GetFullPath(path),cancellation.Token,()=>waiting.Set());return false; }
                catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { return true; }
            },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
            Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
            cancellation.Cancel();
            Assert.True(waiter.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(waiter.Result);
            Assert.Equal(bytes,File.ReadAllBytes(path));
            using(var saveCancellation=new CancellationTokenSource())
            using(var saveStarted=new ManualResetEventSlim())
            {
                var save=Task.Factory.StartNew(()=>
                {
                    saveStarted.Set();
                    try { ProjectStorage.SaveIfUnchanged(new ProjectDocument { Name="Cancelled" },path,expected,saveCancellation.Token);return false; }
                    catch(OperationCanceledException) when(saveCancellation.IsCancellationRequested) { return true; }
                },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
                try
                {
                    Assert.True(saveStarted.Wait(TimeSpan.FromSeconds(5)));
                    saveCancellation.Cancel();
                    Assert.True(save.Wait(TimeSpan.FromSeconds(5)) && save.Result);
                    Assert.Equal(bytes,File.ReadAllBytes(path));
                }
                finally
                {
                    saveCancellation.Cancel();release.Set();
                    save.GetAwaiter().GetResult();
                }
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
    [Fact]
    public void CancellationWakesThreadObservedBlockedInLeaseWait()
    {
        var path=Path.GetFullPath($"waiting-{Guid.NewGuid():N}.tcad");
        using var held=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        using var aboutToWait=new ManualResetEventSlim();using var cancellation=new CancellationTokenSource();
        Exception? holderError=null,waiterError=null;bool cancelled=false;
        var holder=new Thread(()=>
        {
            try { using var lease=ProjectStorage.AcquireWriteLease(path);held.Set();release.Wait(); }
            catch(Exception error) { holderError=error;held.Set(); }
        });
        var waiter=new Thread(()=>
        {
            try { using var lease=ProjectStorage.AcquireWriteLease(path,cancellation.Token,()=>aboutToWait.Set()); }
            catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { cancelled=true; }
            catch(Exception error) { waiterError=error; }
        });
        try
        {
            holder.Start();Assert.True(held.Wait(TimeSpan.FromSeconds(5)));Assert.Null(holderError);
            waiter.Start();Assert.True(aboutToWait.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(()=> (waiter.ThreadState & System.Threading.ThreadState.WaitSleepJoin)!=0,TimeSpan.FromSeconds(5)));
            cancellation.Cancel();Assert.True(waiter.Join(TimeSpan.FromSeconds(5)));
            Assert.True(cancelled);Assert.Null(waiterError);
        }
        finally { cancellation.Cancel();release.Set();if(holder.IsAlive)holder.Join();if(waiter.IsAlive)waiter.Join(); }
    }

    [Fact]
    public void CancellationAfterAcquisitionReleasesMutexForAnotherWriter()
    {
        var path=Path.GetFullPath($"acquired-{Guid.NewGuid():N}.tcad");
        using var cancellation=new CancellationTokenSource();bool acquired=false;
        Assert.Throws<OperationCanceledException>(()=>ProjectStorage.AcquireWriteLease(path,cancellation.Token,
            afterAcquireForVerification:_=> { acquired=true;cancellation.Cancel(); }));
        Assert.True(acquired);
        var following=Task.Run(()=> { using var lease=ProjectStorage.AcquireWriteLease(path);return true; });
        Assert.True(following.Wait(TimeSpan.FromSeconds(5)) && following.Result);
    }

    [Fact]
    public void AbandonedLeaseIsAcquiredAndReleasedNormally()
    {
        var path=Path.GetFullPath($"abandoned-{Guid.NewGuid():N}.tcad");
        IDisposable? abandonedLease=null;Exception? error=null;
        var owner=new Thread(()=> { try { abandonedLease=ProjectStorage.AcquireWriteLease(path); } catch(Exception ex) { error=ex; } });
        owner.Start();Assert.True(owner.Join(TimeSpan.FromSeconds(5)));Assert.Null(error);Assert.NotNull(abandonedLease);
        bool wasAbandoned=false;
        using(ProjectStorage.AcquireWriteLease(path,afterAcquireForVerification:abandoned=>wasAbandoned=abandoned))
            Assert.True(wasAbandoned);
        var following=Task.Run(()=> { using var lease=ProjectStorage.AcquireWriteLease(path);return true; });
        Assert.True(following.Wait(TimeSpan.FromSeconds(5)) && following.Result);
        GC.KeepAlive(abandonedLease);
    }

}
