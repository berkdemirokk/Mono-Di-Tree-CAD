using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Small builders and checks shared by the piece-operation tests.</summary>
public static class PieceOperationTestData
{
    public static PatternPiece Rect(string name, double x, double y, double width, double height) => new()
    {
        Name = name,
        Points = [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)]
    };

    public static PatternPiece Segment(string name, double x, double y, double length) => new()
    {
        Name = name, Closed = false, Points = [new(x, y), new(x + length, y)]
    };

    public static ProjectDocument Doc(params PatternPiece[] pieces) => new() { Pieces = [.. pieces] };

    /// <summary>A 120 x 80 rectangle named "Ön beden" at (0, 0), alone in the document.</summary>
    public static ProjectDocument OneRect() => Doc(Rect("Ön beden", 0, 0, 120, 80));

    public static Point2[] Pts(params (double X, double Y)[] values) => values.Select(v => new Point2(v.X, v.Y)).ToArray();

    public static PatternPiece Find(PieceOperationResult result, Guid id) => result.Document!.Pieces.Single(p => p.Id == id);

    /// <summary>Runs <paramref name="operation"/> and checks it succeeded, left its input alone and gives a valid document.</summary>
    public static PieceOperationResult Run(ProjectDocument document, Func<ProjectDocument, PieceOperationResult> operation)
    {
        var snapshot = document.Clone();
        var result = operation(document);
        Assert.True(DocumentSession.ContentEquals(snapshot, document), "The operation changed its input document.");
        Assert.True(result.Succeeded, result.Error);
        ProjectStorage.Validate(result.Document!);
        return result;
    }

    /// <summary>Execute through a session, then undo and redo; the document must come back bit for bit.</summary>
    public static void AssertUndoRoundTrip(ProjectDocument start, Func<ProjectDocument, PieceOperationResult> operation)
    {
        var session = new DocumentSession(start);
        var before = session.Document.Clone();
        var result = Run(session.Document, operation);
        Assert.True(result.ChangesDocument);
        Assert.True(session.Execute(result.ApplyTo, ProjectStorage.Validate));
        Assert.True(DocumentSession.ContentEquals(result.Document!, session.Document));
        Assert.Equal(1, session.UndoCount);
        Assert.True(session.Undo());
        Assert.True(DocumentSession.ContentEquals(before, session.Document));
        Assert.True(session.Redo());
        Assert.True(DocumentSession.ContentEquals(result.Document!, session.Document));
    }

    public static void AssertRefused(PieceOperationResult result, PieceOperationError code, string message)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(code, result.ErrorCode);
        Assert.Equal(message, result.Error);
        Assert.Null(result.Document);
        Assert.False(result.ChangesDocument);
    }

    public static ProjectDocument WithLayers(ProjectDocument document, params Layer[] extra)
    {
        document.Layers = [.. Layer.Defaults(), .. extra];
        return document;
    }
}
