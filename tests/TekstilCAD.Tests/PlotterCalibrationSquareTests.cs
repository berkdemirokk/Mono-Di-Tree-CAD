using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class PlotterCalibrationSquareTests
{
    [Theory]
    [InlineData(1, 100)] [InlineData(40, 4000)] [InlineData(101.25, 10125)]
    [InlineData(0.07, 7)] [InlineData(0.29, 29)]
    public void SquareHasExactHundredMillimeterBounds(double units, int side)
    {
        var prepared = PlotterCalibrationSquare.Prepare(units, 1);
        var geometry = prepared.Output.GetPieceGeometry(prepared.PieceId);
        Assert.True(geometry.Closed);
        Assert.Equal(4, geometry.Points.Count);
        Assert.Equal(0, geometry.Points.Min(p => p.PlotX));
        Assert.Equal(side, geometry.Points.Max(p => p.PlotX));
        Assert.Equal(0, geometry.Points.Min(p => p.PlotY));
        Assert.Equal(side, geometry.Points.Max(p => p.PlotY));
        Assert.Equal(100, geometry.Points.Max(p => p.SourceX));
        Assert.Equal(100, geometry.Points.Max(p => p.SourceY));
        Assert.StartsWith("IN;SP1;PA;", prepared.Output.Text);
        Assert.Contains("SP0;", prepared.Output.Text);
    }

    [Theory]
    [InlineData(0.1, 2, 7, 6)] [InlineData(1, 20, 70, 60)] [InlineData(40, 800, 2800, 2400)]
    public void OptionalOrientationMarkerHasDistinctAxisEdgesInsideTheSquare(double units, int origin, int endX, int endY)
    {
        var prepared = PlotterCalibrationSquare.PrepareWithOrientationMarker(units, 1);
        Assert.NotNull(prepared.MarkerPieceId);
        var square=prepared.Output.GetPieceGeometry(prepared.PieceId);
        var triangle=prepared.Output.GetPieceGeometry(prepared.MarkerPieceId.Value);
        Assert.True(square.Closed); Assert.True(triangle.Closed); Assert.Equal(3, triangle.Points.Count);
        Assert.Equal((origin,origin), (triangle.Points[0].PlotX,triangle.Points[0].PlotY));
        Assert.Equal((endX,origin), (triangle.Points[1].PlotX,triangle.Points[1].PlotY));
        Assert.Equal((origin,endY), (triangle.Points[2].PlotX,triangle.Points[2].PlotY));
        Assert.True(endX-origin > endY-origin);
        Assert.Equal(2,prepared.Output.Text.Split("PU;",StringSplitOptions.None).Length-1);
        Assert.Contains($"PU{origin},{origin};PD{endX},{origin},{origin},{endY},{origin},{origin};PU;", prepared.Output.Text);
        Assert.Null(PlotterCalibrationSquare.Prepare(units,1).MarkerPieceId);
    }

    [Theory]
    [InlineData(40,4000)]
    [InlineData(0.1,10)]
    public void ReferencePassesDeclaredBedButRejectsInsufficientBoundsAndMismatchedUnits(double units,int side)
    {
        var output=PlotterCalibrationSquare.PrepareWithOrientationMarker(units,1).Output;
        var profile=new PlotterCapabilityProfile("Test vendor","Test model","Test firmware","Test driver","Test adapter","Synthetic evidence",
            PlotterPlatform.Windows,PlotterTransport.WindowsSpooler,units,100,100,256,CapabilitySupport.Supported,CapabilitySupport.Unknown)
        { ProtocolIdentifier="synthetic",SupportedCommands=PlotterHpglCommands.CurrentExporter,LineBreaks=CapabilitySupport.Supported,
          MaximumXPlotUnits=side,MaximumYPlotUnits=side,MaximumJobBytes=100000 };
        var accepted=PlotterFileCheck.Inspect(output,profile,true);
        Assert.Equal(PlotterFileCheck.Status.Passed,accepted.State);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(output.Text))),accepted.Inspection!.Sha256);
        Assert.Equal(PlotterFileCheck.Status.Rejected,PlotterFileCheck.Inspect(output,profile with { MaximumXPlotUnits=side-1 },true).State);
        Assert.Equal(PlotterFileCheck.Status.Rejected,PlotterFileCheck.Inspect(output,profile with { MaximumYPlotUnits=side-1 },true).State);
        Assert.Equal(PlotterFileCheck.Status.UnitMismatch,PlotterFileCheck.Inspect(output,profile with { UnitsPerMillimeter=units*2 },true).State);
        Assert.Equal(PlotterFileCheck.Status.Incomplete,PlotterFileCheck.Inspect(output,profile,false).State);
    }
    [Fact]
    public void OrientationMarkerRejectsGridTooCoarseToDistinguishItsEdges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlotterCalibrationSquare.PrepareWithOrientationMarker(0.07,1));
        Assert.NotNull(PlotterCalibrationSquare.Prepare(0.07,1));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(10001)] [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)] [InlineData(0.015)] [InlineData(0.070001)]
    public void InvalidOrNonIntegralReferenceUnitsAreRejected(double units)
    { Assert.ThrowsAny<ArgumentException>(() => PlotterCalibrationSquare.Prepare(units, 1)); }

    [Theory]
    [InlineData(0)] [InlineData(0.5)] [InlineData(2)] [InlineData(double.NaN)]
    public void ReferenceCannotBeScaledAwayFromHundredMillimeters(double scale)
    { Assert.ThrowsAny<ArgumentException>(() => PlotterCalibrationSquare.Prepare(40, scale)); }

    [Theory]
    [InlineData(@"C:\WearCadfe\reference-100mm.plt")]
    [InlineData(@"C:\Hpgl\reference-100mm.plt")]
    [InlineData(@"C:\Nester\reference-100mm.plt")]
    [InlineData(@"C:\Nester1\reference-100mm.plt")]
    [InlineData(@"C:\NST\reference-100mm.plt")]
    public void ProtectedTargetsAreRejected(string path)
    { Assert.Throws<InvalidOperationException>(() => PlotterCalibrationSquare.WriteNew(PlotterCalibrationSquare.Prepare(40, 1), path)); }

    [Fact]
    public void NewOutputIsWrittenAndExistingOutputIsNeverReplaced()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "calibration-square-tests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".plt");
        var prepared = PlotterCalibrationSquare.Prepare(40, 1);
        try
        {
            PlotterCalibrationSquare.WriteNew(prepared, path);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(System.Text.Encoding.UTF8.GetBytes(prepared.Output.Text), bytes);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),prepared.ContentSha256);
            Assert.Throws<IOException>(() => PlotterCalibrationSquare.WriteNew(prepared, path));
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void PreparedReferenceIdentityChangesWithUnitsOrOrientation()
    {
        var baseline=PlotterCalibrationSquare.Prepare(40,1);
        Assert.Equal(64,baseline.ContentSha256.Length);
        Assert.Equal(baseline.ContentSha256,PlotterCalibrationSquare.Prepare(40,1).ContentSha256);
        Assert.NotEqual(baseline.ContentSha256,PlotterCalibrationSquare.Prepare(20,1).ContentSha256);
        Assert.NotEqual(baseline.ContentSha256,PlotterCalibrationSquare.PrepareWithOrientationMarker(40,1).ContentSha256);
    }
}
