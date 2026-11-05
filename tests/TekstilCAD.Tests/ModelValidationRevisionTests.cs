using TekstilCAD.Desktop;

namespace TekstilCAD.Tests;

public class ModelValidationRevisionTests
{
    [Fact]
    public void OnlyAnOpenUnchangedUncancelledContextCanApply()
    {
        var initial=new ModelValidationRevision(1,2,3,4);
        Assert.True(ModelValidationRevision.IsCurrent(initial,initial,false,false,false));
        Assert.False(ModelValidationRevision.IsCurrent(initial,initial with { Question=2 },false,false,false));
        Assert.False(ModelValidationRevision.IsCurrent(initial,initial with { Reference=3 },false,false,false));
        Assert.False(ModelValidationRevision.IsCurrent(initial,initial with { Chat=4 },false,false,false));
        Assert.False(ModelValidationRevision.IsCurrent(initial,initial with { Model=5 },false,false,false));
        Assert.False(ModelValidationRevision.IsCurrent(initial,initial,true,false,false));
        Assert.False(ModelValidationRevision.IsCurrent(initial,initial,false,true,false));
        Assert.False(ModelValidationRevision.IsCurrent(initial,initial,false,false,true));
    }

    [Fact]
    public async Task CancellationStopsWaitingEvenWhileSynchronousValidationIsRunning()
    {
        using var entered=new ManualResetEventSlim();
        using var release=new ManualResetEventSlim();
        using var cancellation=new CancellationTokenSource();
        var validation=ModelValidationRevision.ValidateAsync(()=> { entered.Set();release.Wait(); },cancellation.Token);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>validation);
        }
        finally { release.Set(); }
    }
}
