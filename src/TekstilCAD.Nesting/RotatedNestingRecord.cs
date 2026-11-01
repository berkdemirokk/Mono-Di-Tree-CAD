using System.Collections.ObjectModel;
using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>Detached in-memory evidence for one explicitly angled calculation.
/// This type is not a file format, signature, production approval or save authorization.</summary>
public sealed class RotatedNestingRecord
{
    private readonly ProjectDocument source;
    private readonly Guid[] selected;
    private readonly Dictionary<(Guid,int),int> angles;
    private readonly PerCopyNestingResult result;
    private readonly GrainDirectionPolicy.Direction direction;
    private readonly double tolerance,width,gap,margin,maxLength;

    private RotatedNestingRecord(ProjectDocument source,Guid[] selected,Dictionary<(Guid,int),int> angles,
        PerCopyNestingResult result,GrainDirectionPolicy.Direction direction,double tolerance,double width,double gap,double margin,double maxLength)
    { this.source=source;this.selected=selected;this.angles=angles;this.result=result;this.direction=direction;this.tolerance=tolerance;this.width=width;this.gap=gap;this.margin=margin;this.maxLength=maxLength; }

    /// <summary>The caller must prevent concurrent mutation until capture returns. Only selected
    /// source pieces are retained; source project rules and history are not part of this evidence.</summary>
    public static RotatedNestingRecord Capture(ProjectDocument source,IReadOnlyCollection<Guid> selected,
        IReadOnlyDictionary<(Guid SourcePieceId,int CopyNumber),int> angles,PerCopyNestingResult result,
        GrainDirectionPolicy.Direction direction,double tolerance,double width,double gap,double margin,double maxLength,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(angles);ArgumentNullException.ThrowIfNull(result);
        if(selected.Count is <1 or >NestingEngine.MaximumPieces || angles.Count>NestingEngine.MaximumPieces ||
           source.Pieces is null || source.Pieces.Count>NestingEngine.MaximumTotalVertices ||
           result.Plan?.Nesting?.Layout?.Pieces is null || result.Plan.Nesting.Layout.Pieces.Count>NestingEngine.MaximumPieces ||
           result.Plan.Nesting.Layout.Name is null || result.Plan.Nesting.Layout.Name.Length>1024 ||
           result.Plan.Nesting.Layout.GradingRules is null || result.Plan.Nesting.Layout.GradingRules.Count!=0 ||
           result.Plan.Nesting.Layout.NestingSnapshot is not null || result.Plan.Nesting.Layout.ImportProvenance is not null ||
           result.Plan.Placements is null || result.Plan.Placements.Count>NestingEngine.MaximumPieces ||
           result.PlacedRotations is null || result.PlacedRotations.Count>NestingEngine.MaximumPieces)
            throw new ArgumentException("Döndürme izi kaynak sınırını aşıyor.");
        var selectedIds=selected.ToArray();var ids=selectedIds.ToHashSet();
        if(ids.Count!=selectedIds.Length || ids.Contains(Guid.Empty))throw new ArgumentException("Kaynak seçimi geçersiz.");
        var snapshots=new List<PatternPiece>();long vertices=0;
        foreach(var piece in source.Pieces)
        {
            token.ThrowIfCancellationRequested();
            if(piece is null || !ids.Contains(piece.Id))continue;
            CheckPiece(piece,ref vertices);snapshots.Add(piece.Clone());
        }
        if(snapshots.Count!=ids.Count || snapshots.Select(p=>p.Id).Distinct().Count()!=ids.Count)
            throw new ArgumentException("Kaynak seçimi eksik veya yinelenmiş.");
        var sourceCopy=new ProjectDocument { Pieces=snapshots };
        var angleCopy=new Dictionary<(Guid,int),int>();
        foreach(var pair in angles) { token.ThrowIfCancellationRequested();if(!angleCopy.TryAdd(pair.Key,pair.Value))throw new ArgumentException("Yinelenen açı."); }
        var resultCopy=CopyResult(result,token,vertices);
        var captured=new RotatedNestingRecord(sourceCopy,selectedIds,angleCopy,resultCopy,direction,tolerance,width,gap,margin,maxLength);
        if(!captured.Verify(token))throw new InvalidOperationException("Döndürme izi doğrulanamadı; kayıt adayı oluşturulmadı.");
        return captured;
    }

    public bool Verify(CancellationToken token=default) => PerCopyTraceVerifier.IsValidForConstraints(source,selected,result,angles,
        direction,tolerance,width,gap,margin,maxLength,token);

    /// <summary>Returns a detached copy; editing it cannot modify the captured evidence.</summary>
    public ProjectDocument CopySource() => source.Clone();
    /// <summary>Returns a detached copy; editing it cannot modify the captured evidence.</summary>
    public PerCopyNestingResult CopyResult() => CopyResult(result,CancellationToken.None);
    public IReadOnlyList<Guid> CopySelection() => Array.AsReadOnly((Guid[])selected.Clone());
    public IReadOnlyDictionary<(Guid SourcePieceId,int CopyNumber),int> CopyAngles() =>
        new ReadOnlyDictionary<(Guid,int),int>(new Dictionary<(Guid,int),int>(angles));
    public GrainDirectionPolicy.Direction Direction => direction;
    public double ToleranceDegrees => tolerance;
    public double FabricWidth => width;
    public double Gap => gap;
    public double EdgeMargin => margin;
    public double MaxLength => maxLength;

    private static void CheckPiece(PatternPiece piece,ref long vertices)
    {
        if(piece.Points is null || piece.Points.Count>NestingEngine.MaximumVerticesPerPiece ||
           (vertices+=piece.Points.Count)>NestingEngine.MaximumTotalVertices || piece.Name is null || piece.Name.Length>1024 ||
           piece.Notches is { } marks && (marks.Count==0 || marks.Count>piece.Points.Count) ||
           piece.Metadata?.Material is { Length:>128 } || piece.Metadata?.Size is { Length:>128 } || piece.Metadata?.Role is { Length:>128 })
            throw new ArgumentException("Döndürme izi parça sınırını aşıyor.");
    }

    private static PerCopyNestingResult CopyResult(PerCopyNestingResult original,CancellationToken token,long vertices=0)
    {
        var pieces=new List<PatternPiece>(original.Plan.Nesting.Layout.Pieces.Count);
        foreach(var piece in original.Plan.Nesting.Layout.Pieces)
        { token.ThrowIfCancellationRequested();if(piece is null)throw new ArgumentException("Yerleşen parça eksik.");CheckPiece(piece,ref vertices);pieces.Add(piece.Clone()); }
        token.ThrowIfCancellationRequested();
        var layout=new ProjectDocument { Name=original.Plan.Nesting.Layout.Name,Pieces=pieces };
        var nesting=original.Plan.Nesting with { Layout=layout };
        var mappings=original.Plan.Placements.ToArray();
        var rotations=new Dictionary<Guid,int>();
        foreach(var pair in original.PlacedRotations) { token.ThrowIfCancellationRequested();rotations.Add(pair.Key,pair.Value); }
        return new PerCopyNestingResult(new ProductionNestingResult(nesting,Array.AsReadOnly(mappings)),new ReadOnlyDictionary<Guid,int>(rotations))
            { SourceFingerprint=original.SourceFingerprint,LayoutFingerprint=original.LayoutFingerprint };
    }
}
