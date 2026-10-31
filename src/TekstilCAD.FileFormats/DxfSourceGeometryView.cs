using System.Collections.Immutable;

namespace TekstilCAD.FileFormats;

/// <summary>A decoded group in source order. This is not the original byte representation.</summary>
public sealed record DxfSourceGroupView(int GroupOrdinal,int Code,string RawValue,bool InApplicationGroup);
/// <summary>One uninterpreted source record, not a validated contour or placement.</summary>
public sealed class DxfSourceRecordView
{
    public DxfSourceRecordView(int sourceOrdinal,string section,string? block,string entityType,ImmutableArray<DxfSourceGroupView> groups)
    { SourceOrdinal=sourceOrdinal;Section=section;Block=block;EntityType=entityType;Groups=groups; }
    public int SourceOrdinal { get; }
    public string Section { get; }
    public string? Block { get; }
    public string EntityType { get; }
    public ImmutableArray<DxfSourceGroupView> Groups { get; }
    /// <summary>Identity of the captured source bytes, not authentication of the file or its meaning.</summary>
    public string? SourceSha256 { get; internal init; }
    public long? SourceByteLength { get; internal init; }
    /// <summary>Explicit decoder used for the source groups; this does not establish the intended source encoding.</summary>
    public DxfTextEncoding? Encoding { get; internal init; }
}

/// <summary>Joins a captured geometry record with its private groups from the same snapshot.</summary>
public static class DxfSourceGeometryView
{
    public static DxfSourceRecordView Read(DxfImportResult import,int sourceOrdinal,CancellationToken cancellationToken=default)
    {
        ValidateImport(import);
        return Attach(ReadCore(import.SourceGeometry,import.OpaqueMetadata,sourceOrdinal,cancellationToken),import);
    }

    public static DxfSourceRecordView Read(DxfBlockPlacementInspection inspection,int sourceOrdinal,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return Attach(ReadCore(inspection.SourceGeometry,inspection.OpaqueMetadata,sourceOrdinal,cancellationToken),inspection);
    }

    /// <summary>Reads an unassessed source entity from inspection, not an indication that import supports it.</summary>
    public static DxfSourceRecordView ReadOther(DxfBlockPlacementInspection inspection,int sourceOrdinal,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return Attach(ReadCore(inspection.OtherSourceRecords,inspection.OpaqueMetadata,sourceOrdinal,cancellationToken),inspection);
    }

    public static DxfSourceRecordView ReadObject(DxfImportResult import,int sourceOrdinal,CancellationToken cancellationToken=default)
    {
        ValidateImport(import);
        return Attach(ReadCore(import.SourceObjects,import.OpaqueMetadata,sourceOrdinal,cancellationToken),import);
    }

    public static DxfSourceRecordView ReadObject(DxfBlockPlacementInspection inspection,int sourceOrdinal,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return Attach(ReadCore(inspection.SourceObjects,inspection.OpaqueMetadata,sourceOrdinal,cancellationToken),inspection);
    }

    public static DxfSourceRecordView ReadAnnotation(DxfImportResult import,int sourceOrdinal,CancellationToken cancellationToken=default)
    {
        ValidateImport(import);
        return Attach(ReadAnnotationCore(import.Annotations,import.OpaqueMetadata,sourceOrdinal,cancellationToken),import);
    }

    public static DxfSourceRecordView ReadAnnotation(DxfBlockPlacementInspection inspection,int sourceOrdinal,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return Attach(ReadAnnotationCore(inspection.Annotations,inspection.OpaqueMetadata,sourceOrdinal,cancellationToken),inspection);
    }

    public static DxfSourceRecordView ReadSupplemental(DxfImportResult import,int sourceOrdinal,CancellationToken cancellationToken=default)
    {
        ValidateImport(import);
        return Attach(ReadSupplementalCore(import.SupplementalGeometry,import.OpaqueMetadata,sourceOrdinal,cancellationToken),import);
    }

    public static DxfSourceRecordView ReadSupplemental(DxfBlockPlacementInspection inspection,int sourceOrdinal,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return Attach(ReadSupplementalCore(inspection.SupplementalGeometry,inspection.OpaqueMetadata,sourceOrdinal,cancellationToken),inspection);
    }

    public static DxfSourceRecordView ReadSkipped(DxfImportResult import,int sourceOrdinal,CancellationToken cancellationToken=default)
    {
        ValidateImport(import);
        cancellationToken.ThrowIfCancellationRequested();
        var report=import.SkippedSourceDetails;
        if(sourceOrdinal<0 || report is null || report.Records.IsDefault)throw new InvalidDataException("DXF atlanan kaynak kaydı eksik.");
        DxfSkippedSourceDetail? found=null;
        foreach(var item in report.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(item.Source.SourceOrdinal!=sourceOrdinal)continue;
            if(found is not null)throw new InvalidDataException("DXF atlanan kaynak kaydı yinelenmiş.");
            found=item;
        }
        if(found is null)throw new InvalidDataException("DXF atlanan kaynak kaydı bulunamadı.");
        var source=found.Source;
        return Attach(Join(source.SourceOrdinal,source.Section,source.Block,source.EntityType,found.Groups,import.OpaqueMetadata,cancellationToken),import);
    }

    private static DxfSourceRecordView Attach(DxfSourceRecordView record,DxfImportResult import)
        => new(record.SourceOrdinal,record.Section,record.Block,record.EntityType,record.Groups)
            { SourceSha256=import.AnnotationSnapshotSha256,SourceByteLength=import.AnnotationSnapshotByteLength,Encoding=import.ImportParameters!.Encoding };

    private static DxfSourceRecordView Attach(DxfSourceRecordView record,DxfBlockPlacementInspection inspection)
        => new(record.SourceOrdinal,record.Section,record.Block,record.EntityType,record.Groups)
            { SourceSha256=inspection.SourceSha256,SourceByteLength=inspection.SourceByteLength,Encoding=inspection.Encoding };

    private static DxfSourceRecordView ReadSupplementalCore(DxfSupplementalGeometryReport? report,DxfOpaqueMetadata? opaque,int ordinal,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(ordinal<0 || report is null || report.Records.IsDefault)throw new InvalidDataException("DXF ek geometri kaydı eksik.");
        DxfSourceRecordView? found=null;
        foreach(var item in report.Records)
        {
            token.ThrowIfCancellationRequested();
            if(item.SourceOrdinal==ordinal)
            {
                if(found is not null)throw new InvalidDataException("DXF ek geometri kaydı yinelenmiş.");
                found=Join(ordinal,item.Section,item.Block,item.EntityType,item.Groups,opaque,token);
            }
            if(item.Children.IsDefault)throw new InvalidDataException("DXF ek geometri alt kayıtları eksik.");
            foreach(var child in item.Children)
            {
                token.ThrowIfCancellationRequested();
                if(child.SourceOrdinal!=ordinal)continue;
                if(found is not null)throw new InvalidDataException("DXF ek geometri kaydı yinelenmiş.");
                found=Join(ordinal,item.Section,item.Block,child.EntityType,child.Groups,opaque,token);
            }
        }
        return found ?? throw new InvalidDataException("DXF ek geometri kaydı bulunamadı.");
    }

    private static void ValidateImport(DxfImportResult import)
    {
        ArgumentNullException.ThrowIfNull(import);
        if(import.AnnotationSnapshotSha256 is null || import.AnnotationSnapshotByteLength is null || import.ImportParameters is null ||
           !string.Equals(import.AnnotationSnapshotSha256,import.SourceSha256,StringComparison.OrdinalIgnoreCase) ||
           import.AnnotationSnapshotByteLength!=import.SourceByteLength)
            throw new InvalidDataException("DXF kaynak kimliği eksik veya değiştirilmiş.");
    }

    private static DxfSourceRecordView ReadAnnotationCore(DxfAnnotationReport? report,DxfOpaqueMetadata? opaque,int ordinal,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(ordinal<0 || report is null || report.Records.IsDefault)throw new InvalidDataException("DXF metin kaydı eksik.");
        DxfAnnotationRecord? record=null;
        foreach(var item in report.Records)
        {
            token.ThrowIfCancellationRequested();
            if(item.SourceOrdinal!=ordinal)continue;
            if(record is not null)throw new InvalidDataException("DXF metin kaydı yinelenmiş.");
            record=item;
        }
        if(record is null)throw new InvalidDataException("DXF metin kaydı bulunamadı.");
        return Join(record.SourceOrdinal,record.Section,record.Block,record.EntityType,record.Groups,opaque,token);
    }

    private static DxfSourceRecordView ReadCore(DxfSourceGeometryReport? report,DxfOpaqueMetadata? opaque,int ordinal,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(ordinal<0 || report is null || report.Records.IsDefault || opaque is null || opaque.Truncated || opaque.Groups.IsDefault)
            throw new InvalidDataException("DXF kaynak kayıtları eksik veya özel veri envanteri kesilmiş.");
        DxfSourceGeometryRecord? record=null;
        foreach(var item in report.Records)
        {
            token.ThrowIfCancellationRequested();
            if(item.SourceOrdinal!=ordinal)continue;
            if(record is not null)throw new InvalidDataException("DXF kaynak kaydı yinelenmiş.");
            record=item;
        }
        if(record is null || record.Groups.IsDefault || record.GroupOrdinals.IsDefault || record.Groups.Length!=record.GroupOrdinals.Length)
            throw new InvalidDataException("DXF kaynak kaydı bulunamadı veya eksik.");
        for(var i=0;i<record.Groups.Length;i++)
        {
            token.ThrowIfCancellationRequested();
            if(record.Groups[i] is null || record.Groups[i].GroupOrdinal!=record.GroupOrdinals[i])
                throw new InvalidDataException("DXF kaynak grup sırası geçersiz.");
        }
        return Join(record.SourceOrdinal,record.Section,record.Block,record.EntityType,record.Groups,opaque,token);
    }

    private static DxfSourceRecordView Join(int ordinal,string section,string? block,string entityType,
        ImmutableArray<DxfAnnotationGroup> sourceGroups,DxfOpaqueMetadata? opaque,CancellationToken token)
    {
        if(sourceGroups.IsDefault || opaque is null || opaque.Truncated || opaque.Groups.IsDefault)
            throw new InvalidDataException("DXF kaynak kayıtları eksik veya özel veri envanteri kesilmiş.");
        var groups=new SortedDictionary<int,DxfSourceGroupView>();
        foreach(var group in sourceGroups)
        {
            token.ThrowIfCancellationRequested();
            if(group is null || group.GroupOrdinal<=0 || !groups.TryAdd(group.GroupOrdinal,new(group.GroupOrdinal,group.Code,group.RawValue,false)))
                throw new InvalidDataException("DXF kaynak grup sırası geçersiz.");
        }
        foreach(var group in opaque.Groups)
        {
            token.ThrowIfCancellationRequested();
            if(group.SourceOrdinal!=ordinal)continue;
            if(group.GroupOrdinal<=0)throw new InvalidDataException("DXF özel grup sırası geçersiz.");
            if(groups.TryGetValue(group.GroupOrdinal,out var existing))
            {
                // XDATA is also present in the normal decoded record. It must
                // agree exactly; private 102 payload must never overlap it.
                if(group.InApplicationGroup || group.Code<1000 || existing.Code!=group.Code || existing.RawValue!=group.RawValue)
                    throw new InvalidDataException("DXF kaynak grup envanterleri uyuşmuyor.");
            }
            else if(!groups.TryAdd(group.GroupOrdinal,new(group.GroupOrdinal,group.Code,group.RawValue,group.InApplicationGroup)))
                throw new InvalidDataException("DXF özel grup yinelenmiş.");
        }
        var result=ImmutableArray.CreateBuilder<DxfSourceGroupView>(groups.Count);
        var expected=1;
        foreach(var group in groups.Values)
        {
            token.ThrowIfCancellationRequested();
            if(group.GroupOrdinal!=expected++)throw new InvalidDataException("DXF kaynak grup dizisinde boşluk var.");
            result.Add(group);
        }
        return new(ordinal,section,block,entityType,result.MoveToImmutable());
    }
}
