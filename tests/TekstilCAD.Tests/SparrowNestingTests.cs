using System.Globalization;
using System.Text.Json;
using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

/// <summary>sparrow itself is not run here: these tests cover our JSON, the frame mapping and the
/// independent check, using hand-written solutions in sparrow's documented output shape.</summary>
public class SparrowNestingTests
{
    private static PatternPiece Rectangle(double width, double height) =>
        new() { Name = "Test", Points = [new(0, 0), new(width, 0), new(width, height), new(0, height)] };

    private static SparrowNestingRequest Request(PatternPiece[] pieces, bool allow180 = false, double gap = 5, double margin = 0) =>
        new(pieces, 100, gap, margin, allow180);

    private static string Solution(params (int Id, double Rotation, double U, double V, bool Reflected)[] items)
    {
        var placed = string.Join(",", items.Select(i =>
            $"{{\"item_id\":{i.Id},\"transformation\":{{" + (i.Reflected ? "\"reflected\":true," : "") +
            string.Create(CultureInfo.InvariantCulture, $"\"rotation\":{i.Rotation},\"translation\":[{i.U},{i.V}]}}}}")));
        return $"{{\"name\":\"tekstilcad\",\"items\":[],\"strip_height\":100,\"solution\":{{\"strip_width\":30,\"layout\":{{\"container_id\":0,\"placed_items\":[{placed}],\"density\":0.8}},\"density\":0.8,\"run_time_sec\":1}}}}";
    }

    private static Bounds2 Bounds(SparrowPlacement placement) => Geometry.Bounds(placement.Points);

    [Fact]
    public void InstanceJsonMapsFabricWidthToStripHeightWithoutMirroring()
    {
        var request = Request([Rectangle(40, 30)], allow180: true, gap: 4, margin: 10);
        using var json = JsonDocument.Parse(SparrowNesting.BuildInstanceJson(request));
        var root = json.RootElement;
        Assert.Equal("tekstilcad", root.GetProperty("name").GetString());
        Assert.Equal(80, root.GetProperty("strip_height").GetDouble());
        Assert.Equal(4, root.GetProperty("min_item_separation").GetDouble());
        var item = root.GetProperty("items")[0];
        Assert.Equal(0, item.GetProperty("id").GetInt32());
        Assert.Equal(1, item.GetProperty("demand").GetInt32());
        var rotation = item.GetProperty("orientation").GetProperty("rotation");
        Assert.Equal("discrete", rotation.GetProperty("mode").GetString());
        Assert.Equal(new[] { 0d, 180d }, rotation.GetProperty("angles").EnumerateArray().Select(a => a.GetDouble()).ToArray());
        var points = item.GetProperty("shape").GetProperty("data").EnumerateArray()
            .Select(p => (U: p[0].GetDouble(), V: p[1].GetDouble())).ToArray();
        // (u, v) = (y, -x): the 40 mm width becomes sparrow's vertical extent.
        Assert.Equal(0, points.Min(p => p.U)); Assert.Equal(30, points.Max(p => p.U));
        Assert.Equal(-40, points.Min(p => p.V)); Assert.Equal(0, points.Max(p => p.V));
        double signedArea = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var (a, b) = (points[i], points[(i + 1) % points.Length]);
            signedArea += a.U * b.V - b.U * a.V;
        }
        Assert.True(signedArea > 0, "sparrow input should be counter-clockwise.");
    }

    [Fact]
    public void RotationListOnlyHasZeroWhenTurningIsNotAllowed()
    {
        using var json = JsonDocument.Parse(SparrowNesting.BuildInstanceJson(Request([Rectangle(40, 30)])));
        var angles = json.RootElement.GetProperty("items")[0].GetProperty("orientation").GetProperty("rotation").GetProperty("angles");
        Assert.Equal(new[] { 0d }, angles.EnumerateArray().Select(a => a.GetDouble()).ToArray());
    }

    [Fact]
    public void SolutionIsMappedBackAndExactGapIsAccepted()
    {
        var request = Request([Rectangle(40, 30), Rectangle(40, 30)]);
        var result = SparrowNesting.ReadSolution(Solution((0, 0, 0, 40, false), (1, 0, 0, 85.0002, false)), request);
        var first = Bounds(result.Placements[0]);
        var second = Bounds(result.Placements[1]);
        Assert.Equal(60, first.MinX, 6); Assert.Equal(100, first.MaxX, 6);
        Assert.Equal(0, first.MinY, 6); Assert.Equal(30, first.MaxY, 6);
        Assert.Equal(15, second.MinX, 3); Assert.Equal(55, second.MaxX, 3);
        Assert.Equal(request.Pieces[1].Id, result.Placements[1].PieceId);
        Assert.Equal(30, result.UsedLength, 6);
        Assert.Equal(80, result.UtilizationPercent, 6);
    }

    [Fact]
    public void AllowedHalfTurnIsAppliedAsRotationNotMirror()
    {
        var piece = new PatternPiece { Name = "L", Points = [new(0, 0), new(40, 0), new(40, 10), new(10, 10), new(10, 30), new(0, 30)] };
        var result = SparrowNesting.ReadSolution(Solution((0, 180, 30, 0, false)), Request([piece], allow180: true));
        var placed = result.Placements[0];
        Assert.Equal(180, placed.RotationDegrees);
        var bounds = Bounds(placed);
        Assert.Equal(60, bounds.MinX, 6); Assert.Equal(100, bounds.MaxX, 6);
        Assert.Equal(0, bounds.MinY, 6); Assert.Equal(30, bounds.MaxY, 6);
        // A half turn sends the original origin corner (0,0) to the opposite corner (100,30).
        Assert.Equal(new Point2(100, 30), placed.Points[0]);
        Assert.Equal(Math.Abs(Geometry.SignedArea(piece.Points)), Math.Abs(Geometry.SignedArea(placed.Points)), 6);
    }

    [Fact]
    public void HalfTurnIsRejectedWhenNotAllowed() =>
        Assert.Throws<SparrowResultRejectedException>(() =>
            SparrowNesting.ReadSolution(Solution((0, 180, 30, 0, false)), Request([Rectangle(40, 30)])));

    [Fact]
    public void ReflectionIsRejected() =>
        Assert.Throws<SparrowResultRejectedException>(() =>
            SparrowNesting.ReadSolution(Solution((0, 0, 0, 40, true)), Request([Rectangle(40, 30)])));

    [Theory]
    [InlineData(84.0)]  // gap 4 mm instead of 5
    [InlineData(60.0)]  // overlap
    public void TooSmallGapOrOverlapIsRejected(double secondV) =>
        Assert.Throws<SparrowResultRejectedException>(() =>
            SparrowNesting.ReadSolution(Solution((0, 0, 0, 40, false), (1, 0, 0, secondV, false)), Request([Rectangle(40, 30), Rectangle(40, 30)])));

    [Theory]
    [InlineData(0, 39.0)]   // right edge at 101 mm
    [InlineData(0, 101.0)]  // left edge below 0
    [InlineData(-1, 40.0)]  // behind the marker start
    public void PlacementOutsideFabricIsRejected(double u, double v) =>
        Assert.Throws<SparrowResultRejectedException>(() =>
            SparrowNesting.ReadSolution(Solution((0, 0, u, v, false)), Request([Rectangle(40, 30)])));

    [Fact]
    public void EdgeMarginIsAHardBound()
    {
        var request = Request([Rectangle(40, 30)], margin: 10);
        var ok = SparrowNesting.ReadSolution(Solution((0, 0, 0, 40, false)), request);
        Assert.Equal(90, Bounds(ok.Placements[0]).MaxX, 6);
        Assert.Throws<SparrowResultRejectedException>(() => SparrowNesting.ReadSolution(Solution((0, 0, 0, 30, false)), request));
    }

    [Fact]
    public void MissingDuplicateOrUnknownItemsAreRejected()
    {
        var request = Request([Rectangle(40, 30), Rectangle(40, 30)]);
        Assert.Throws<SparrowResultRejectedException>(() => SparrowNesting.ReadSolution(Solution((0, 0, 0, 40, false)), request));
        Assert.Throws<SparrowResultRejectedException>(() => SparrowNesting.ReadSolution(Solution((0, 0, 0, 40, false), (0, 0, 0, 85, false)), request));
        Assert.Throws<SparrowResultRejectedException>(() => SparrowNesting.ReadSolution(Solution((0, 0, 0, 40, false), (2, 0, 0, 85, false)), request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"solution\":{\"layout\":{\"placed_items\":[{\"item_id\":0,\"transformation\":{\"rotation\":0,\"translation\":[1]}}]}}}")]
    [InlineData("{\"solution\":{\"layout\":{\"placed_items\":[{\"item_id\":\"x\",\"transformation\":{\"rotation\":0,\"translation\":[0,40]}}]}}}")]
    public void MalformedOutputIsRejected(string json) =>
        Assert.Throws<SparrowResultRejectedException>(() => SparrowNesting.ReadSolution(json, Request([Rectangle(40, 30)])));

    [Fact]
    public void PieceWiderThanUsableFabricIsReportedWithItsId()
    {
        var wide = Rectangle(90, 30);
        var error = Assert.ThrowsAny<ArgumentException>(() => SparrowNesting.BuildInstanceJson(Request([wide], margin: 10)));
        Assert.True(NestingPieceError.TryGet(error, out var id, out _));
        Assert.Equal(wide.Id, id);
    }

    [Fact]
    public async Task MissingExecutableIsReportedBeforeAnyWork() =>
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            SparrowNesting.RunAsync(Path.Combine(Path.GetTempPath(), "no-such-sparrow-" + Guid.NewGuid().ToString("N")), Request([Rectangle(40, 30)])));
}
