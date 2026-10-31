using System.Collections.Immutable;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Historical text evidence is separate from the saved project.</summary>
public sealed record DxfTextileAnnotationEvidence(DxfAnnotationReport Annotations, DxfOpaqueMetadata? OpaqueMetadata,
    DxfSupplementalGeometryReport? SupplementalGeometry, ImmutableArray<string> Warnings, string? SourceSha256, DxfImportParameters? ImportParameters)
{
    public DxfSourceGeometryReport? SourceGeometry { get; init; }
    public DxfSkippedSourceReport? SkippedSourceRecords { get; init; }
    public DxfSkippedSourceDetailReport? SkippedSourceDetails { get; init; }
    public DxfSourceGeometryReport? SourceObjects { get; init; }
}
/// <summary>Historical import evidence. It is separate from the project file and is not proof of textile semantics.</summary>
public sealed record DxfTextileImportEvidence(string SourceSha256, long SourceByteLength, DxfImportParameters ImportParameters,
    string PieceSourcesFingerprint, ImmutableArray<DxfPieceSource> PieceSources, DxfMetadataLossSummary? MetadataLoss,
    DxfMetadataSourceReport? MetadataSources, DxfOpaqueMetadata? OpaqueMetadata, DxfAnnotationReport? Annotations,
    DxfSupplementalGeometryReport? SupplementalGeometry, DxfSkippedSourceReport? SkippedSourceRecords, ImmutableArray<string> Warnings)
{
    public DxfSourceGeometryReport? SourceGeometry { get; init; }
    public DxfSkippedSourceDetailReport? SkippedSourceDetails { get; init; }
    public DxfSourceGeometryReport? SourceObjects { get; init; }
}
public sealed record DxfTextileMappingResult(ProjectDocument Project, ImmutableArray<DxfTextileTextAudit> TextAssignments,
    ImmutableArray<DxfGrainlineMarkerAudit> GrainlineAssignments, DxfTextileAnnotationEvidence? TextEvidence,
    DxfGrainlineSourceContext? GrainlineSourceContext)
{
    /// <summary>Original source context, or null if the project no longer matches the imported piece state.</summary>
    public DxfTextileImportEvidence? SourceEvidence { get; internal init; }
    public ImmutableArray<DxfTextileRoleAudit> RoleAssignments { get; internal init; } = ImmutableArray<DxfTextileRoleAudit>.Empty;
}

/// <summary>Applies independently reviewed text and grainline choices transactionally against one imported snapshot.</summary>
public static class DxfTextileMappingWorkflow
{
    public static DxfTextileMappingResult Apply(DxfImportResult import,
        IReadOnlyCollection<DxfTextileTextSelection> textSelections,
        IReadOnlyCollection<DxfGrainlineSelection> grainlineSelections,
        string expectedFingerprint, CancellationToken cancellationToken=default)
        => Apply(import,textSelections,grainlineSelections,Array.Empty<DxfTextileRoleSelection>(),expectedFingerprint,cancellationToken);

    public static DxfTextileMappingResult Apply(DxfImportResult import,
        IReadOnlyCollection<DxfTextileTextSelection> textSelections,
        IReadOnlyCollection<DxfGrainlineSelection> grainlineSelections,
        IReadOnlyCollection<DxfTextileRoleSelection> roleSelections,
        string expectedFingerprint, CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(textSelections);
        ArgumentNullException.ThrowIfNull(grainlineSelections);
        ArgumentNullException.ThrowIfNull(roleSelections);
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        cancellationToken.ThrowIfCancellationRequested();
        var text=DxfBoundedSelections.Copy(textSelections,ProjectStorage.MaximumPieces*4,cancellationToken);
        var grainlines=DxfBoundedSelections.Copy(grainlineSelections,ProjectStorage.MaximumPieces,cancellationToken);
        var roles=DxfBoundedSelections.Copy(roleSelections,ProjectStorage.MaximumPieces,cancellationToken);
        if(text.Length==0 && grainlines.Length==0 && roles.Length==0)
            throw new InvalidDataException("DXF tekstil seçimleri boş veya sınırı aşıyor.");

        if(!string.Equals(DxfTextileGrainlineMapping.CaptureFingerprint(import,cancellationToken),expectedFingerprint,StringComparison.Ordinal))
            throw new InvalidDataException("DXF inceleme kimliği değişti; yeniden inceleyin.");
        var sourceLayout=NestingSnapshot.ComputeLayoutFingerprint(import.Project,cancellationToken);
        DxfTextileImportEvidence? sourceEvidence=null;
        if(string.Equals(import.PieceSourcesFingerprint,sourceLayout,StringComparison.Ordinal) && import.PieceSources is { } sources)
            sourceEvidence=new(import.AnnotationSnapshotSha256!,import.AnnotationSnapshotByteLength!.Value,import.ImportParameters!,
                sourceLayout,sources,import.MetadataLoss,import.MetadataSources,import.OpaqueMetadata,import.Annotations,
                import.SupplementalGeometry,import.SkippedSourceRecords,import.CapturedWarnings) { SourceGeometry=import.SourceGeometry, SkippedSourceDetails=import.SkippedSourceDetails, SourceObjects=import.SourceObjects };

        // Each mapper validates the same original and changes only its own
        // clone. Neither partial result is returned if the other choice fails.
        var textResult=text.Length==0 ? null : DxfTextileAnnotationMapping.Apply(import,text,expectedFingerprint,cancellationToken);
        var grainResult=grainlines.Length==0 ? null : DxfTextileGrainlineMapping.Apply(import,grainlines,expectedFingerprint,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var project=grainResult?.Project ?? textResult?.Project ?? import.Project.Clone();
        if(grainResult is null && textResult is null &&
           !string.Equals(DxfTextileGrainlineMapping.CaptureFingerprint(import with { Project=project },cancellationToken),expectedFingerprint,StringComparison.Ordinal))
            throw new InvalidDataException("DXF içeriği kopyalanırken değişti; rol eşlenmedi.");
        if(project.GradingRules.Count!=0 || project.NestingSnapshot is not null)
            throw new InvalidDataException("Serileme veya pastal ilişkileri olan projede DXF tekstil eşlemesi yapılamaz.");
        if(textResult is not null && grainResult is not null)
        {
            var destination=project.Pieces.ToDictionary(p=>p.Id);
            var textPieces=textResult.Project.Pieces.ToDictionary(p=>p.Id);
            foreach(var assignments in textResult.Assignments.GroupBy(a=>a.PieceId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(!destination.TryGetValue(assignments.Key,out var target) || !textPieces.TryGetValue(assignments.Key,out var source))
                    throw new InvalidDataException("DXF tekstil eşleme sonuçları birleştirilemedi.");
                if(assignments.Any(a=>a.Field==DxfTextileTextField.PieceName))target.Name=source.Name;
                if(assignments.Any(a=>a.Field!=DxfTextileTextField.PieceName))
                {
                    if(source.Metadata is null)throw new InvalidDataException("DXF tekstil meta verisi eksik.");
                    target.Metadata=source.Metadata.Clone();
                }
            }
        }
        var roleAudit=DxfTextileRoleMapping.ApplyToCopy(project,roles,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectStorage.Validate(project);
        cancellationToken.ThrowIfCancellationRequested();
        var textEvidence=textResult is null ? null : new DxfTextileAnnotationEvidence(textResult.SourceAnnotations,
            textResult.SourceOpaqueMetadata,textResult.SupplementalGeometry,textResult.SourceWarnings,textResult.SourceSha256,textResult.ImportParameters)
            { SourceGeometry=textResult.SourceGeometry, SkippedSourceRecords=textResult.SkippedSourceRecords, SkippedSourceDetails=textResult.SkippedSourceDetails, SourceObjects=textResult.SourceObjects };
        return new DxfTextileMappingResult(project,textResult?.Assignments ?? ImmutableArray<DxfTextileTextAudit>.Empty,
            grainResult?.ConsumedMarkers ?? ImmutableArray<DxfGrainlineMarkerAudit>.Empty,textEvidence,grainResult?.SourceContext)
            { SourceEvidence=sourceEvidence, RoleAssignments=roleAudit };
    }
}
