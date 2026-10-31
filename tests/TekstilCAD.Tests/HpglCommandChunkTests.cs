using System.Globalization;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class HpglCommandChunkTests
{
    private static PlotterCapabilityProfile Profile(int maximumCommandBytes) => new("Vendor", "Model", "1", "1", "1", "evidence",
        PlotterPlatform.Windows, PlotterTransport.WindowsSpooler, 1, 1000, 1000, maximumCommandBytes,
        CapabilitySupport.Supported, CapabilitySupport.Unknown)
        { SupportedCommands = PlotterHpglCommands.CurrentExporter, LineBreaks = CapabilitySupport.Supported,
          MaximumJobBytes = 100_000, ProtocolIdentifier = "hpgl-simulation-v1", MaximumXPlotUnits = int.MaxValue, MaximumYPlotUnits = int.MaxValue };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplitCommandsPreserveEveryPointAndRespectTheBoundary(bool closed)
    {
        var points = Enumerable.Range(0, 80).Select(x => new Point2(x, 0)).ToList();
        if (closed) points.AddRange(Enumerable.Range(0, 80).Reverse().Select(x => new Point2(x, 10)));
        var document = new ProjectDocument { Pieces = [new PatternPiece { Closed = closed, Points = points }] };
        var output = HpglFormat.Prepare(document, 1);
        var commands = output.Text.Split(';').Select(x => x.Trim()).Where(x => x.Length != 0).ToArray();
        var penDown = commands.Where(x => x.StartsWith("PD", StringComparison.Ordinal)).ToArray();
        Assert.True(penDown.Length > 1);
        Assert.All(penDown, command => Assert.InRange(command.Length + 1, 1, HpglFormat.MaximumPenDownCommandBytes));
        var actual = penDown.SelectMany(command => command[2..].Split(','))
            .Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        var expectedPoints = points.Skip(1).Concat(closed ? points.Take(1) : Enumerable.Empty<Point2>());
        var expected = expectedPoints.SelectMany(point => new[] { (int)point.X, (int)point.Y }).ToArray();
        Assert.Equal(expected, actual);
        Assert.NotNull(PlotterHpglPreflight.Inspect(output, Profile(HpglFormat.MaximumPenDownCommandBytes)));
        var longest = penDown.Max(command => command.Length + 1);
        Assert.Throws<InvalidDataException>(() => PlotterHpglPreflight.Inspect(output, Profile(longest - 1)));
        Assert.Throws<InvalidDataException>(() => PlotterHpglPreflight.Inspect(output, Profile(128) with { MaximumXPlotUnits = 50 }));
    }
}
