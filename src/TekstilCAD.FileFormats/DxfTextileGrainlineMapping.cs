using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>An explicit association between two pieces in one imported snapshot.</summary>
public sealed record DxfGrainlineSelection(Guid ContourId, Guid MarkerId);
/// <summary>The consumed marker's import identity and geometry; this is not a verified DXF entity handle.</summary>
public sealed record DxfGrainlineMarkerAudit(Guid ContourId, Guid MarkerId, string MarkerName, Point2 Start, Point2 End, DxfPieceSource? Source);
/// <summary>Historical import context. Opaque data is shared once for all markers, may be truncated, and is not saved in the project.</summary>
public sealed record DxfGrainlineSourceContext(string? SourceSha256, long? SourceByteLength,
    DxfImportParameters? ImportParameters,
    DxfMetadataLossSummary? MetadataLoss, DxfMetadataSourceReport? MetadataSources, DxfOpaqueMetadata? OpaqueMetadata,
    DxfAnnotationReport? Annotations, DxfSupplementalGeometryReport? SupplementalGeometry, DxfSkippedSourceReport? SkippedSourceRecords, ImmutableArray<string> Warnings)
{
    public DxfSourceGeometryReport? SourceGeometry { get; init; }
    public DxfSkippedSourceDetailReport? SkippedSourceDetails { get; init; }
    public DxfSourceGeometryReport? SourceObjects { get; init; }
}
public sealed record DxfGrainlineMappingResult(ProjectDocument Project, ImmutableArray<DxfGrainlineMarkerAudit> ConsumedMarkers,
    DxfGrainlineSourceContext? SourceContext);

/// <summary>
/// Applies user-selected grainline associations to a copy of an imported DXF.
/// No layer name, geometric containment or AAMA/ASTM semantics are inferred.
/// The marker is consumed from the copy; the supplied import remains intact.
/// </summary>
public static class DxfTextileGrainlineMapping
{
    /// <summary>Capture before selecting IDs. Binds the current piece state, source bytes identity and import choices.</summary>
    public static string CaptureFingerprint(DxfImportResult import, CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(import);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectStorage.Validate(import.Project);
        if(import.AnnotationSnapshotSha256 is not { Length:64 } sourceHash || !sourceHash.All(Uri.IsHexDigit) ||
           import.AnnotationSnapshotByteLength is not (>=0 and <=DxfSourceSnapshotCheck.MaximumBytes) ||
           !string.Equals(sourceHash,import.SourceSha256,StringComparison.OrdinalIgnoreCase) || import.SourceByteLength!=import.AnnotationSnapshotByteLength ||
           import.ImportParameters is null || import.CapturedWarnings.IsDefault ||
           import.CapturedSkippedRelevantInsertEntities is null || import.CapturedImportedBlockDefinitions is null ||
           import.SkippedRelevantInsertEntities!=import.CapturedSkippedRelevantInsertEntities || import.ImportedBlockDefinitions!=import.CapturedImportedBlockDefinitions)
            throw new InvalidDataException("DXF kaynak kimliği veya aktarım durumu eksik ya da değiştirilmiş.");
        if(import.CapturedSkippedRelevantInsertEntities is not false || import.CapturedImportedBlockDefinitions is not false)
            throw new InvalidDataException("DXF blok yerleşimleri veya tanım kapsamı doğrulanmadı; iplik yönü eşlenmedi.");
        var parameters=import.ImportParameters;
        if(parameters.BulgeToleranceMm is double bulgeTolerance && (!double.IsFinite(bulgeTolerance) || bulgeTolerance<=0))
            throw new InvalidDataException("DXF yay örnekleme toleransı geçersiz; inceleme yeniden yapılmalıdır.");
        var layout=NestingSnapshot.ComputeLayoutFingerprint(import.Project,cancellationToken);
        using var sha=SHA256.Create();
        using var hashStream=new CryptoStream(Stream.Null,sha,CryptoStreamMode.Write,leaveOpen:true);
        using var writer=new BinaryWriter(hashStream,Encoding.UTF8,leaveOpen:true);
        writer.Write("TekstilCAD.dxf-textile-review.v3");writer.Write(sourceHash.ToUpperInvariant());
        writer.Write(import.AnnotationSnapshotByteLength.Value);writer.Write(layout);
        writer.Write((int)parameters.Encoding);writer.Write(BitConverter.DoubleToInt64Bits(parameters.MillimetersPerUnit));
        writer.Write((int)parameters.Mode);writer.Write(parameters.ComponentToleranceMm.HasValue);
        if(parameters.ComponentToleranceMm is double tolerance)writer.Write(BitConverter.DoubleToInt64Bits(tolerance));
        writer.Write(parameters.BulgeToleranceMm.HasValue);
        if(parameters.BulgeToleranceMm is double bulge)writer.Write(BitConverter.DoubleToInt64Bits(bulge));
        writer.Write(parameters.ApprovedLayers.Length);
        foreach(var layer in parameters.ApprovedLayers.OrderBy(name=>name,StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // BinaryWriter's UTF-8 replacement fallback can collapse distinct
            // unpaired UTF-16 units in caller-provided approval names. Encode
            // exact code units, as the layout fingerprint does.
            writer.Write(layer.Length);
            for(var i=0;i<layer.Length;i++)
            { if((i&255)==0)cancellationToken.ThrowIfCancellationRequested();writer.Write((ushort)layer[i]); }
        }
        writer.Flush();hashStream.FlushFinalBlock();cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(sha.Hash!);
    }

    public static DxfGrainlineMappingResult Apply(DxfImportResult import, IReadOnlyCollection<DxfGrainlineSelection> selections,
        string expectedFingerprint,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        cancellationToken.ThrowIfCancellationRequested();
        if(!string.Equals(CaptureFingerprint(import,cancellationToken),expectedFingerprint,StringComparison.Ordinal))
            throw new InvalidDataException("DXF geometrisi seçimden sonra değişti; yeniden inceleyin.");
        // Removing a marker could invalidate rules or a historical layout.
        // This operation is restricted to documents without those links.
        if(import.Project.GradingRules.Count!=0 || import.Project.NestingSnapshot is not null)
            throw new InvalidDataException("Serileme veya pastal ilişkileri olan projede DXF işareti taşınamaz.");
        var selected=DxfBoundedSelections.Copy(selections,import.Project.Pieces.Count,cancellationToken);
        var byId=import.Project.Pieces.ToDictionary(p=>p.Id);
        var contours=new HashSet<Guid>();
        var markers=new HashSet<Guid>();
        foreach(var selection in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(selection is null || selection.ContourId==selection.MarkerId ||
               !contours.Add(selection.ContourId) || !markers.Add(selection.MarkerId) ||
               !byId.TryGetValue(selection.ContourId,out var contour) || !byId.TryGetValue(selection.MarkerId,out var marker))
                throw new InvalidDataException("DXF iplik yönü seçimi eksik, yinelenmiş veya geçersiz.");
            if(!contour.Closed || contour.Points.Count<3 || contour.Grainline is not null)
                throw new InvalidDataException("İplik yönü hedefi kapalı kontur olmalı ve mevcut iplik yönü içermemelidir.");
            if(marker.Notches is not null)
                throw new InvalidDataException("Çentik işaretli parça iplik yönüne dönüştürülemez; işaretler korunmalıdır.");
            if(marker.Closed || marker.Points.Count!=2 || marker.Points[0]==marker.Points[1] || marker.Grainline is not null || marker.Metadata is not null)
                throw new InvalidDataException("İplik yönü işareti meta verisiz, iki farklı uçlu açık çizgi olmalıdır.");
        }
        if(contours.Overlaps(markers))throw new InvalidDataException("Bir DXF parçası hem kontur hem işaret olarak seçilemez.");
        cancellationToken.ThrowIfCancellationRequested();
        var copy=import.Project.Clone();
        ProjectStorage.Validate(copy);
        if(copy.GradingRules.Count!=0 || copy.NestingSnapshot is not null)
            throw new InvalidDataException("Serileme veya pastal ilişkileri olan projede DXF işareti taşınamaz.");
        if(!string.Equals(CaptureFingerprint(import with { Project=copy },cancellationToken),expectedFingerprint,StringComparison.Ordinal))
            throw new InvalidDataException("DXF geometrisi kopyalanırken değişti; yeniden inceleyin.");
        var copies=copy.Pieces.ToDictionary(p=>p.Id);
        // Piece sources describe the imported geometry, not later edits that
        // happen to keep the same piece IDs. Keep them only for that state.
        var sameSourceState=string.Equals(import.PieceSourcesFingerprint,NestingSnapshot.ComputeLayoutFingerprint(copy,cancellationToken),StringComparison.Ordinal);
        var pieceSources=sameSourceState ? import.PieceSources : null;
        var sources=pieceSources.HasValue ? pieceSources.Value.ToDictionary(source=>source.PieceId) : null;
        var audit=ImmutableArray.CreateBuilder<DxfGrainlineMarkerAudit>(selected.Length);
        foreach(var selection in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var marker=copies[selection.MarkerId];
            if(marker.Notches is not null)
                throw new InvalidDataException("Çentik işaretli parça iplik yönüne dönüştürülemez; işaretler korunmalıdır.");
            var source=sources is not null && sources.TryGetValue(marker.Id,out var found) ? found : null;
            audit.Add(new(selection.ContourId,marker.Id,marker.Name,marker.Points[0],marker.Points[1],source));
            copies[selection.ContourId].Grainline=new Grainline { Start=marker.Points[0],End=marker.Points[1],NeedsReview=true };
        }
        copy.Pieces.RemoveAll(p=>markers.Contains(p.Id));
        cancellationToken.ThrowIfCancellationRequested();
        ProjectStorage.Validate(copy);
        cancellationToken.ThrowIfCancellationRequested();
        // Keep source context once, rather than duplicating bounded opaque
        // metadata per marker or implying it was persisted with the project.
        var context=sameSourceState ? new DxfGrainlineSourceContext(import.SourceSha256,import.SourceByteLength,import.ImportParameters,
            import.MetadataLoss,import.MetadataSources,import.OpaqueMetadata,import.Annotations,import.SupplementalGeometry,import.SkippedSourceRecords,import.CapturedWarnings)
            { SourceGeometry=import.SourceGeometry, SkippedSourceDetails=import.SkippedSourceDetails, SourceObjects=import.SourceObjects } : null;
        return new(copy,audit.MoveToImmutable(),context);
    }
}
