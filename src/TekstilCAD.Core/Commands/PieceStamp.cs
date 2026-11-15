using System.Security.Cryptography;

namespace TekstilCAD.Core.Commands;

/// <summary>What a stamp covers besides the document reference and the project epoch.</summary>
public enum StampScope
{
    /// <summary>Nothing of the content (view, tool, window and file commands).</summary>
    None,
    /// <summary>One piece, its layer state and its grading rule.</summary>
    Piece,
    /// <summary>Every piece, every layer state and every grading rule (placing a new piece, selecting by order).</summary>
    Document,
}

/// <summary>Why a stamp is no longer valid.</summary>
public enum StampState
{
    Current,
    /// <summary>The window now holds another document object.</summary>
    DifferentDocument,
    /// <summary>The project was loaded or replaced (the epoch changed).</summary>
    EpochChanged,
    /// <summary>The target piece is no longer in the document.</summary>
    PieceMissing,
    /// <summary>A different piece (or none) is selected now.</summary>
    SelectionChanged,
    /// <summary>The content the plan was calculated from changed.</summary>
    ContentChanged,
}

/// <summary>
/// What a preview looked at: the document object, the project epoch, the target piece, the selection and a digest of the
/// content that the plan depends on. A plan is applied only while <see cref="IsCurrent"/> still answers true, so an old
/// preview is never applied to a changed project. Pure: no clock, no I/O, deterministic for equal content.
/// </summary>
public sealed class PieceStamp
{
    private readonly object document;
    private readonly Guid[] selection;
    private readonly string facts;

    private PieceStamp(object document, Guid projectEpoch, Guid? pieceId, StampScope scope, string digest, Guid[] selection, string facts)
    {
        this.document = document;
        ProjectEpoch = projectEpoch;
        PieceId = pieceId;
        Scope = scope;
        ContentDigest = digest;
        this.selection = selection;
        this.facts = facts;
    }

    /// <summary>The project epoch the plan was made at.</summary>
    public Guid ProjectEpoch { get; }

    /// <summary>The piece the plan acts on; null when it acts on no single piece.</summary>
    public Guid? PieceId { get; }

    /// <summary>What <see cref="ContentDigest"/> covers.</summary>
    public StampScope Scope { get; }

    /// <summary>SHA-256 (hex) of the covered content; empty for <see cref="StampScope.None"/>.</summary>
    public string ContentDigest { get; }

    /// <summary>The selected piece ids at planning time.</summary>
    public IReadOnlyList<Guid> SelectedIds => selection;

    /// <summary>True when <paramref name="other"/> is the document object this stamp was taken from.</summary>
    public bool IsSameDocument(ProjectDocument other) => ReferenceEquals(document, other);

    /// <summary>Takes a stamp of <paramref name="view"/> for a plan that acts on <paramref name="pieceId"/> (<see cref="StampScope.Piece"/>), on the whole document, or on nothing.</summary>
    public static PieceStamp Capture(DocumentView view, Selection? selection, Guid? pieceId, StampScope scope)
    {
        ArgumentNullException.ThrowIfNull(view);
        var ids = SelectedOf(selection);
        var effectiveScope = scope == StampScope.Piece && pieceId is null ? StampScope.None : scope;
        return new PieceStamp(view.Document, view.ProjectEpoch, pieceId, effectiveScope, Digest(view.Document, pieceId, effectiveScope), ids, Facts(view));
    }

    /// <summary>
    /// Compares this stamp with the current state. <paramref name="selection"/> is the current selection; pass null when the
    /// selection is not known (only document, epoch, piece and content are checked then).
    /// </summary>
    public StampState Check(DocumentView current, Selection? selection)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!ReferenceEquals(document, current.Document)) return StampState.DifferentDocument;
        if (ProjectEpoch != current.ProjectEpoch) return StampState.EpochChanged;
        if (PieceId is { } id && !current.Document.Pieces.Any(p => p.Id == id)) return StampState.PieceMissing;
        if (selection is not null && !SelectedOf(selection).SequenceEqual(this.selection)) return StampState.SelectionChanged;
        if (!string.Equals(Facts(current), facts, StringComparison.Ordinal)) return StampState.ContentChanged;
        return string.Equals(Digest(current.Document, PieceId, Scope), ContentDigest, StringComparison.Ordinal)
            ? StampState.Current
            : StampState.ContentChanged;
    }

    /// <summary>True while nothing the plan depends on changed. See <see cref="Check"/>.</summary>
    public bool IsCurrent(DocumentView current, Selection? selection) => Check(current, selection) == StampState.Current;

    // Facts outside the document that a preview sentence states: the undo and redo depth, the file name, the unsaved flag and the pending drawing.
    private static string Facts(DocumentView v) =>
        string.Join("|", v.UndoCount, v.RedoCount, v.FileName ?? "", v.HasUnsavedChanges, v.DrawingInProgress, v.DrawingPointCount);

    private static Guid[] SelectedOf(Selection? selection) =>
        selection?.PieceIds is { Count: > 0 } ids ? ids.ToArray() : Array.Empty<Guid>();

    // ---- digest -------------------------------------------------------------------------------------------------------

    private static string Digest(ProjectDocument document, Guid? pieceId, StampScope scope)
    {
        if (scope == StampScope.None) return string.Empty;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var writer = new DigestWriter(hash);
        if (scope == StampScope.Piece)
        {
            writer.Tag(1);
            var piece = document.Pieces.FirstOrDefault(p => p.Id == pieceId);
            if (piece is null) writer.Tag(0);
            else
            {
                WritePiece(writer, piece);
                WriteLayer(writer, document, piece.LayerId);
                foreach (var rule in document.GradingRules)
                    if (rule.SourcePieceId == piece.Id) WriteRule(writer, rule);
            }
        }
        else
        {
            writer.Tag(2);
            writer.Int(document.Pieces.Count);
            foreach (var piece in document.Pieces) WritePiece(writer, piece);
            writer.Int(document.GradingRules.Count);
            foreach (var rule in document.GradingRules) WriteRule(writer, rule);
            var layers = document.Layers ?? Layer.Defaults();
            writer.Int(layers.Count);
            foreach (var layer in layers) WriteLayerState(writer, layer);
        }
        writer.Flush();
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void WriteLayer(DigestWriter w, ProjectDocument document, Guid layerId)
    {
        var layer = LayerTools.Find(document, layerId);
        if (layer is null) w.Tag(0); else WriteLayerState(w, layer);
    }

    private static void WriteLayerState(DigestWriter w, Layer layer)
    {
        w.Guid(layer.Id);
        w.Bool(layer.Visible);
        w.Bool(layer.Locked);
    }

    private static void WritePiece(DigestWriter w, PatternPiece piece)
    {
        w.Guid(piece.Id);
        w.Guid(piece.LayerId);
        w.Text(piece.Name);
        w.Bool(piece.Closed);
        w.Int(piece.Points?.Count ?? -1);
        if (piece.Points is not null) foreach (var p in piece.Points) w.Point(p);
        if (piece.Curves is null) w.Tag(0);
        else
        {
            w.Tag(1);
            w.Int(piece.Curves.Count);
            foreach (var segment in piece.Curves)
            {
                switch (segment)
                {
                    case CurveSegment.Line l: w.Tag(10); w.Point(l.Start); w.Point(l.End); break;
                    case CurveSegment.CubicBezier b: w.Tag(11); w.Point(b.P0); w.Point(b.P1); w.Point(b.P2); w.Point(b.P3); break;
                    case CurveSegment.Arc a: w.Tag(12); w.Point(a.Center); w.Double(a.Radius); w.Double(a.StartAngle); w.Double(a.SweepAngle); break;
                    default: w.Tag(19); w.Text(segment?.GetType().FullName); break;
                }
            }
        }
        if (piece.Grainline is null) w.Tag(0);
        else { w.Tag(1); w.Point(piece.Grainline.Start); w.Point(piece.Grainline.End); w.Bool(piece.Grainline.NeedsReview); }
        if (piece.Notches is null) w.Tag(0);
        else
        {
            w.Tag(1);
            w.Int(piece.Notches.Count);
            foreach (var n in piece.Notches) { w.Int(n.VertexIndex); w.Point(n.Position); }
        }
        if (piece.Metadata is null) w.Tag(0);
        else { w.Tag(1); w.Text(piece.Metadata.Role); w.Text(piece.Metadata.Size); w.Text(piece.Metadata.Material); w.Int(piece.Metadata.Quantity ?? -1); }
    }

    private static void WriteRule(DigestWriter w, GradingRuleSet rule)
    {
        w.Tag(7);
        w.Guid(rule.SourcePieceId);
        w.Bool(rule.SourceClosed);
        w.Int(rule.SourcePoints.Count);
        foreach (var p in rule.SourcePoints) w.Point(p);
        w.Int(rule.Deltas.Count);
        foreach (var p in rule.Deltas) w.Point(p);
    }

    /// <summary>Feeds a hash with fixed-width values through a small buffer; no allocation per value.</summary>
    private sealed class DigestWriter
    {
        private readonly IncrementalHash hash;
        private readonly byte[] buffer = new byte[4096];
        private int length;

        public DigestWriter(IncrementalHash hash) => this.hash = hash;

        private Span<byte> Reserve(int count)
        {
            if (length + count > buffer.Length) Flush();
            var span = buffer.AsSpan(length, count);
            length += count;
            return span;
        }

        public void Flush()
        {
            if (length == 0) return;
            hash.AppendData(buffer, 0, length);
            length = 0;
        }

        public void Tag(byte value) => Reserve(1)[0] = value;
        public void Bool(bool value) => Tag(value ? (byte)1 : (byte)0);
        public void Int(int value) => BitConverter.TryWriteBytes(Reserve(4), value);
        public void Double(double value) => BitConverter.TryWriteBytes(Reserve(8), BitConverter.DoubleToInt64Bits(value));
        public void Point(Point2 p) { Double(p.X); Double(p.Y); }

        public void Guid(Guid value)
        {
            // Guid.TryWriteBytes needs 16 bytes; a stack buffer keeps the helper allocation free.
            Span<byte> bytes = stackalloc byte[16];
            value.TryWriteBytes(bytes);
            bytes.CopyTo(Reserve(16));
        }

        public void Text(string? value)
        {
            if (value is null) { Int(-1); return; }
            Int(value.Length);
            foreach (var c in value) BitConverter.TryWriteBytes(Reserve(2), (ushort)c);
        }
    }
}
