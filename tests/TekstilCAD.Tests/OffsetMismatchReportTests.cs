using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetMismatchReportTests
{
    private static List<Point2> Square(double origin = 0) =>
        [new(origin, origin), new(origin + 100, origin), new(origin + 100, origin + 100), new(origin, origin + 100)];

    private static List<Point2> Notched(double origin = 0) =>
    [
        new(origin, origin), new(origin + 100, origin), new(origin + 100, origin + 100),
        new(origin + 50.1, origin + 100), new(origin + 50, origin + 99.9),
        new(origin + 49.9, origin + 100), new(origin, origin + 100)
    ];

    [Fact]
    public void IdenticalContourAndOptionalClosingPointProduceNoReportedMismatch()
    {
        var source = Square();
        var output = Square();
        output.Add(output[0]);
        var report = OffsetMismatchReport.Analyze(source, output, .01, 150);
        Assert.Equal(4, report.SourceEdgeCount);
        Assert.Equal(4, report.OutputEdgeCount);
        Assert.Empty(report.Mismatches);
    }

    [Theory]
    [InlineData(0d, false)]
    [InlineData(0d, true)]
    [InlineData(50000000d, false)]
    [InlineData(50000000d, true)]
    public void ShortVEdgesBeyondToleranceAreReportedAcrossDirectionAndOrigin(double origin, bool reverse)
    {
        var source = Square(origin);
        var output = Notched(origin);
        if (reverse) { source.Reverse(); output.Reverse(); }
        var report = OffsetMismatchReport.Analyze(source, output, .01, .2);
        Assert.Equal(2, report.Mismatches.Count);
        Assert.All(report.Mismatches, edge =>
        {
            Assert.InRange(edge.LengthMm, .14, .15);
            Assert.InRange(edge.NearestSourceEdgeIndex, 0, 3);
            Assert.InRange(edge.NearestSourceVertexIndex, 0, 3);
            Assert.True(Math.Max(edge.StartDistanceMm, edge.EndDistanceMm) > .09);
            Assert.InRange(edge.MidpointDistanceMm, .049, .051);
        });
    }

    [Fact]
    public void RotatedStartIndexChangesIndicesButNotReportedDistances()
    {
        var source = Square();
        var output = Notched();
        var first = OffsetMismatchReport.Analyze(source, output, .01, .2);
        var rotatedSource = source.Skip(2).Concat(source.Take(2)).ToList();
        var rotatedOutput = output.Skip(3).Concat(output.Take(3)).ToList();
        var rotated = OffsetMismatchReport.Analyze(rotatedSource, rotatedOutput, .01, .2);
        Assert.Equal(2, first.Mismatches.Count);
        Assert.Equal(2, rotated.Mismatches.Count);
        Assert.Equal(first.Mismatches.Select(x => x.MidpointDistanceMm).Order().ToArray(),
            rotated.Mismatches.Select(x => x.MidpointDistanceMm).Order().ToArray());
        Assert.NotEqual(first.Mismatches.Select(x => x.OutputEdgeIndex), rotated.Mismatches.Select(x => x.OutputEdgeIndex));
    }

    [Fact]
    public void LengthAndToleranceAreDiagnosticFiltersNotCoverageCertification()
    {
        var source = Square();
        var output = Notched();
        Assert.Empty(OffsetMismatchReport.Analyze(source, output, .01, .1).Mismatches);
        Assert.Empty(OffsetMismatchReport.Analyze(source, output, .2, .2).Mismatches);
        Assert.NotEmpty(OffsetMismatchReport.Analyze(source, output, .01, .2).Mismatches);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1.01)]
    public void InvalidToleranceIsRejected(double tolerance)
        => Assert.Throws<ArgumentOutOfRangeException>(() => OffsetMismatchReport.Analyze(Square(), Square(), tolerance, 1));

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidEdgeLengthLimitIsRejected(double maxLength)
        => Assert.Throws<ArgumentOutOfRangeException>(() => OffsetMismatchReport.Analyze(Square(), Square(), .01, maxLength));

    [Fact]
    public void TooManyVerticesAreRejectedBeforeSampling()
    {
        var tooMany = Enumerable.Range(0, OffsetMismatchReport.MaximumVertices + 1)
            .Select(i => { var angle = i * Math.Tau / (OffsetMismatchReport.MaximumVertices + 1); return new Point2(Math.Cos(angle), Math.Sin(angle)); })
            .ToList();
        Assert.Throws<ArgumentException>(() => OffsetMismatchReport.Analyze(tooMany, Square(), .01, 1));
        Assert.Throws<ArgumentException>(() => OffsetMismatchReport.Analyze(Square(), tooMany, .01, 1));
    }
}
