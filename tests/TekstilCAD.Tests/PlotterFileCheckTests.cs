using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class PlotterFileCheckTests
{
    private static PlotterCapabilityProfile Profile()=>new("Vendor","Model","Firmware","Driver","Adapter","Reference",PlotterPlatform.Windows,PlotterTransport.WindowsSpooler,40,100,100,256,CapabilitySupport.Supported,CapabilitySupport.Unknown)
    { ProtocolIdentifier="declared",SupportedCommands=PlotterHpglCommands.CurrentExporter,LineBreaks=CapabilitySupport.Supported,MaximumXPlotUnits=4000,MaximumYPlotUnits=4000,MaximumJobBytes=100000 };
    private static HpglPreparedOutput Output(double x=0,double width=100)=>HpglFormat.Prepare(new ProjectDocument { Pieces=[new PatternPiece { Closed=false,Points=[new(x,0),new(x+width,0)] }] },40);
    [Fact] public void MissingDeclarationAndUnitsAreNotReportedAsPassing()
    {
        Assert.Equal(PlotterFileCheck.Status.Incomplete,PlotterFileCheck.Inspect(Output(),null,true).State);
        Assert.Equal(PlotterFileCheck.Status.Incomplete,PlotterFileCheck.Inspect(Output(),Profile(),false).State);
        Assert.Equal(PlotterFileCheck.Status.Incomplete,PlotterFileCheck.Inspect(Output(),Profile() with { Manufacturer="" },true).State);
        Assert.Equal(PlotterFileCheck.Status.UnitMismatch,PlotterFileCheck.Inspect(Output(),Profile() with { UnitsPerMillimeter=20 },true).State);
    }
    [Fact] public void BoundsAndCommandLimitsAreAppliedToPreparedBytes()
    {
        Assert.Equal(PlotterFileCheck.Status.Passed,PlotterFileCheck.Inspect(Output(),Profile(),true).State);
        Assert.Equal(PlotterFileCheck.Status.Rejected,PlotterFileCheck.Inspect(Output(width:100.1),Profile(),true).State);
        Assert.Equal(PlotterFileCheck.Status.Rejected,PlotterFileCheck.Inspect(Output(x:-1,width:2),Profile(),true).State);
        Assert.Equal(PlotterFileCheck.Status.Rejected,PlotterFileCheck.Inspect(Output(),Profile() with { MaximumCommandBytes=3 },true).State);
        Assert.Equal(PlotterFileCheck.Status.Rejected,PlotterFileCheck.Inspect(Output(),Profile() with { MaximumJobBytes=5 },true).State);
    }
    [Fact] public void CancellationIsNotConvertedToACompatibilityResult()
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>PlotterFileCheck.Inspect(Output(),Profile(),true,cancellation.Token));
    }
}
