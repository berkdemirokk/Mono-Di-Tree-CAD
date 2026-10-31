using System.Security.Cryptography;

namespace TekstilCAD.FileFormats;

/// <summary>
/// Caller-selected companion bytes and the DXF snapshot identity shown during
/// selection. Their association, format and textile meaning are not verified.
/// This object is not persisted automatically.
/// </summary>
public sealed class DxfCompanionSnapshot
{
    private readonly byte[] bytes;
    private DxfCompanionSnapshot(byte[] bytes,string sha256,string dxfSha256,long dxfLength,
        DxfTextEncoding dxfEncoding,DxfImportParameters? dxfImportParameters)
    { this.bytes=bytes;Sha256=sha256;DxfSha256=dxfSha256;DxfByteLength=dxfLength;DxfEncoding=dxfEncoding;DxfImportParameters=dxfImportParameters; }
    public string Sha256 { get; }
    public long ByteLength => bytes.LongLength;
    public string DxfSha256 { get; }
    public long DxfByteLength { get; }
    /// <summary>DXF decoding selected by the caller, not a proven source encoding.</summary>
    public DxfTextEncoding DxfEncoding { get; }
    /// <summary>Actual completed import choices, or null for an inspection or an isolated record view.</summary>
    public DxfImportParameters? DxfImportParameters { get; }

    /// <summary>Returns a caller-owned copy of the captured companion bytes.</summary>
    public byte[] CopyBytes() => (byte[])bytes.Clone();

    /// <summary>
    /// Reads from the current position through end without closing or writing
    /// the stream. No filename lookup, decoder or grade-rule parser is used.
    /// </summary>
    public static DxfCompanionSnapshot Capture(Stream companion,DxfSourceRecordView selectedDxf,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(companion);
        ArgumentNullException.ThrowIfNull(selectedDxf);
        cancellationToken.ThrowIfCancellationRequested();
        if(selectedDxf.SourceSha256 is not { Length:64 } dxfHash || !dxfHash.All(Uri.IsHexDigit) ||
           selectedDxf.SourceByteLength is not (>=0 and <=DxfSourceSnapshotCheck.MaximumBytes) || selectedDxf.Encoding is null)
            throw new InvalidDataException("DXF kaynak kimliği eksik veya geçersiz.");
        return CaptureCore(companion,dxfHash,selectedDxf.SourceByteLength.Value,selectedDxf.Encoding.Value,null,cancellationToken);
    }

    /// <summary>Uses a pre-import inspection, including when no individual record can be viewed.</summary>
    public static DxfCompanionSnapshot Capture(Stream companion,DxfBlockPlacementInspection inspection,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(companion);
        ArgumentNullException.ThrowIfNull(inspection);
        cancellationToken.ThrowIfCancellationRequested();
        if(inspection.SourceSha256 is not { Length:64 } dxfHash || !dxfHash.All(Uri.IsHexDigit) ||
           inspection.SourceByteLength is <0 or >DxfSourceSnapshotCheck.MaximumBytes)
            throw new InvalidDataException("DXF inceleme kimliği eksik veya geçersiz.");
        return CaptureCore(companion,dxfHash,inspection.SourceByteLength,inspection.Encoding,null,cancellationToken);
    }

    /// <summary>Uses the internally captured identity of a completed import.</summary>
    public static DxfCompanionSnapshot Capture(Stream companion,DxfImportResult import,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(companion);
        ArgumentNullException.ThrowIfNull(import);
        cancellationToken.ThrowIfCancellationRequested();
        if(import.AnnotationSnapshotSha256 is not { Length:64 } dxfHash || !dxfHash.All(Uri.IsHexDigit) ||
           import.AnnotationSnapshotByteLength is not (>=0 and <=DxfSourceSnapshotCheck.MaximumBytes) ||
           !string.Equals(dxfHash,import.SourceSha256,StringComparison.OrdinalIgnoreCase) ||
           import.AnnotationSnapshotByteLength!=import.SourceByteLength || import.ImportParameters is null)
            throw new InvalidDataException("DXF aktarım kimliği eksik veya değiştirilmiş.");
        return CaptureCore(companion,dxfHash,import.AnnotationSnapshotByteLength.Value,import.ImportParameters.Encoding,import.ImportParameters,cancellationToken);
    }

    private static DxfCompanionSnapshot CaptureCore(Stream companion,string dxfHash,long dxfLength,DxfTextEncoding dxfEncoding,
        DxfImportParameters? dxfImportParameters,CancellationToken cancellationToken)
    {
        if(!companion.CanRead)throw new InvalidDataException("Eşlikçi kaynak akışı okunamıyor.");
        long? start=null,end=null;
        if(companion.CanSeek)
        {
            start=companion.Position;end=companion.Length;
            if(start<0 || end<start || end-start>DxfSourceSnapshotCheck.MaximumBytes)
                throw new InvalidDataException("Eşlikçi kaynak boyutu geçersiz veya 64 MB sınırını aşıyor.");
        }
        using var buffer=new MemoryStream();
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chunk=new byte[81920];long total=0;
        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read=companion.Read(chunk,0,chunk.Length);
            if(read==0)break;
            total+=read;
            if(total>DxfSourceSnapshotCheck.MaximumBytes)throw new InvalidDataException("Eşlikçi kaynak 64 MB sınırını aşıyor.");
            hash.AppendData(chunk,0,read);buffer.Write(chunk,0,read);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if(start.HasValue && (companion.Length!=end || companion.Position!=end || total!=end-start))
            throw new InvalidDataException("Eşlikçi kaynak okuma sırasında değişti.");
        var captured=buffer.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var digest=Convert.ToHexString(hash.GetHashAndReset());
        cancellationToken.ThrowIfCancellationRequested();
        return new(captured,digest,dxfHash,dxfLength,dxfEncoding,dxfImportParameters);
    }
}
