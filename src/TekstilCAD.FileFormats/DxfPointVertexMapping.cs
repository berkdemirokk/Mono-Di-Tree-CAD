using System.Collections.Immutable;
using System.Globalization;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Explicit source POINT and target vertex choice; no physical-notch meaning is inferred.</summary>
public sealed record DxfPointVertexSelection(int SourceOrdinal, Guid PieceId, int VertexIndex);
public sealed record DxfPointVertexAudit(int SourceOrdinal, Guid PieceId, int VertexIndex, Point2 Position);
public sealed record DxfPointVertexResult(ProjectDocument Project, ImmutableArray<DxfPointVertexAudit> Assignments);

/// <summary>Conservative mapping of untransformed integer model-space POINTs at a one-mm scale.
/// Caller must keep the import and source stream unchanged while this method runs.</summary>
public static class DxfPointVertexMapping
{
    public static DxfPointVertexResult Apply(DxfImportResult import, Stream source,
        IReadOnlyCollection<DxfPointVertexSelection> selections, string expectedFingerprint,
        CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(import);ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selections);ArgumentNullException.ThrowIfNull(expectedFingerprint);
        cancellationToken.ThrowIfCancellationRequested();
        if(!source.CanRead || !source.CanSeek || source.Position!=0)
            throw new InvalidDataException("DXF kaynak akışı baştan okunabilir olmalıdır.");
        var check=DxfSourceSnapshotCheck.CheckStream(source,import,cancellationToken);
        if(check.Status!=DxfSourceSnapshotCheck.Status.MatchesReadBytes || !check.ImportIdentityVerified)
            throw new InvalidDataException("DXF kaynak baytları içe aktarımla eşleşmiyor.");
        if(!string.Equals(DxfTextileGrainlineMapping.CaptureFingerprint(import,cancellationToken),expectedFingerprint,StringComparison.Ordinal) ||
           import.ImportParameters?.MillimetersPerUnit!=1 || import.Project.GradingRules.Count!=0 || import.Project.NestingSnapshot is not null)
            throw new InvalidDataException("DXF geometrisi veya desteklenen ölçek doğrulanamadı.");
        var report=import.SupplementalGeometry;
        if(report is null || report.Truncated || report.Records.IsDefault)
            throw new InvalidDataException("DXF nokta envanteri eksik veya kesilmiş.");
        var records=report.Records.Where(r=>r.EntityType=="POINT").ToDictionary(r=>r.SourceOrdinal);
        var selected=DxfBoundedSelections.Copy(selections,ProjectStorage.MaximumPieces,cancellationToken);
        if((long)selected.Length*import.Project.Pieces.Sum(p=>(long)p.Points.Count)>2_000_000)
            throw new InvalidDataException("DXF nokta eşleme çalışma sınırı aşıldı.");
        var copy=import.Project.Clone();ProjectStorage.Validate(copy,cancellationToken);
        if(!string.Equals(DxfTextileGrainlineMapping.CaptureFingerprint(import with { Project=copy },cancellationToken),expectedFingerprint,StringComparison.Ordinal))
            throw new InvalidDataException("DXF geometrisi kopyalanırken değişti.");
        var pieces=copy.Pieces.ToDictionary(p=>p.Id);var usedSources=new HashSet<int>();var usedTargets=new HashSet<(Guid,int)>();
        var audit=ImmutableArray.CreateBuilder<DxfPointVertexAudit>();
        foreach(var choice in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(choice is null || !usedSources.Add(choice.SourceOrdinal) || !usedTargets.Add((choice.PieceId,choice.VertexIndex)) ||
               !records.TryGetValue(choice.SourceOrdinal,out var point) || point.Section!="ENTITIES" || point.Block is not null ||
               point.Groups.IsDefault || point.Groups.Any(g=>g.Code is not (5 or 8 or 10 or 20 or 30 or 100)) ||
               !pieces.TryGetValue(choice.PieceId,out var piece) || !piece.Closed || choice.VertexIndex<0 || choice.VertexIndex>=piece.Points.Count ||
               (choice.VertexIndex==piece.Points.Count-1 && piece.Points[0]==piece.Points[^1]))
                throw new InvalidDataException("DXF POINT seçimi veya hedef köşesi geçersiz.");
            long Coordinate(int code,bool optional=false)
            {
                var values=point.Groups.Where(g=>g.Code==code).ToArray();
                if(optional && values.Length==0)return 0;
                if(values.Length!=1 || !long.TryParse(values[0].RawValue.Trim(),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var value) || Math.Abs((double)value)>100_000_000)
                    throw new InvalidDataException("DXF POINT koordinatı tam sayı olarak doğrulanamadı.");
                return value;
            }
            var x=Coordinate(10);var y=Coordinate(20);if(Coordinate(30,true)!=0)throw new InvalidDataException("3B POINT desteklenmiyor.");
            var position=new Point2(x,y);
            if(piece.Points[choice.VertexIndex]!=position)throw new InvalidDataException("POINT seçilen köşeyle tam eşleşmiyor.");
            int matches=0;
            foreach(var candidate in copy.Pieces) for(int i=0;i<candidate.Points.Count;i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(candidate.Closed && i==candidate.Points.Count-1 && candidate.Points[0]==candidate.Points[^1])continue;
                if(candidate.Points[i]==position && ++matches>1)throw new InvalidDataException("POINT birden fazla köşeyle eşleşiyor.");
            }
            if(matches!=1 || piece.Notches?.Any(n=>n.VertexIndex==choice.VertexIndex)==true)
                throw new InvalidDataException("POINT eşleşmesi belirsiz veya işaret zaten mevcut.");
            (piece.Notches ??= []).Add(new NotchMark { VertexIndex=choice.VertexIndex,Position=position });
            audit.Add(new(choice.SourceOrdinal,choice.PieceId,choice.VertexIndex,position));
        }
        ProjectStorage.Validate(copy,cancellationToken);
        return new(copy,audit.ToImmutable());
    }
}
