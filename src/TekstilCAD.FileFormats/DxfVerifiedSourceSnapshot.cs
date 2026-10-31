using System.Security.Cryptography;

namespace TekstilCAD.FileFormats;

/// <summary>Captures original DXF bytes only when a re-read matches an earlier import snapshot.</summary>
public static class DxfVerifiedSourceSnapshot
{
    /// <summary>
    /// Reads from the stream's current position through its end, advances but
    /// never closes the stream, and returns caller-owned bytes. The source hash
    /// identifies the bytes read; it does not authenticate their author or
    /// establish textile semantics. No filesystem path is opened or written.
    /// </summary>
    public static byte[] Capture(Stream input,DxfImportResult import,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(import);
        cancellationToken.ThrowIfCancellationRequested();
        var hash=import.AnnotationSnapshotSha256;
        var length=import.AnnotationSnapshotByteLength;
        if(hash is not { Length:64 } || !hash.All(Uri.IsHexDigit) ||
           length is not (>=0 and <=DxfSourceSnapshotCheck.MaximumBytes) ||
           !string.Equals(hash,import.SourceSha256,StringComparison.OrdinalIgnoreCase) || length!=import.SourceByteLength)
            throw new InvalidDataException("DXF aktarımına ait kaynak kimliği eksik veya değiştirilmiş.");
        return CaptureCore(input,hash,length.Value,cancellationToken);
    }

    /// <summary>Captures bytes from a pre-import inspection, including when controlled import would reject the source.</summary>
    public static byte[] Capture(Stream input,DxfBlockPlacementInspection inspection,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(inspection);
        cancellationToken.ThrowIfCancellationRequested();
        if(inspection.SourceSha256 is not { Length:64 } hash || !hash.All(Uri.IsHexDigit) ||
           inspection.SourceByteLength is <0 or >DxfSourceSnapshotCheck.MaximumBytes)
            throw new InvalidDataException("DXF incelemesine ait kaynak kimliği geçersiz.");
        return CaptureCore(input,hash,inspection.SourceByteLength,cancellationToken);
    }

    /// <summary>
    /// Re-captures the complete original source using the identity carried by a
    /// record view. This verifies bytes only; it does not prove any grading or
    /// textile interpretation and does not reconstruct a DXF from groups.
    /// </summary>
    public static byte[] Capture(Stream input,DxfSourceRecordView record,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        if(record.SourceSha256 is not { Length:64 } hash || !hash.All(Uri.IsHexDigit) ||
           record.SourceByteLength is not (>=0 and <=DxfSourceSnapshotCheck.MaximumBytes) || record.Encoding is null)
            throw new InvalidDataException("DXF kaynak kaydına ait kimlik eksik veya geçersiz.");
        return CaptureCore(input,hash,record.SourceByteLength.Value,cancellationToken);
    }

    private static byte[] CaptureCore(Stream input,string hash,long length,CancellationToken cancellationToken)
    {
        if(!input.CanRead || !input.CanSeek)throw new InvalidDataException("DXF kaynak akışı okunabilir ve konumlanabilir olmalıdır.");
        var start=input.Position;
        var end=input.Length;
        if(start<0 || end<start || end-start!=length)
            throw new InvalidDataException("DXF kaynak uzunluğu içe alınan kopyayla uyuşmuyor.");
        var bytes=new byte[checked((int)length)];
        using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var offset=0;
        while(offset<bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read=input.Read(bytes,offset,Math.Min(81920,bytes.Length-offset));
            if(read<=0)throw new InvalidDataException("DXF kaynak akışı beklenenden önce sona erdi.");
            digest.AppendData(bytes,offset,read);offset+=read;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if(input.Length!=end || input.Position!=end)
            throw new InvalidDataException("DXF kaynak akışı okunurken değişti.");
        var observed=digest.GetHashAndReset();
        var expected=Convert.FromHexString(hash);
        if(!CryptographicOperations.FixedTimeEquals(observed,expected))
            throw new InvalidDataException("DXF kaynak baytları içe alınan kopyayla uyuşmuyor.");
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }
}
