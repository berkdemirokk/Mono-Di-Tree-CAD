using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class GrainlineModelTests
{
    private static ProjectDocument LegacyDocument() => new()
    {
        Name = "Örnek",
        Pieces = [new PatternPiece
        {
            Id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            Name = "A",
            Closed = true,
            Points = [new(0,0), new(10,0), new(0,20)],
            Metadata = new PieceMetadata { Role="Kesim", Size="M", Material="Pamuk", Quantity=2 }
        }]
    };

    private static Grainline Example() => new() { Start=new(1,2), End=new(3,4), NeedsReview=false };

    [Fact]
    public void CloneOwnsIndependentGrainlineAndEqualityIncludesEveryField()
    {
        var original=LegacyDocument(); original.Pieces[0].Grainline=Example();
        var clone=original.Clone();
        Assert.True(DocumentSession.ContentEquals(original,clone));
        Assert.NotSame(original.Pieces[0].Grainline,clone.Pieces[0].Grainline);
        clone.Pieces[0].Grainline!.Start=new(2,2);
        Assert.Equal(new Point2(1,2),original.Pieces[0].Grainline!.Start);
        Assert.False(DocumentSession.ContentEquals(original,clone));
        clone=original.Clone();clone.Pieces[0].Grainline!.End=new(3,5);
        Assert.False(DocumentSession.ContentEquals(original,clone));
        clone=original.Clone();clone.Pieces[0].Grainline!.NeedsReview=true;
        Assert.False(DocumentSession.ContentEquals(original,clone));
        clone=original.Clone();clone.Pieces[0].Grainline=null;
        Assert.False(DocumentSession.ContentEquals(original,clone));
    }

    [Fact]
    public void GrainlineOnlyEditsParticipateInDirtyUndoRedoAndNoOp()
    {
        var session=new DocumentSession(LegacyDocument());
        Assert.False(session.IsDirty);
        Assert.True(session.Execute(d=>d.Pieces[0].Grainline=Example()));
        Assert.True(session.IsDirty); Assert.Equal(1,session.UndoCount);
        Assert.False(session.Execute(d=>d.Pieces[0].Grainline=d.Pieces[0].Grainline!.Clone()));
        Assert.Equal(1,session.UndoCount);
        Assert.True(session.Execute(d=>d.Pieces[0].Grainline!.NeedsReview=true));
        Assert.True(session.Undo()); Assert.False(session.Document.Pieces[0].Grainline!.NeedsReview);
        Assert.True(session.Undo()); Assert.Null(session.Document.Pieces[0].Grainline); Assert.False(session.IsDirty);
        Assert.True(session.Redo()); Assert.Equal(new Point2(1,2),session.Document.Pieces[0].Grainline!.Start);
        Assert.True(session.Redo()); Assert.True(session.Document.Pieces[0].Grainline!.NeedsReview);
    }

    [Fact]
    public void LegacyFingerprintRetainsKnownV1Bytes()
    {
        // Independently constructed from the legacy BinaryWriter v1 layout,
        // including .NET Guid byte order, little-endian doubles and UTF-16 text.
        const string expected="6E88FB181763E66B4B067E652AFAE62B33591DCE6D793E2D547485D0B6176235";
        Assert.Equal(expected,NestingSnapshot.ComputeLayoutFingerprint(LegacyDocument()));
    }

    [Fact]
    public void GrainlineAdditionChangeAndRemovalInvalidateSnapshot()
    {
        var doc=LegacyDocument();var piece=doc.Pieces[0];
        var snapshot=new NestingSnapshot { LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(doc),
            Placements=[new NestingPlacementSnapshot(Guid.NewGuid(),1,piece.Id)] };
        Assert.True(snapshot.IsCurrentFor(doc));
        piece.Grainline=Example();Assert.False(snapshot.IsCurrentFor(doc));
        snapshot.LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(doc);
        Assert.True(snapshot.IsCurrentFor(doc));
        piece.Grainline.NeedsReview=true;Assert.False(snapshot.IsCurrentFor(doc));
        piece.Grainline.NeedsReview=false;Assert.True(snapshot.IsCurrentFor(doc));
        piece.Grainline.End=new(5,4);Assert.False(snapshot.IsCurrentFor(doc));
        piece.Grainline=null;Assert.False(snapshot.IsCurrentFor(doc));
    }
}
