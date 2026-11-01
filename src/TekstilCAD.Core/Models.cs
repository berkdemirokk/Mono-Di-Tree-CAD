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
    public PatternPiece Clone() => new() { Id = Id, Name = Name, Points = [.. Points], Closed = Closed, Metadata = Metadata?.Clone(), Grainline=Grainline?.Clone() };
}

public enum OrthogonalGradingTransform { RotateLeft, RotateRight, MirrorVertical, MirrorHorizontal }

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

    public NestingSnapshot Clone() => new() { FabricWidth=FabricWidth, UsedLength=UsedLength, Gap=Gap,
        EdgeMargin=EdgeMargin, Algorithm=Algorithm, LayoutFingerprint=LayoutFingerprint, Placements=[.. Placements] };

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
    /// without grainlines retain the exact legacy v1 encoding. Text is encoded as
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
        writer.Write(includeGrainlines ? "TekstilCAD.nesting-layout.v2" : "TekstilCAD.nesting-layout.v1");
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

public sealed class ProjectDocument
{
    public string Name { get; set; } = "Yeni proje";
    public List<PatternPiece> Pieces { get; set; } = [];

    public List<GradingRuleSet> GradingRules { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NestingSnapshot? NestingSnapshot { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DxfImportProvenance? ImportProvenance { get; set; }

    public ProjectDocument Clone() => new() { Name = Name, Pieces = Pieces.Select(p => p.Clone()).ToList(), GradingRules = GradingRules.Select(r => r.Clone()).ToList(), NestingSnapshot=NestingSnapshot?.Clone(), ImportProvenance=ImportProvenance?.Clone() };
}
