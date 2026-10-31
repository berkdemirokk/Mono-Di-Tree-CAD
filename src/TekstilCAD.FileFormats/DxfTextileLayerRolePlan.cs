using System.Collections.Immutable;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Explicit role proposal for one exact source-layer spelling.</summary>
public sealed record DxfLayerRoleSelection(string Layer, DxfTextileRole Role);
public enum DxfLayerRoleEligibility { Eligible, OpenCuttingContour, ExistingRole }
/// <summary>A source piece to review individually; it is not a production or cutting approval.</summary>
public sealed record DxfLayerRoleCandidate(DxfTextileRoleSelection Selection, DxfPieceSource Source,
    bool Closed, DxfLayerRoleEligibility Eligibility);
/// <summary>A bounded review plan. Skipped source records and warnings remain relevant even when all candidates are eligible.</summary>
public sealed record DxfLayerRolePlan(string ReviewFingerprint, string SourceSha256, DxfImportParameters ImportParameters,
    ImmutableArray<DxfLayerRoleCandidate> Candidates, DxfSkippedSourceReport? SkippedSourceRecords, ImmutableArray<string> Warnings)
{
    /// <summary>Bounded, uninterpreted source inventories; null or truncation is not evidence that other data is absent.</summary>
    public DxfMetadataLossSummary? MetadataLoss { get; init; }
    public DxfMetadataSourceReport? MetadataSources { get; init; }
    public DxfOpaqueMetadata? OpaqueMetadata { get; init; }
    public DxfAnnotationReport? Annotations { get; init; }
    public DxfSupplementalGeometryReport? SupplementalGeometry { get; init; }
    public DxfSourceGeometryReport? SourceGeometry { get; init; }
    public DxfSkippedSourceDetailReport? SkippedSourceDetails { get; init; }
    public DxfSourceGeometryReport? SourceObjects { get; init; }
}

/// <summary>Expands explicit layer proposals into reviewable piece selections without changing the project.</summary>
public static class DxfTextileLayerRolePlan
{
    public static DxfLayerRolePlan Create(DxfImportResult import,IReadOnlyCollection<DxfLayerRoleSelection> selections,
        string expectedFingerprint,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        cancellationToken.ThrowIfCancellationRequested();
        if(!string.Equals(DxfTextileGrainlineMapping.CaptureFingerprint(import,cancellationToken),expectedFingerprint,StringComparison.Ordinal))
            throw new InvalidDataException("DXF inceleme kimliği değişti; katman planı oluşturulmadı.");
        if(!string.Equals(import.PieceSourcesFingerprint,NestingSnapshot.ComputeLayoutFingerprint(import.Project,cancellationToken),StringComparison.Ordinal) ||
           import.PieceSources is not { } sourceRecords || sourceRecords.IsDefault || sourceRecords.Length!=import.Project.Pieces.Count)
            throw new InvalidDataException("DXF parça kaynakları eksik veya güncel değil.");
        var requested=DxfBoundedSelections.Copy(selections,ProjectStorage.MaximumPieces,cancellationToken);
        if(requested.Length==0)throw new InvalidDataException("DXF katman seçimi boş.");
        var requestedByLayer=new Dictionary<string,DxfTextileRole>(StringComparer.Ordinal);
        foreach(var selection in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(selection is null || string.IsNullOrWhiteSpace(selection.Layer) ||
               selection.Role is not (DxfTextileRole.Cutting or DxfTextileRole.Auxiliary) ||
               !requestedByLayer.TryAdd(selection.Layer,selection.Role))
                throw new InvalidDataException("DXF katman rol seçimi eksik, yinelenmiş veya geçersiz.");
        }
        var pieces=import.Project.Pieces.ToDictionary(piece=>piece.Id);
        var seenPieces=new HashSet<Guid>();
        var seenLayers=new HashSet<string>(StringComparer.Ordinal);
        var candidates=ImmutableArray.CreateBuilder<DxfLayerRoleCandidate>();
        foreach(var source in sourceRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(source is null || string.IsNullOrEmpty(source.Layer) || !seenPieces.Add(source.PieceId) || !pieces.TryGetValue(source.PieceId,out var piece))
                throw new InvalidDataException("DXF parça kaynak eşlemesi geçersiz.");
            if(!requestedByLayer.TryGetValue(source.Layer,out var role))continue;
            seenLayers.Add(source.Layer);
            var eligibility=piece.Metadata?.Role is not null ? DxfLayerRoleEligibility.ExistingRole
                : role==DxfTextileRole.Cutting && !piece.Closed ? DxfLayerRoleEligibility.OpenCuttingContour
                : DxfLayerRoleEligibility.Eligible;
            candidates.Add(new(new(piece.Id,role),source,piece.Closed,eligibility));
        }
        if(seenPieces.Count!=pieces.Count || seenLayers.Count!=requestedByLayer.Count)
            throw new InvalidDataException("DXF katmanı veya parça kaynağı bulunamadı; plan oluşturulmadı.");
        // A second check prevents a plan from being returned after normal
        // concurrent changes to the mutable project during enumeration.
        if(!string.Equals(DxfTextileGrainlineMapping.CaptureFingerprint(import,cancellationToken),expectedFingerprint,StringComparison.Ordinal))
            throw new InvalidDataException("DXF proje içeriği planlama sırasında değişti.");
        return new(expectedFingerprint,import.AnnotationSnapshotSha256!,import.ImportParameters!,candidates.ToImmutable(),
            import.SkippedSourceRecords,import.CapturedWarnings)
        {
            MetadataLoss=import.MetadataLoss, MetadataSources=import.MetadataSources, OpaqueMetadata=import.OpaqueMetadata,
            Annotations=import.Annotations, SupplementalGeometry=import.SupplementalGeometry, SourceGeometry=import.SourceGeometry, SkippedSourceDetails=import.SkippedSourceDetails, SourceObjects=import.SourceObjects
        };
    }
}
