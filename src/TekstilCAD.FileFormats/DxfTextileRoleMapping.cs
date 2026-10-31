using System.Collections.Immutable;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

public enum DxfTextileRole { Cutting, Auxiliary }
/// <summary>Explicit user selection; a role is never inferred from a DXF layer or color.</summary>
public sealed record DxfTextileRoleSelection(Guid PieceId, DxfTextileRole Role);
public sealed record DxfTextileRoleAudit(Guid PieceId, DxfTextileRole Role);
public sealed record DxfTextileRoleMappingResult(ProjectDocument Project, ImmutableArray<DxfTextileRoleAudit> Assignments);

/// <summary>
/// Applies explicit roles to a project copy. This does not certify production
/// suitability or guarantee that downstream exporters enforce the role.
/// </summary>
public static class DxfTextileRoleMapping
{
    public static DxfTextileRoleMappingResult Apply(DxfImportResult import,IReadOnlyCollection<DxfTextileRoleSelection> selections,
        string expectedFingerprint,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        cancellationToken.ThrowIfCancellationRequested();
        if(!string.Equals(DxfTextileGrainlineMapping.CaptureFingerprint(import,cancellationToken),expectedFingerprint,StringComparison.Ordinal))
            throw new InvalidDataException("DXF inceleme kimliği değişti; rol eşlenmedi.");
        var selected=DxfBoundedSelections.Copy(selections,import.Project.Pieces.Count,cancellationToken);
        var copy=import.Project.Clone();
        ProjectStorage.Validate(copy);
        if(copy.GradingRules.Count!=0 || copy.NestingSnapshot is not null)
            throw new InvalidDataException("Serileme veya pastal ilişkileri olan projede DXF rolü eşlenemez.");
        if(!string.Equals(DxfTextileGrainlineMapping.CaptureFingerprint(import with { Project=copy },cancellationToken),expectedFingerprint,StringComparison.Ordinal))
            throw new InvalidDataException("DXF içeriği kopyalanırken değişti; rol eşlenmedi.");
        var audit=ApplyToCopy(copy,selected,cancellationToken);
        ProjectStorage.Validate(copy);
        cancellationToken.ThrowIfCancellationRequested();
        return new(copy,audit);
    }

    // Called only on a private transactional copy by this class or the workflow.
    internal static ImmutableArray<DxfTextileRoleAudit> ApplyToCopy(ProjectDocument copy,IReadOnlyList<DxfTextileRoleSelection> selections,
        CancellationToken cancellationToken)
    {
        var pieces=copy.Pieces.ToDictionary(p=>p.Id);
        var used=new HashSet<Guid>();
        var audit=ImmutableArray.CreateBuilder<DxfTextileRoleAudit>(selections.Count);
        foreach(var selection in selections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(selection is null || selection.Role is not (DxfTextileRole.Cutting or DxfTextileRole.Auxiliary) ||
               !used.Add(selection.PieceId) || !pieces.TryGetValue(selection.PieceId,out var piece))
                throw new InvalidDataException("DXF rol seçimi eksik, yinelenmiş veya geçersiz.");
            if(selection.Role==DxfTextileRole.Cutting && !piece.Closed)
                throw new InvalidDataException("Kesim rolü için kapalı kontur seçilmelidir.");
            var metadata=piece.Metadata ??= new PieceMetadata();
            if(metadata.Role is not null)throw new InvalidDataException("Mevcut parça rolü üzerine yazılamaz.");
            metadata.Role=selection.Role==DxfTextileRole.Cutting ? "Kesim" : "Yardımcı";
            audit.Add(new(selection.PieceId,selection.Role));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return audit.MoveToImmutable();
    }
}
