using System.Security.Cryptography;

namespace TekstilCAD.FileFormats;

/// <summary>Read-only comparison with a source fingerprint. A manually constructed result supplies an unverified fingerprint.</summary>
public static class DxfSourceSnapshotCheck
{
    public const long MaximumBytes=64L*1024*1024;
    public enum Status { MatchesReadBytes,Changed,Missing,Unreadable,TooLarge,Unstable }
    public sealed record Result(Status Status,long? ActualByteLength,string? ActualSha256,string? Detail)
    {
        /// <summary>True only for matching bytes tied to an internally captured completed import identity.</summary>
        public bool ImportIdentityVerified { get; init; }
    }

    /// <summary>
    /// Reads at most 64 MiB without writing. A match describes only the bytes
    /// read during this check; the path or file can change immediately afterward.
    /// If captured is manually constructed, a match checks its supplied hash
    /// only; Detail explicitly says that no import identity was verified.
    /// A missing path does not prove intentional deletion. Windows share mode
    /// excludes cooperating writers while this handle is open; external or
    /// noncooperating filesystem races are not eliminated.
    /// </summary>
    public static Result Check(string path,DxfImportResult captured,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(captured);
        ValidateFingerprint(captured);
        try
        {
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,FileOptions.SequentialScan);
            return CheckStream(stream,captured,cancellationToken);
        }
        catch(FileNotFoundException) { return new(Status.Missing,null,null,"Kaynak yolu bulunamadı."); }
        catch(DirectoryNotFoundException) { return new(Status.Missing,null,null,"Kaynak klasörü bulunamadı."); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(Status.Unreadable,null,null,"Kaynak dosya okunamadı: "+ex.Message); }
    }

    internal static Result CheckStream(Stream stream,DxfImportResult captured,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(stream);ArgumentNullException.ThrowIfNull(captured);
        ValidateFingerprint(captured);
        var start=stream.Length;
        if(start>MaximumBytes)return new(Status.TooLarge,start,null,"Kaynak 64 MB karşılaştırma sınırını aşıyor.");
        using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer=new byte[81920];long total=0;
        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count=stream.Read(buffer,0,buffer.Length);
            cancellationToken.ThrowIfCancellationRequested();
            if(count==0)break;
            total+=count;
            if(total>MaximumBytes)return new(Status.TooLarge,total,null,"Kaynak okuma sırasında 64 MB sınırını aştı.");
            digest.AppendData(buffer,0,count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var end=stream.Length;
        if(start!=end || total!=start)return new(Status.Unstable,total,null,"Dosya uzunluğu okuma sırasında değişti; karşılaştırma güvenilir değil.");
        var hash=Convert.ToHexString(digest.GetHashAndReset());
        cancellationToken.ThrowIfCancellationRequested();
        return total==captured.SourceByteLength && string.Equals(hash,captured.SourceSha256,StringComparison.OrdinalIgnoreCase)
            ? new Result(Status.MatchesReadBytes,total,hash,captured.AnnotationSnapshotSha256 is null
                ? "Sağlanan özetle eşleşti; içe aktarma kimliği doğrulanmadı." : null)
                { ImportIdentityVerified=captured.AnnotationSnapshotSha256 is not null }
            : new(Status.Changed,total,hash,"Okunan baytlar daha önce içe alınan kopyadan farklı.");
    }

    private static void ValidateFingerprint(DxfImportResult captured)
    {
        if(captured.SourceByteLength is not (>=0 and <=MaximumBytes) || captured.SourceSha256 is not { Length:64 } hash || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("İçe alınan DXF kopyasının geçerli bayt sayısı ve SHA-256 özeti gerekli.",nameof(captured));
        if((captured.AnnotationSnapshotSha256 is not null || captured.AnnotationSnapshotByteLength is not null) &&
           (captured.AnnotationSnapshotByteLength!=captured.SourceByteLength ||
            !string.Equals(captured.AnnotationSnapshotSha256,hash,StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("DXF aktarımının kaynak kimliği değiştirilmiş veya eksik.",nameof(captured));
    }
}
