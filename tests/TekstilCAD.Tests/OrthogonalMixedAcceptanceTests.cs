using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class OrthogonalMixedAcceptanceTests
{
    [Fact]
    public void OneWayJobUsesOnlyPermittedRotationToCorrectReverseGrain()
    {
        var piece = new PatternPiece
        {
            Points = [new(0, 0), new(20, 0), new(20, 30), new(0, 30)],
            Grainline = new() { Start = new(5, 20), End = new(5, 10) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 2 }
        };
        var source = new ProjectDocument { Pieces = [piece] };
        var permitted = new Dictionary<Guid, IReadOnlyList<int>> { [piece.Id] = [0, 180] };
        var result = OrthogonalGrainNesting.ArrangeWithPieceRotations(source, [piece.Id], 100, 2, 100, 1,
            permitted, GrainDirectionPolicy.Direction.OneWay, 0);
        Assert.Equal(180, result.SourceRotations[piece.Id]);
        Assert.All(result.Plan.Nesting.Layout.Pieces, p =>
            Assert.True(p.Grainline!.End.Y > p.Grainline.Start.Y));
        Assert.True(OrthogonalTraceVerifier.IsValidForConstraints(source, [piece.Id], result, permitted,
            GrainDirectionPolicy.Direction.OneWay, 0, 100, 2, 1, 100));
        permitted[piece.Id] = [0];
        var error = Assert.Throws<ArgumentException>(() => OrthogonalGrainNesting.ArrangeWithPieceRotations(
            source, [piece.Id], 100, 2, 100, 1, permitted, GrainDirectionPolicy.Direction.OneWay, 0));
        Assert.True(NestingPieceError.TryGet(error, out var sourceId, out _));
        Assert.Equal(piece.Id, sourceId);
        Assert.Equal(new Point2(5, 10), piece.Grainline!.End);
    }

    [Fact]
    public void MissingGrainOnLaterSourceRejectsWholeJobAndReportsItsIdentity()
    {
        PatternPiece Make(string name) => new()
        {
            Name = name, Points = [new(0, 0), new(20, 0), new(20, 30), new(0, 30)],
            Grainline = new() { Start = new(5, 5), End = new(5, 15) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 2 }
        };
        var valid = Make("Geçerli"); var missing = Make("Eksik");
        missing.Grainline = null;
        var source = new ProjectDocument { Pieces = [valid, missing] };
        var permitted = new Dictionary<Guid, IReadOnlyList<int>> { [valid.Id] = [0], [missing.Id] = [0, 180] };
        var originalPoints = valid.Points.ToArray();
        var error = Assert.Throws<ArgumentException>(() => OrthogonalGrainNesting.ArrangeWithPieceRotations(
            source, [valid.Id, missing.Id], 100, 2, 100, 1, permitted, GrainDirectionPolicy.Direction.OneWay, 0));
        Assert.True(NestingPieceError.TryGet(error, out var sourceId, out _));
        Assert.Equal(missing.Id, sourceId);
        Assert.Equal(originalPoints, valid.Points);
        Assert.Null(missing.Grainline);
        Assert.Equal(2, valid.Metadata!.Quantity);
    }

    [Fact]
    public void MixedConcaveAndRectangularJobsRespectConstraintsAndCopyCounts()
    {
        var random = new Random(71931);
        for (int job = 0; job < 30; job++)
        {
            var pieces = new List<PatternPiece>();
            var permitted = new Dictionary<Guid, IReadOnlyList<int>>();
            for (int i = 0; i < 5; i++)
            {
                int width = random.Next(20, 101), height = random.Next(20, 101);
                int cutX = width / 2, cutY = height / 2;
                var points = i % 2 == 0
                    ? new List<Point2> { new(0, 0), new(width, 0), new(width, height), new(0, height) }
                    : new List<Point2> { new(0, 0), new(width, 0), new(width, cutY),
                        new(cutX, cutY), new(cutX, height), new(0, height) };
                var piece = new PatternPiece { Name = $"Parça {i}", Points = points,
                    Grainline = new Grainline { Start = new(0, 0), End = new(0, 5) },
                    Metadata = new PieceMetadata { Role = "Kesim", Material = "Pamuk", Quantity = random.Next(1, 5) } };
                pieces.Add(piece);
                permitted.Add(piece.Id, i % 2 == 0 ? [0, 180] : [0, 90, 180, 270]);
            }
            var source = new ProjectDocument { Pieces = pieces };
            var ids = pieces.Select(p => p.Id).ToArray();
            var result = OrthogonalGrainNesting.ArrangeWithPieceRotations(source, ids, 1000, 3, 10000, 2,
                permitted, GrainDirectionPolicy.Direction.TwoWay, 90);
            Assert.True(OrthogonalTraceVerifier.IsValidForConstraints(source, ids, result, permitted,
                GrainDirectionPolicy.Direction.TwoWay, 90, 1000, 3, 2, 10000));
            Assert.Equal(pieces.Sum(p => p.Metadata!.Quantity!.Value), result.Plan.Placements.Count);
            var boxes = result.Plan.Nesting.Layout.Pieces.Select(p => Geometry.Bounds(p.Points)).ToArray();
            foreach (var box in boxes)
            {
                Assert.True(box.MinX >= 2 && box.MaxX <= 998);
                Assert.True(box.MinY >= 2 && box.MaxY <= result.Plan.Nesting.UsedLength - 2);
            }
            for (int i = 0; i < boxes.Length; i++)
                for (int j = i + 1; j < boxes.Length; j++)
                {
                    var a = boxes[i]; var b = boxes[j];
                    Assert.True(a.MaxX + 3 <= b.MinX || b.MaxX + 3 <= a.MinX ||
                        a.MaxY + 3 <= b.MinY || b.MaxY + 3 <= a.MinY);
                }
            foreach (var piece in pieces)
            {
                Assert.Contains(result.SourceRotations[piece.Id], permitted[piece.Id]);
                Assert.Equal(piece.Metadata!.Quantity!.Value,
                    result.Plan.Placements.Count(p => p.SourcePieceId == piece.Id));
            }
        }
    }
}
