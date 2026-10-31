using System.Collections.Immutable;
using System.Globalization;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

public enum DxfTextileTextField { Size, Material, Quantity, PieceName }
/// <summary>A human-selected source TEXT and target field; this is not automatic textile classification.</summary>
public sealed record DxfTextileTextSelection(Guid PieceId, int SourceOrdinal, DxfTextileTextField Field);
/// <summary>Decoded value and its original TEXT group-1 representation.</summary>
public sealed record DxfTextileTextAudit(Guid PieceId, int SourceOrdinal, DxfTextileTextField Field, string Value, string RawValue)
{
    /// <summary>Prior imported display name when explicitly replacing it; null for other fields.</summary>
    public string? PreviousValue { get; init; }
    /// <summary>Historical target piece source, if captured; this does not establish that the text describes that piece.</summary>
    public DxfPieceSource? TargetSource { get; init; }
}
/// <summary>The source report is historical and remains separate from project persistence.</summary>
public sealed record DxfTextileAnnotationMappingResult(ProjectDocument Project, ImmutableArray<DxfTextileTextAudit> Assignments,
    DxfAnnotationReport SourceAnnotations, DxfOpaqueMetadata? SourceOpaqueMetadata, DxfSupplementalGeometryReport? SupplementalGeometry, ImmutableArray<string> SourceWarnings, string? SourceSha256, DxfImportParameters? ImportParameters)
{
    /// <summary>Bounded historical source records; null or truncation does not imply absent source data.</summary>
    public DxfSourceGeometryReport? SourceGeometry { get; init; }
    public DxfSkippedSourceReport? SkippedSourceRecords { get; init; }
    public DxfSkippedSourceDetailReport? SkippedSourceDetails { get; init; }
    public DxfSourceGeometryReport? SourceObjects { get; init; }
}

/// <summary>Copies explicitly selected model-space TEXT values to textile fields or explicitly replaces an imported display name. No garment or layer semantics are inferred.</summary>
public static class DxfTextileAnnotationMapping
{
    public static DxfTextileAnnotationMappingResult Apply(DxfImportResult import, IReadOnlyCollection<DxfTextileTextSelection> selections,
        string expectedFingerprint, CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        cancellationToken.ThrowIfCancellationRequested();
        var current=DxfTextileGrainlineMapping.CaptureFingerprint(import,cancellationToken);
        if(!string.Equals(current,expectedFingerprint,StringComparison.Ordinal) ||
           !string.Equals(import.PieceSourcesFingerprint,NestingSnapshot.ComputeLayoutFingerprint(import.Project,cancellationToken),StringComparison.Ordinal) ||
           import.AnnotationSnapshotSha256 is null || import.AnnotationSnapshotByteLength is null ||
           !string.Equals(import.SourceSha256,import.AnnotationSnapshotSha256,StringComparison.OrdinalIgnoreCase) ||
           import.SourceByteLength!=import.AnnotationSnapshotByteLength)
            throw new InvalidDataException("DXF içeriği kaynak incelemesinden sonra değişti; metin eşlenmedi.");
        var annotations=import.Annotations;
        if(annotations is null || annotations.Truncated)
            throw new InvalidDataException("DXF metin envanteri eksik veya kesilmiş; metin eşlenmedi.");
        var selected=DxfBoundedSelections.Copy(selections,ProjectStorage.MaximumPieces*4,cancellationToken);
        var bySource=annotations.Records.ToDictionary(a=>a.SourceOrdinal);
        var copy=import.Project.Clone();
        ProjectStorage.Validate(copy);
        if(copy.GradingRules.Count!=0 || copy.NestingSnapshot is not null)
            throw new InvalidDataException("Serileme veya pastal ilişkileri olan projede DXF metni eşlenemez.");
        if(!string.Equals(DxfTextileGrainlineMapping.CaptureFingerprint(import with { Project=copy },cancellationToken),expectedFingerprint,StringComparison.Ordinal))
            throw new InvalidDataException("DXF içeriği kopyalanırken değişti; metin eşlenmedi.");
        var pieces=copy.Pieces.ToDictionary(p=>p.Id);
        var sourceByPiece=import.PieceSources is { } sources && !sources.IsDefault
            ? sources.ToDictionary(source=>source.PieceId) : null;
        var used=new HashSet<(Guid,DxfTextileTextField)>();
        var audit=ImmutableArray.CreateBuilder<DxfTextileTextAudit>(selected.Length);
        foreach(var selection in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(selection is null || selection.Field is not (DxfTextileTextField.Size or DxfTextileTextField.Material or DxfTextileTextField.Quantity or DxfTextileTextField.PieceName) ||
               !used.Add((selection.PieceId,selection.Field)) || !pieces.TryGetValue(selection.PieceId,out var piece) ||
               !bySource.TryGetValue(selection.SourceOrdinal,out var annotation) || annotation.EntityType!="TEXT" || annotation.Section!="ENTITIES" || annotation.Block is not null)
                throw new InvalidDataException("DXF metin seçimi eksik, yinelenmiş veya desteklenmiyor.");
            if(!piece.Closed)throw new InvalidDataException("Tekstil bilgisi için kapalı kontur seçilmelidir.");
            if(annotation.LayerVisible is not true)
                throw new InvalidDataException("DXF TEXT katmanı görünmez veya görünürlüğü doğrulanamadı.");
            // Transparency may be inherited or encoded in group 440. Until
            // the effective value is resolved, do not present it as visible.
            if(annotation.LayerHasNoTransparency is not true || annotation.Groups.Any(g=>g.Code==440))
                throw new InvalidDataException("DXF TEXT saydamlığı doğrulanamadı; metin eşlenmedi.");
            string? Field(int code)
            {
                var groups=annotation.Groups.Where(g=>g.Code==code).ToArray();
                if(groups.Length>1)throw new InvalidDataException($"DXF TEXT {code} alanı yinelenmiş.");
                return groups.Length==0 ? null : groups[0].RawValue.Trim();
            }
            int? Flag(int code)
            {
                var raw=Field(code);
                if(raw is null)return null;
                if(!int.TryParse(raw,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value))
                    throw new InvalidDataException($"DXF TEXT {code} alanı geçersiz.");
                return value;
            }
            var visibility=Flag(60);var space=Flag(67);var color=Flag(62);var layout=Field(410);
            if(visibility is not (null or 0) || space is not (null or 0) || color is < 1 or > 256 ||
               (layout is not null && !layout.Equals("Model",StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Görünmez, model dışı veya belirsiz DXF TEXT eşlenemez.");
            var values=annotation.Groups.Where(g=>g.Code==1).ToArray();
            if(values.Length!=1)throw new InvalidDataException("DXF TEXT değeri eksik veya yinelenmiş.");
            var rawValue=values[0].RawValue;
            var value=DxfTextileTextDecoder.Decode(rawValue);
            string? previousValue=null;
            if(selection.Field==DxfTextileTextField.PieceName)
            {
                previousValue=piece.Name;
                piece.Name=value;
            }
            else
            {
                var metadata=piece.Metadata ??= new PieceMetadata();
                if(selection.Field==DxfTextileTextField.Size)
                {
                    if(metadata.Size is not null)throw new InvalidDataException("Mevcut beden bilgisi üzerine yazılamaz.");
                    metadata.Size=value;
                }
                else if(selection.Field==DxfTextileTextField.Material)
                {
                    if(metadata.Material is not null)throw new InvalidDataException("Mevcut malzeme bilgisi üzerine yazılamaz.");
                    metadata.Material=value;
                }
                else
                {
                    if(metadata.Quantity is not null)throw new InvalidDataException("Mevcut adet bilgisi üzerine yazılamaz.");
                    if(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var quantity) || quantity is <1 or >100000)
                        throw new InvalidDataException("DXF adet değeri 1 ile 100000 arasında düz bir tam sayı olmalıdır.");
                    metadata.Quantity=quantity;
                }
            }
            var targetSource=sourceByPiece is not null && sourceByPiece.TryGetValue(selection.PieceId,out var found) ? found : null;
            audit.Add(new DxfTextileTextAudit(selection.PieceId,selection.SourceOrdinal,selection.Field,value,rawValue) { PreviousValue=previousValue, TargetSource=targetSource });
        }
        cancellationToken.ThrowIfCancellationRequested();
        ProjectStorage.Validate(copy);
        cancellationToken.ThrowIfCancellationRequested();
        return new(copy,audit.MoveToImmutable(),annotations,import.OpaqueMetadata,import.SupplementalGeometry,import.CapturedWarnings,import.SourceSha256,import.ImportParameters)
            { SourceGeometry=import.SourceGeometry, SkippedSourceRecords=import.SkippedSourceRecords, SkippedSourceDetails=import.SkippedSourceDetails, SourceObjects=import.SourceObjects };
    }
}
