using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace TekstilCAD.Core;

/// <summary>A world coordinate in millimetres. Validation occurs at geometry and persistence boundaries.</summary>
public readonly record struct Point2(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2 operator *(Point2 p, double scale) => new(p.X * scale, p.Y * scale);
}

public readonly record struct Bounds2(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public Point2 Center => new(MinX / 2 + MaxX / 2, MinY / 2 + MaxY / 2);
}

public sealed class PieceMetadata
{
 public string? Role { get; set; }
 public string? Size { get; set; }
 public string? Material { get; set; }
 public int? Quantity { get; set; }
 public PieceMetadata Clone() => new() { Role=Role, Size=Size, Material=Material, Quantity=Quantity };
}

/// <summary>
/// A piece-associated grainline in world millimetres. NeedsReview records that
/// its relationship to edited geometry requires user review; it is not a
/// production or machine-compatibility certification.
/// Validation occurs at persistence and geometry operation boundaries.
/// </summary>
public sealed class Grainline
{
    public Point2 Start { get; set; }
    public Point2 End { get; set; }
    public bool NeedsReview { get; set; }
    public Grainline Clone() => new() { Start=Start, End=End, NeedsReview=NeedsReview };
}

/// <summary>A visual marker anchored to an existing contour vertex, with no cutting depth or machine semantics.</summary>
public sealed class NotchMark
{
    public int VertexIndex { get; set; }
    public Point2 Position { get; set; }
    public NotchMark Clone() => new() { VertexIndex=VertexIndex,Position=Position };
}

/// <summary>Unapproved experimental lower-section provenance; output callers must enforce the experimental guard.</summary>
public sealed record ALineExperimentalProvenance(string Algorithm,string SourceSha256,string Side)
{
    public static ALineExperimentalProvenance? Merge(ALineExperimentalProvenance? first,ALineExperimentalProvenance? second)
    {
        if(first is null)return second;if(second is null)return first;
        if(first!=second)throw new InvalidOperationException("Farklı deneysel A kesim kaynakları birleştirilemiyor; kısıtlı kaynak bilgisi kaybolamaz.");
        return first;
    }

    public const string CurrentAlgorithm="A-04.lower-section.experimental.v1";
    public void Validate()
    {
        if(Algorithm!=CurrentAlgorithm || Side is not ("front" or "back") || SourceSha256 is null || SourceSha256.Length!=64 ||
           SourceSha256.Any(c=>!(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new InvalidDataException("Deneysel A kesim kaynak kaydı geçersiz.");
    }
}

/// <summary>Fail-closed guard for output entry points; callers must invoke it before exporting or simulating.</summary>
public static class ExperimentalOutputGuard
{
    public static void EnsureAllowed(IEnumerable<PatternPiece> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        if(pieces.Any(p=>p is null || p.ExperimentalALine is not null))
            throw new InvalidOperationException("Deneysel A kesim taslağı çıktı veya makine işlemi için onaylanmadı.");
    }
}

public sealed class PatternPiece
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Kalıp";
    public List<Point2> Points { get; set; } = [];
    public bool Closed { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PieceMetadata? Metadata { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Grainline? Grainline { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NotchMark>? Notches { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ALineExperimentalProvenance? ExperimentalALine { get; set; }
    public PatternPiece Clone() => new() { Id = Id, Name = Name, Points = [.. Points], Closed = Closed, Metadata = Metadata?.Clone(), Grainline=Grainline?.Clone(), Notches=Notches?.Select(n=>n.Clone()).ToList(), ExperimentalALine=ExperimentalALine };
}

// Rotation names describe mathematical Y-up coordinates. On the Y-down
// canvas RotateLeft appears clockwise; UI labels must use screen direction.
public enum OrthogonalGradingTransform { RotateLeft, RotateRight, MirrorVertical, MirrorHorizontal, RotateHalf }

public sealed class GradingRuleSet
{
    public Guid SourcePieceId { get; set; }
    public bool SourceClosed { get; set; }
    public List<Point2> SourcePoints { get; set; } = [];
    public List<Point2> Deltas { get; set; } = [];
    public GradingRuleSet Clone() => new() { SourcePieceId = SourcePieceId, SourceClosed = SourceClosed, SourcePoints = [.. SourcePoints], Deltas = [.. Deltas] };

    /// <summary>Transform a matching rule only when its source geometry is exactly the same rigid transformation.</summary>
    public GradingRuleSet? OrthogonalFor(PatternPiece source,IReadOnlyList<Point2> transformed,OrthogonalGradingTransform operation)
    {
        if(source.Id!=SourcePieceId || source.Closed!=SourceClosed || Deltas.Count!=source.Points.Count ||
           !SourcePoints.SequenceEqual(source.Points) || transformed.Count!=source.Points.Count) return null;
        static Point2 Map(Point2 p,OrthogonalGradingTransform op)=>op switch {
            OrthogonalGradingTransform.RotateLeft=>new(-p.Y,p.X),
            OrthogonalGradingTransform.RotateRight=>new(p.Y,-p.X),
            OrthogonalGradingTransform.RotateHalf=>new(-p.X,-p.Y),
            OrthogonalGradingTransform.MirrorVertical=>new(-p.X,p.Y),
            OrthogonalGradingTransform.MirrorHorizontal=>new(p.X,-p.Y),
            _=>throw new ArgumentOutOfRangeException(nameof(op))
        };
        var rotated=source.Points.Select(p=>Map(p,operation)).ToArray();
        // A pivot only adds a constant translation. This exact dyadic check
        // refuses to attach deltas if the represented contour was distorted.
        if(!IsExactPointTranslation(rotated,transformed))return null;
        var result=Clone();result.SourcePoints=transformed.ToList();result.Deltas=Deltas.Select(p=>Map(p,operation)).ToList();
        return result;
    }

    /// <summary>Verify that a rule and its source changed only by an exactly represented translation.</summary>
    public bool IsExactTranslationTo(GradingRuleSet current, PatternPiece piece)
    {
        if (current is null || piece is null || SourcePieceId != current.SourcePieceId || piece.Id != SourcePieceId ||
            SourceClosed != current.SourceClosed || piece.Closed != SourceClosed || SourcePoints.Count == 0 ||
            SourcePoints.Count != current.SourcePoints.Count || Deltas.Count != SourcePoints.Count ||
            !Deltas.SequenceEqual(current.Deltas) || !current.SourcePoints.SequenceEqual(piece.Points)) return false;
        return IsExactPointTranslation(SourcePoints,current.SourcePoints);
    }

    /// <summary>Compare exact binary coordinate differences against the first point's displacement.</summary>
    public static bool IsExactPointTranslation(IReadOnlyList<Point2> before,IReadOnlyList<Point2> after)
    {
        if(before is null || after is null || before.Count==0 || before.Count!=after.Count) return false;
        static System.Numerics.BigInteger Units(double value)
        {
            var bits = BitConverter.DoubleToInt64Bits(value); var exponent = (int)((bits >> 52) & 0x7ff);
            var mantissa = bits & 0x000f_ffff_ffff_ffffL;
            if (exponent != 0) mantissa |= 1L << 52;
            var units = new System.Numerics.BigInteger(mantissa) << (exponent == 0 ? 0 : exponent - 1);
            return bits < 0 ? -units : units;
        }
        if (before.Any(p => !p.IsFinite) || after.Any(p => !p.IsFinite)) return false;
        var dx = Units(after[0].X) - Units(before[0].X);
        var dy = Units(after[0].Y) - Units(before[0].Y);
        for (var i = 1; i < before.Count; i++)
            if (Units(after[i].X) - Units(before[i].X) != dx ||
                Units(after[i].Y) - Units(before[i].Y) != dy) return false;
        return true;
    }

    /// <summary>Keep a matching rule attached to a safely translated source contour.</summary>
    public GradingRuleSet? TranslatedFor(PatternPiece source, IReadOnlyList<Point2> translated, double dx, double dy)
    {
        if (source.Id != SourcePieceId || source.Closed != SourceClosed ||
            Deltas.Count != source.Points.Count || !SourcePoints.SequenceEqual(source.Points)) return null;
        // Recompute with the same geometry precision checks so an unrelated edit
        // cannot accidentally be adopted as a translation.
        if (!Geometry.Translate(source.Points, dx, dy).SequenceEqual(translated)) return null;
        var result = Clone(); result.SourcePoints = translated.ToList();
        return result;
    }

    /// <summary>Carry a matching rule through an exactly representable edge split.</summary>
    public GradingRuleSet? WithEdgeSplitFor(PatternPiece source,PatternPiece edited,int afterIndex)
    {
        if(source.Id!=SourcePieceId || source.Closed!=SourceClosed || edited.Closed!=source.Closed ||
           Deltas.Count!=source.Points.Count || !SourcePoints.SequenceEqual(source.Points) ||
           afterIndex<0 || afterIndex>=source.Points.Count || edited.Points.Count!=source.Points.Count+1) return null;
        if(source.Closed && source.Points.Count>1 && source.Points[0]==source.Points[^1] && afterIndex==source.Points.Count-1)afterIndex=0;
        if(!source.Closed && afterIndex==source.Points.Count-1)return null;
        var next=(afterIndex+1)%source.Points.Count;
        var a=source.Points[afterIndex];var b=source.Points[next];var p=edited.Points[afterIndex+1];
        var expected=source.Points.ToList();expected.Insert(afterIndex+1,p);
        if(!expected.SequenceEqual(edited.Points) || !a.IsFinite || !b.IsFinite || !p.IsFinite || !Deltas[afterIndex].IsFinite || !Deltas[next].IsFinite)return null;
        static System.Numerics.BigInteger Units(double value)
        {
            var bits=BitConverter.DoubleToInt64Bits(value);var exponent=(int)((bits>>52)&0x7ff);
            var mantissa=bits&0x000f_ffff_ffff_ffffL;if(exponent!=0)mantissa|=1L<<52;
            var units=new System.Numerics.BigInteger(mantissa) << (exponent==0 ? 0 : exponent-1);
            return bits<0 ? -units : units;
        }
        var dx=Units(b.X)-Units(a.X);var dy=Units(b.Y)-Units(a.Y);
        var px=Units(p.X)-Units(a.X);var py=Units(p.Y)-Units(a.Y);
        if(dx*py!=dy*px)return null;
        var denominator=dx!=0 ? dx : dy;var numerator=dx!=0 ? px : py;
        if(denominator.Sign<0) { denominator=-denominator;numerator=-numerator; }
        if(denominator.IsZero || numerator<=0 || numerator>=denominator)return null;
        var fraction=dx!=0 ? (p.X-a.X)/(b.X-a.X) : (p.Y-a.Y)/(b.Y-a.Y);
        var da=Deltas[afterIndex];var db=Deltas[next];
        var interpolated=new Point2(da.X+(db.X-da.X)*fraction,da.Y+(db.Y-da.Y)*fraction);
        if(!interpolated.IsFinite ||
           (Units(interpolated.X)-Units(da.X))*denominator!=(Units(db.X)-Units(da.X))*numerator ||
           (Units(interpolated.Y)-Units(da.Y))*denominator!=(Units(db.Y)-Units(da.Y))*numerator)return null;
        var result=Clone();result.SourcePoints=expected;result.Deltas.Insert(afterIndex+1,interpolated);return result;
    }

    /// <summary>Carry a matching rule through an edge midpoint insertion using linearly interpolated increments.</summary>
    public GradingRuleSet? WithEdgeMidpointFor(PatternPiece source, PatternPiece edited, int afterIndex)
    {
        if (source.Id != SourcePieceId || source.Closed != SourceClosed || edited.Closed != source.Closed ||
            Deltas.Count != source.Points.Count || !SourcePoints.SequenceEqual(source.Points) ||
            afterIndex < 0 || afterIndex >= source.Points.Count) return null;
        var repeated = source.Closed && source.Points.Count > 1 && source.Points[0] == source.Points[^1];
        if (repeated && afterIndex == source.Points.Count - 1) afterIndex = 0;
        if (!source.Closed && afterIndex == source.Points.Count - 1) return null;
        var next = (afterIndex + 1) % source.Points.Count;
        var a = source.Points[afterIndex]; var b = source.Points[next];
        var midpoint = new Point2((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var expected = source.Points.ToList(); expected.Insert(afterIndex + 1, midpoint);
        if (!expected.SequenceEqual(edited.Points)) return null;
        var da = Deltas[afterIndex]; var db = Deltas[next];
        var delta = new Point2((da.X + db.X) / 2, (da.Y + db.Y) / 2);
        if (!da.IsFinite || !db.IsFinite || !delta.IsFinite) return null;
        // A rounded increment changes the graded edge. Preserve the old rule
        // for review unless both exact binary64 midpoints are representable.
        static System.Numerics.BigInteger Units(double value)
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            var exponent = (int)((bits >> 52) & 0x7ff);
            var mantissa = bits & 0x000f_ffff_ffff_ffffL;
            if (exponent != 0) mantissa |= 1L << 52;
            var units = new System.Numerics.BigInteger(mantissa) << (exponent == 0 ? 0 : exponent - 1);
            return bits < 0 ? -units : units;
        }
        if (2 * Units(delta.X) != Units(da.X) + Units(db.X) ||
            2 * Units(delta.Y) != Units(da.Y) + Units(db.Y)) return null;
        var result = Clone(); result.SourcePoints = expected; result.Deltas.Insert(afterIndex + 1, delta);
        return result;
    }

    /// <summary>Slice an exact open-path rule onto two independently identified split outputs.</summary>
    public (GradingRuleSet First,GradingRuleSet Second)? SplitOpenPathFor(PatternPiece source,PatternPiece first,PatternPiece second,int index,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(first);ArgumentNullException.ThrowIfNull(second);token.ThrowIfCancellationRequested();
        if(source.Id!=SourcePieceId || source.Closed || SourceClosed || first.Closed || second.Closed ||
           first.Id==Guid.Empty || second.Id==Guid.Empty || first.Id==second.Id || first.Id==source.Id || second.Id==source.Id ||
           source.Points is null || first.Points is null || second.Points is null || SourcePoints is null || Deltas is null ||
           index<=0 || index>=source.Points.Count-1 || SourcePoints.Count!=source.Points.Count || Deltas.Count!=source.Points.Count ||
           first.Points.Count!=index+1 || second.Points.Count!=source.Points.Count-index)return null;
        static bool Bits(Point2 a,Point2 b)=>BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) &&
            BitConverter.DoubleToInt64Bits(a.Y)==BitConverter.DoubleToInt64Bits(b.Y);
        for(var i=0;i<source.Points.Count;i++)
        {
            token.ThrowIfCancellationRequested();var p=source.Points[i];
            if(!p.IsFinite || !Deltas[i].IsFinite || !Bits(SourcePoints[i],p) ||
               (i<=index && !Bits(first.Points[i],p)) || (i>=index && !Bits(second.Points[i-index],p)))return null;
        }
        token.ThrowIfCancellationRequested();
        return (new GradingRuleSet { SourcePieceId=first.Id,SourceClosed=false,SourcePoints=[..first.Points],Deltas=Deltas.Take(index+1).ToList() },
                new GradingRuleSet { SourcePieceId=second.Id,SourceClosed=false,SourcePoints=[..second.Points],Deltas=Deltas.Skip(index).ToList() });
    }

    /// <summary>Retain increments only for a proven single-vertex move of the exact saved source.</summary>
    public GradingRuleSet? WithVertexMoveFor(PatternPiece source,IReadOnlyList<Point2> moved,int index)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(moved);
        if(source.Id!=SourcePieceId || source.Closed!=SourceClosed || source.Points is null || SourcePoints is null || Deltas is null ||
           index<0 || index>=source.Points.Count || source.Points.Count!=SourcePoints.Count ||
           Deltas.Count!=SourcePoints.Count || moved.Count!=SourcePoints.Count)return null;
        static bool Bits(Point2 a,Point2 b)=>BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) &&
            BitConverter.DoubleToInt64Bits(a.Y)==BitConverter.DoubleToInt64Bits(b.Y);
        var repeated=source.Closed && source.Points.Count>1 && source.Points[0]==source.Points[^1];
        if(repeated && (!Bits(source.Points[0],source.Points[^1]) || !Bits(Deltas[0],Deltas[^1])))return null;
        if(repeated && index==source.Points.Count-1)index=0;
        for(var i=0;i<source.Points.Count;i++)
        {
            if(!source.Points[i].IsFinite || !moved[i].IsFinite || !Deltas[i].IsFinite || !Bits(SourcePoints[i],source.Points[i]))return null;
            if(i!=index && !(repeated && index==0 && i==source.Points.Count-1) && !Bits(source.Points[i],moved[i]))return null;
        }
        if(repeated && !Bits(moved[0],moved[^1]))return null;
        var result=Clone();result.SourcePoints=moved.ToList();return result;
    }

    /// <summary>Remove one vertex and its increment only when the source and result match bit for bit.</summary>
    public GradingRuleSet? WithVertexDeletionFor(PatternPiece source,PatternPiece edited,int index)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(edited);
        if(source.Id!=SourcePieceId || edited.Id!=source.Id || source.Closed!=SourceClosed || edited.Closed!=SourceClosed ||
           SourcePoints is null || Deltas is null || source.Points is null || edited.Points is null ||
           index<0 || index>=source.Points.Count || SourcePoints.Count!=source.Points.Count ||
           Deltas.Count!=source.Points.Count || edited.Points.Count!=source.Points.Count-1)return null;
        static bool Bits(Point2 a,Point2 b)=>BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) &&
            BitConverter.DoubleToInt64Bits(a.Y)==BitConverter.DoubleToInt64Bits(b.Y);
        for(var i=0;i<source.Points.Count;i++)
            if(!source.Points[i].IsFinite || !Deltas[i].IsFinite || !Bits(SourcePoints[i],source.Points[i]))return null;
        var repeated=source.Closed && source.Points.Count>1 && source.Points[0]==source.Points[^1];
        if(repeated && (!Bits(source.Points[0],source.Points[^1]) || !Bits(Deltas[0],Deltas[^1])))return null;
        if(repeated && index==source.Points.Count-1)index=0;
        var points=source.Points.ToList();var deltas=Deltas.ToList();
        if(repeated && index==0)
        {
            points.RemoveAt(points.Count-1);deltas.RemoveAt(deltas.Count-1);
            points.RemoveAt(0);deltas.RemoveAt(0);
            if(points.Count==0)return null;
            points.Add(points[0]);deltas.Add(deltas[0]);
        }
        else { points.RemoveAt(index);deltas.RemoveAt(index); }
        if(points.Count!=edited.Points.Count || points.Count-(repeated ? 1 : 0)<(SourceClosed ? 3 : 2))return null;
        for(var i=0;i<points.Count;i++)if(!Bits(points[i],edited.Points[i]))return null;
        return new GradingRuleSet { SourcePieceId=SourcePieceId,SourceClosed=SourceClosed,SourcePoints=points,Deltas=deltas };
    }

    /// <summary>Remap a rule only when removed consecutive copies carry bit-identical increments.</summary>
    public GradingRuleSet? WithDuplicateRemovalFor(PatternPiece source,PatternPiece edited,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(edited);token.ThrowIfCancellationRequested();
        if(source.Id!=SourcePieceId || edited.Id!=source.Id || source.Closed!=SourceClosed || edited.Closed!=SourceClosed ||
           SourcePoints is null || Deltas is null || source.Points is null || edited.Points is null ||
           SourcePoints.Count!=source.Points.Count || Deltas.Count!=source.Points.Count || edited.Points.Count<1 ||
           edited.Points.Count>=source.Points.Count)return null;
        static bool Bits(Point2 a,Point2 b)=>BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) &&
            BitConverter.DoubleToInt64Bits(a.Y)==BitConverter.DoubleToInt64Bits(b.Y);
        var outputPoints=new List<Point2>(edited.Points.Count);var outputDeltas=new List<Point2>(edited.Points.Count);var target=0;
        for(var i=0;i<source.Points.Count;i++)
        {
            token.ThrowIfCancellationRequested();var point=source.Points[i];var delta=Deltas[i];
            if(!point.IsFinite || !delta.IsFinite || !Bits(SourcePoints[i],point))return null;
            if(target<edited.Points.Count && Bits(point,edited.Points[target]))
            { outputPoints.Add(point);outputDeltas.Add(delta);target++; }
            else if(outputPoints.Count==0 || !Bits(point,outputPoints[^1]) || !Bits(delta,outputDeltas[^1]))return null;
        }
        if(target!=edited.Points.Count)return null;
        if(SourceClosed && outputPoints.Count>1 && outputPoints[0]==outputPoints[^1] &&
           (!Bits(outputPoints[0],outputPoints[^1]) || !Bits(outputDeltas[0],outputDeltas[^1])))return null;
        token.ThrowIfCancellationRequested();return new GradingRuleSet { SourcePieceId=SourcePieceId,SourceClosed=SourceClosed,SourcePoints=outputPoints,Deltas=outputDeltas };
    }
    /// <summary>Reorder a matching closed-contour rule with its vertices, keeping the first vertex fixed.</summary>
    public GradingRuleSet? ReversedClosedFor(PatternPiece source)
    {
        if (source.Id != SourcePieceId || !source.Closed || !SourceClosed ||
            Deltas.Count != source.Points.Count || !SourcePoints.SequenceEqual(source.Points)) return null;
        var result = Clone();
        var repeated = SourcePoints.Count > 1 && SourcePoints[0] == SourcePoints[^1];
        var count = SourcePoints.Count - 1 - (repeated ? 1 : 0);
        if (count < 0) return null;
        result.SourcePoints.Reverse(1, count);
        result.Deltas.Reverse(1, count);
        return result;
    }

    /// <summary>Reorder a rule only when it describes the exact open source contour.</summary>
    public GradingRuleSet? ReversedFor(PatternPiece source)
    {
        if (source.Id != SourcePieceId || source.Closed || SourceClosed ||
            Deltas.Count != source.Points.Count || !SourcePoints.SequenceEqual(source.Points)) return null;
        var result = Clone();
        result.SourcePoints.Reverse();
        result.Deltas.Reverse();
        return result;
    }
}

/// <summary>Historical mapping to a source piece in another project; it is not a live link.</summary>
public sealed record NestingPlacementSnapshot(Guid SourcePieceId, int CopyNumber, Guid PlacedPieceId);

/// <summary>Historical inputs and mapping of a calculated layout, not a production or machine certificate.
/// The fingerprint records the placed pieces at calculation time; IsCurrentFor detects subsequent edits.</summary>
public sealed class NestingSnapshot
{
    public const string ShelfAlgorithm = "deterministic-shelf-v1";
    public double FabricWidth { get; set; }
    public double UsedLength { get; set; }
    public double Gap { get; set; }
    public double EdgeMargin { get; set; }
    public string Algorithm { get; set; } = ShelfAlgorithm;
    public string LayoutFingerprint { get; set; } = "";
    public List<NestingPlacementSnapshot> Placements { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RotatedNestingTrace? RotationTrace { get; set; }

    public NestingSnapshot Clone() => new() { FabricWidth=FabricWidth, UsedLength=UsedLength, Gap=Gap,
        EdgeMargin=EdgeMargin, Algorithm=Algorithm, LayoutFingerprint=LayoutFingerprint, Placements=[.. Placements],RotationTrace=RotationTrace?.Clone() };

    /// <summary>Checks whether all placed piece content still matches the historical snapshot.
    /// This is not a guarantee of machine suitability or production correctness.</summary>
    public bool IsCurrentFor(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if(Placements is null || document.Pieces is null || Placements.Count != document.Pieces.Count) return false;
        var ids=new HashSet<Guid>();
        foreach(var piece in document.Pieces)
            if(piece is null || piece.Id==Guid.Empty || !ids.Add(piece.Id))return false;
        foreach(var placement in Placements)
            if(placement is null || !ids.Remove(placement.PlacedPieceId))return false;
        return ids.Count==0 && string.Equals(LayoutFingerprint,ComputeLayoutFingerprint(document),StringComparison.Ordinal);
    }

    /// <summary>Stable SHA-256 over ordered piece identity, geometry, names and metadata.
    /// Grainline-bearing layouts use a domain-separated v2 encoding; layouts
    /// without grainlines retain the exact legacy v1 encoding. Experimental markers
    /// use v4; documents without them retain their prior v1–v3 encoding. Text is encoded as
    /// UTF-16 code units and floating point values as IEEE-754 bits.</summary>
    public static string ComputeLayoutFingerprint(ProjectDocument document) => ComputeLayoutFingerprint(document,CancellationToken.None);

    public static string ComputeLayoutFingerprint(ProjectDocument document,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(document);
        using var hash=SHA256.Create();
        using var stream=new CryptoStream(Stream.Null,hash,CryptoStreamMode.Write,leaveOpen:true);
        using var writer=new BinaryWriter(stream,Encoding.UTF8,leaveOpen:true);
        void Text(BinaryWriter writer,string? value)
        {
            writer.Write(value is not null);
            if(value is null)return;
            writer.Write(value.Length);
            for(var i=0;i<value.Length;i++) { if((i & 255)==0)cancellationToken.ThrowIfCancellationRequested();writer.Write((ushort)value[i]); }
        }
        // Never reinterpret existing v3 layout histories: a document without
        // grainlines has byte-for-byte the prior v1 fingerprint encoding.
        // Adding/removing a grainline changes the domain or data and therefore
        // makes a previous snapshot stale rather than silently excluding it.
        var includeGrainlines=document.Pieces.Any(piece=>piece.Grainline is not null);
        var includeNotches=document.Pieces.Any(piece=>piece.Notches is not null);
        var includeExperimental=document.Pieces.Any(piece=>piece.ExperimentalALine is not null);
        writer.Write(includeExperimental ? "TekstilCAD.nesting-layout.v4" : includeNotches ? "TekstilCAD.nesting-layout.v3" : includeGrainlines ? "TekstilCAD.nesting-layout.v2" : "TekstilCAD.nesting-layout.v1");
        if(includeExperimental) { writer.Write(includeGrainlines);writer.Write(includeNotches); }
        else if(includeNotches)writer.Write(includeGrainlines);
        writer.Write(document.Pieces.Count);
        foreach(var piece in document.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.Write(piece.Id.ToByteArray()); Text(writer,piece.Name); writer.Write(piece.Closed);
            writer.Write(piece.Points.Count);
            for(var i=0;i<piece.Points.Count;i++)
            { if((i & 255)==0)cancellationToken.ThrowIfCancellationRequested();var point=piece.Points[i];writer.Write(BitConverter.DoubleToInt64Bits(point.X));writer.Write(BitConverter.DoubleToInt64Bits(point.Y)); }
            writer.Write(piece.Metadata is not null);
            if(piece.Metadata is { } metadata)
            {
                Text(writer,metadata.Role); Text(writer,metadata.Size); Text(writer,metadata.Material);
                writer.Write(metadata.Quantity.HasValue);
                if(metadata.Quantity is { } quantity)writer.Write(quantity);
            }
            if(includeGrainlines)
            {
                writer.Write(piece.Grainline is not null);
                if(piece.Grainline is { } grainline)
                {
                    writer.Write(BitConverter.DoubleToInt64Bits(grainline.Start.X));
                    writer.Write(BitConverter.DoubleToInt64Bits(grainline.Start.Y));
                    writer.Write(BitConverter.DoubleToInt64Bits(grainline.End.X));
                    writer.Write(BitConverter.DoubleToInt64Bits(grainline.End.Y));
                    writer.Write(grainline.NeedsReview);
                }
            }
            if(includeNotches)
            {
                writer.Write(piece.Notches is not null);
                if(piece.Notches is { } marks)
                {
                    writer.Write(marks.Count);
                    foreach(var mark in marks)
                    {
                        cancellationToken.ThrowIfCancellationRequested();writer.Write(mark.VertexIndex);
                        writer.Write(BitConverter.DoubleToInt64Bits(mark.Position.X));writer.Write(BitConverter.DoubleToInt64Bits(mark.Position.Y));
                    }
                }
            }
            if(includeExperimental)
            {
                writer.Write(piece.ExperimentalALine is not null);
                if(piece.ExperimentalALine is { } experimental)
                { Text(writer,experimental.Algorithm);Text(writer,experimental.SourceSha256);Text(writer,experimental.Side); }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();writer.Flush(); stream.FlushFinalBlock();
        cancellationToken.ThrowIfCancellationRequested();return Convert.ToHexString(hash.Hash!);
    }
}

/// <summary>Historical information about one DXF import; not evidence of current geometry or production suitability.</summary>
public sealed class DxfImportProvenance
{
    public string SourceFileName { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public long SourceByteLength { get; set; }
    public double MillimetersPerUnit { get; set; }
    public List<string> Warnings { get; set; } = [];
    // Historical IDs may no longer exist in the document after edits.
    public List<Guid> PieceIds { get; set; } = [];

    public DxfImportProvenance Clone() => new() { SourceFileName=SourceFileName, SourceSha256=SourceSha256,
        SourceByteLength=SourceByteLength, MillimetersPerUnit=MillimetersPerUnit, Warnings=[.. Warnings], PieceIds=[.. PieceIds] };

    /// <summary>Validate bounded, untrusted historical data at a persistence or import boundary.</summary>
    public void Validate()
    {
        static bool ValidText(string? text, int maximum)
        {
            if (text is null || text.Length > maximum) return false;
            for (var i=0;i<text.Length;i++)
            {
                if (char.IsHighSurrogate(text[i])) { if (++i>=text.Length || !char.IsLowSurrogate(text[i])) return false; }
                else if (char.IsLowSurrogate(text[i])) return false;
            }
            return true;
        }
        if (!ValidText(SourceFileName,1024) || string.IsNullOrWhiteSpace(SourceFileName) ||
            SourceFileName is "." or ".." || SourceFileName.IndexOfAny(['/', '\\', ':', '\0'])>=0)
            throw new InvalidDataException("DXF kaynak dosya adı geçersiz; yalnız dosya adı saklanabilir.");
        if (SourceSha256 is null || SourceSha256.Length!=64 || SourceSha256.Any(c=>!((c>='0' && c<='9') || (c>='A' && c<='F') || (c>='a' && c<='f'))))
            throw new InvalidDataException("DXF kaynak özeti geçersiz.");
        if (SourceByteLength<0 || SourceByteLength>64L*1024*1024 || !double.IsFinite(MillimetersPerUnit) || MillimetersPerUnit<=0)
            throw new InvalidDataException("DXF kaynak boyutu veya ölçeği geçersiz.");
        if (Warnings is null || Warnings.Count>256 || Warnings.Any(w=>!ValidText(w,4096)) || Warnings.Sum(w=>(long)w.Length)>65536)
            throw new InvalidDataException("DXF uyarıları geçersiz veya izin verilen sınırı aşıyor.");
        if (PieceIds is null || PieceIds.Count>10000 || PieceIds.Any(id=>id==Guid.Empty) || PieceIds.Distinct().Count()!=PieceIds.Count)
            throw new InvalidDataException("DXF kaynak parça kimlikleri geçersiz.");
    }
}

/// <summary>Four ordered preview-pixel corners and their physical rectangle dimensions.</summary>
public sealed class PhotoPlaneCalibration
{
    public List<Point2> Corners { get; set; } = [];
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public PhotoPlaneCalibration Clone()=>new() { Corners=[..Corners],WidthMm=WidthMm,HeightMm=HeightMm };
}

/// <summary>Immutable-in-meaning historical photo trace evidence, not approval of current geometry.</summary>
public sealed class PhotoTraceProvenance
{
    public int AlgorithmVersion { get; set; }
    public Guid PieceId { get; set; }
    public string SourceSha256 { get; set; } = "";
    public long SourceByteLength { get; set; }
    public int SourceOrientation { get; set; }
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public int PreviewWidth { get; set; }
    public int PreviewHeight { get; set; }
    public Point2 ReferenceStart { get; set; }
    public Point2 ReferenceEnd { get; set; }
    public double ReferenceLengthMm { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PhotoPlaneCalibration? PlaneCalibration { get; set; }
    public List<Point2> TracePixels { get; set; } = [];
    public List<Point2> GeneratedPoints { get; set; } = [];
    public PhotoTraceProvenance Clone() => new() { AlgorithmVersion=AlgorithmVersion,PieceId=PieceId,SourceSha256=SourceSha256,SourceByteLength=SourceByteLength,SourceOrientation=SourceOrientation,
        SourceWidth=SourceWidth,SourceHeight=SourceHeight,PreviewWidth=PreviewWidth,PreviewHeight=PreviewHeight,ReferenceStart=ReferenceStart,ReferenceEnd=ReferenceEnd,
        ReferenceLengthMm=ReferenceLengthMm,PlaneCalibration=PlaneCalibration?.Clone(),TracePixels=[..TracePixels],GeneratedPoints=[..GeneratedPoints] };
    public void Validate()
    {
        if(AlgorithmVersion is not (1 or 2) || SourceOrientation is <1 or >8 || PieceId==Guid.Empty || SourceSha256 is null || SourceSha256.Length!=64 ||
           SourceSha256.Any(c=>!((c>='0' && c<='9') || (c>='A' && c<='F'))) || SourceByteLength<1 || SourceByteLength>8L*1024*1024 ||
           TracePixels is null || GeneratedPoints is null || TracePixels.Count is <2 or >512 || GeneratedPoints.Count!=TracePixels.Count)
            throw new InvalidDataException("Fotoğraf taslağı geçmişi eksik veya geçersiz.");
        try
        {
            IReadOnlyList<Point2> expected;
            if(AlgorithmVersion==1)
            {
                if(PlaneCalibration is not null)throw new InvalidDataException("Tek ölçekli fotoğraf kaydında düzlem bilgisi bulunamaz.");
                expected=PhotoTraceCalibration.ConvertVersion1(PreviewWidth,PreviewHeight,SourceWidth,SourceHeight,ReferenceStart,ReferenceEnd,ReferenceLengthMm,TracePixels);
            }
            else
            {
                var plane=PlaneCalibration;
                if(plane?.Corners is not { Count:4 } || SourceWidth<PreviewWidth || SourceHeight<PreviewHeight || SourceWidth>PhotoTraceCalibration.MaximumImageDimension ||
                   SourceHeight>PhotoTraceCalibration.MaximumImageDimension || (long)SourceWidth*SourceHeight>PhotoTraceCalibration.MaximumImagePixels ||
                   !SameBits(ReferenceStart,plane.Corners[0]) || !SameBits(ReferenceEnd,plane.Corners[1]) ||
                   BitConverter.DoubleToInt64Bits(ReferenceLengthMm)!=BitConverter.DoubleToInt64Bits(plane.WidthMm))
                    throw new InvalidDataException("Düzlem referansı veya kaynak boyutu geçersiz.");
                expected=PhotoPlaneRectification.ConvertVersion1(PreviewWidth,PreviewHeight,plane.Corners,plane.WidthMm,plane.HeightMm,TracePixels);
            }
            for(var i=0;i<expected.Count;i++)
                if(BitConverter.DoubleToInt64Bits(expected[i].X)!=BitConverter.DoubleToInt64Bits(GeneratedPoints[i].X) ||
                   BitConverter.DoubleToInt64Bits(expected[i].Y)!=BitConverter.DoubleToInt64Bits(GeneratedPoints[i].Y))
                    throw new InvalidDataException("Fotoğraf taslağı geçmişi hesaplanan geometriyle eşleşmiyor.");
        }
        catch(ArgumentException ex) { throw new InvalidDataException("Fotoğraf taslağı geçmişi geçersiz.",ex); }
    }
    private static bool SameBits(Point2 a,Point2 b)=>BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) && BitConverter.DoubleToInt64Bits(a.Y)==BitConverter.DoubleToInt64Bits(b.Y);
}

/// <summary>Unapproved A-line evidence embedded in the project, not production clearance.</summary>
public sealed record ALineProjectEvidence(string CandidateEvidenceJson,double[] TranslationMm,double[] FrontPlacedCoordinatesMm,double[] BackPlacedCoordinatesMm,string IntegritySha256)
{
    public ALineProjectEvidence Copy()=>this with { TranslationMm=(double[])TranslationMm.Clone(),FrontPlacedCoordinatesMm=(double[])FrontPlacedCoordinatesMm.Clone(),BackPlacedCoordinatesMm=(double[])BackPlacedCoordinatesMm.Clone() };
    public static bool ContentEquals(ALineProjectEvidence a,ALineProjectEvidence b)
    {
        static bool Bits(double[] x,double[] y)=>x.Length==y.Length && x.Zip(y).All(p=>BitConverter.DoubleToInt64Bits(p.First)==BitConverter.DoubleToInt64Bits(p.Second));
        return a.CandidateEvidenceJson==b.CandidateEvidenceJson && a.IntegritySha256==b.IntegritySha256 && Bits(a.TranslationMm,b.TranslationMm) && Bits(a.FrontPlacedCoordinatesMm,b.FrontPlacedCoordinatesMm) && Bits(a.BackPlacedCoordinatesMm,b.BackPlacedCoordinatesMm);
    }
}

public sealed class ProjectDocument
{
    public string Name { get; set; } = "Yeni proje";
    public List<PatternPiece> Pieces { get; set; } = [];

    public List<GradingRuleSet> GradingRules { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NestingSnapshot? NestingSnapshot { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DxfImportProvenance? ImportProvenance { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PhotoTraceProvenance>? PhotoTraceHistory { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ALineProjectEvidence>? ALineEvidence { get; set; }
    public ProjectDocument Clone() => new() { Name = Name, Pieces = Pieces.Select(p => p.Clone()).ToList(), GradingRules = GradingRules.Select(r => r.Clone()).ToList(), NestingSnapshot=NestingSnapshot?.Clone(), ImportProvenance=ImportProvenance?.Clone(),PhotoTraceHistory=PhotoTraceHistory?.Select(x=>x.Clone()).ToList(),ALineEvidence=ALineEvidence?.Select(x=>x.Copy()).ToList() };
}
