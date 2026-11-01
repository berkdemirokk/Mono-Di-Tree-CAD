namespace TekstilCAD.Core;

/// <summary>One explicit historical copy angle, not an authorization to cut.</summary>
public sealed record RotatedNestingCopy(Guid SourcePieceId,int CopyNumber,Guid PlacedPieceId,int Degrees);

/// <summary>
/// Proposed detached data model for a versioned orthogonal nesting trace.
/// This type is not currently attached to ProjectDocument or accepted by ProjectStorage;
/// it does not establish geometric validity or authorize saving a rotated layout.
/// </summary>
public sealed class RotatedNestingTrace
{
    public const string CurrentSchema="per-copy-orthogonal-trace-v1";
    public const string CurrentAlgorithm="explicit-per-copy-shelf-v1";
    public const string CurrentTransform="origin-quarter-turn-then-translate-v1";
    public string Schema { get; set; }="";
    public string Algorithm { get; set; }="";
    public string TransformConvention { get; set; }="";
    public string SourceFingerprint { get; set; }="";
    public string LayoutFingerprint { get; set; }="";
    public List<PatternPiece> SourcePieces { get; set; }=[];
    public List<Guid> Selections { get; set; }=[];
    public List<RotatedNestingCopy> Copies { get; set; }=[];
    public double FabricWidth { get; set; }
    public double Gap { get; set; }
    public double EdgeMargin { get; set; }
    public double UsedLength { get; set; }
    /// <summary>Null denotes an explicitly unlimited length, not an omitted JSON field.</summary>
    public double? MaxLength { get; set; }
    public string GrainDirection { get; set; }="";
    public double ToleranceDegrees { get; set; }

    private void ValidateStructure()
    {
        if(SourcePieces is null || Selections is null || Copies is null || SourcePieces.Count>10_000 || Selections.Count>10_000 || Copies.Count>10_000 ||
           SourcePieces.Any(p=>p is null || p.Points is null || p.Points.Count>10_000 ||
           (p.Notches is not null && (p.Notches.Count>p.Points.Count || p.Notches.Any(mark=>mark is null)))) ||
           SourcePieces.Sum(p=>(long)p.Points.Count)>200_000 || Copies.Any(copy=>copy is null))
            throw new ArgumentException("Döndürme izi veri yapısı eksik veya geçersiz.");
    }

    public RotatedNestingTrace Clone()
    {
        ValidateStructure();
        return new() {
        Schema=Schema,Algorithm=Algorithm,TransformConvention=TransformConvention,
        SourceFingerprint=SourceFingerprint,LayoutFingerprint=LayoutFingerprint,
        SourcePieces=SourcePieces.Select(p=>p.Clone()).ToList(),Selections=[..Selections],Copies=[..Copies],
        FabricWidth=FabricWidth,Gap=Gap,EdgeMargin=EdgeMargin,UsedLength=UsedLength,
        MaxLength=MaxLength,GrainDirection=GrainDirection,ToleranceDegrees=ToleranceDegrees };
    }

    /// <summary>Compares all stored fields and coordinate bits; does not validate them.</summary>
    public static bool ContentEquals(RotatedNestingTrace a,RotatedNestingTrace b)
    {
        ArgumentNullException.ThrowIfNull(a);ArgumentNullException.ThrowIfNull(b);
        a.ValidateStructure();b.ValidateStructure();
        static bool Same(double x,double y)=>BitConverter.DoubleToInt64Bits(x)==BitConverter.DoubleToInt64Bits(y);
        return a.Schema==b.Schema && a.Algorithm==b.Algorithm && a.TransformConvention==b.TransformConvention &&
            a.SourceFingerprint==b.SourceFingerprint && a.LayoutFingerprint==b.LayoutFingerprint &&
            a.GrainDirection==b.GrainDirection && Same(a.FabricWidth,b.FabricWidth) && Same(a.Gap,b.Gap) &&
            Same(a.EdgeMargin,b.EdgeMargin) && Same(a.UsedLength,b.UsedLength) && Same(a.ToleranceDegrees,b.ToleranceDegrees) &&
            (a.MaxLength is null ? b.MaxLength is null : b.MaxLength is not null && Same(a.MaxLength.Value,b.MaxLength.Value)) &&
            a.Selections.SequenceEqual(b.Selections) && a.Copies.SequenceEqual(b.Copies) &&
            DocumentSession.ContentEquals(new ProjectDocument { Pieces=a.SourcePieces },new ProjectDocument { Pieces=b.SourcePieces });
    }
}
