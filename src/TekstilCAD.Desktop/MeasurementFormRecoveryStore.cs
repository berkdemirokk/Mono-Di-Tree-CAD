using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Windows-user-protected form values only; request, photo and CAD data are not captured.</summary>
internal sealed class MeasurementFormRecoveryStore : IDisposable
{
    private const string Prefix="TekstilCAD.MeasurementRecovery.v1\n";
    private const int MaximumFileBytes=16_384;
    private readonly string directory,kind,path;
    private readonly FileStream lease;
    private FileIdentity.OwnedFileStamp? identity;
    private string? savedHash;
    private bool disposed;
    internal Action? BeforeCommitForVerification { get; set; }
    internal string PathForVerification=>path;
    internal static string DirectoryPath=>Path.Combine(AppPaths.DataDirectory,"MeasurementFormRecovery");

    internal MeasurementFormRecoveryStore(string directory,string kind)
    {
        ValidateKind(kind);this.kind=kind;this.directory=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var id=Guid.NewGuid().ToString("N");path=Path.Combine(this.directory,$"form-{kind}-{id}.mform");
        var lockPath=Path.ChangeExtension(path,".lock");ProjectStorage.EnsureWritableDestination(lockPath);Directory.CreateDirectory(this.directory);
        ProjectStorage.EnsureWritableDestination(lockPath);lease=new FileStream(lockPath,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
    }
    private static int ValidateKind(string kind)=>kind switch { "basic"=>4,"experimental"=>15,_=>throw new ArgumentException("Ölçü formu türü desteklenmiyor.",nameof(kind)) };
    private static byte[] EncodeText(string value)
    {
        if(value is null || value.Length>24)throw new InvalidDataException("Form alanı kurtarma sınırını aşıyor.");
        var bytes=new byte[value.Length*2];for(int i=0;i<value.Length;i++) { bytes[2*i]=(byte)value[i];bytes[2*i+1]=(byte)(value[i]>>8); }return bytes;
    }
    private static string DecodeText(string encoded)
    {
        var bytes=Convert.FromBase64String(encoded);if(bytes.Length>48 || bytes.Length%2!=0)throw new InvalidDataException("Kurtarma form alanı geçersiz.");
        var chars=new char[bytes.Length/2];for(int i=0;i<chars.Length;i++)chars[i]=(char)(bytes[2*i]|bytes[2*i+1]<<8);return new string(chars);
    }
    internal void Save(IReadOnlyList<string> values)
    {
        ObjectDisposedException.ThrowIf(disposed,this);ArgumentNullException.ThrowIfNull(values);
        if(values.Count!=ValidateKind(kind))throw new InvalidDataException("Kurtarma formu alan sayısı geçersiz.");
        var plain=JsonSerializer.SerializeToUtf8Bytes(new { version=1,kind,values=values.Select(v=>Convert.ToBase64String(EncodeText(v))).ToArray() });
        try
        {
            var protectedBytes=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);
            var text=Prefix+Convert.ToBase64String(protectedBytes);if(Encoding.UTF8.GetByteCount(text)>MaximumFileBytes)throw new InvalidDataException("Kurtarma kaydı çok büyük.");
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            var written=ProjectStorage.AtomicWriteOwned(path,text,identity,savedHash,BeforeCommitForVerification);identity=written;savedHash=hash;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private static string Candidate(string directory,string path,string kind)
    {
        ValidateKind(kind);var dir=Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));var full=Path.GetFullPath(path);
        var name=Path.GetFileNameWithoutExtension(full);var prefix=$"form-{kind}-";
        if(!string.Equals(Path.GetDirectoryName(full),dir,StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetExtension(full),".mform",StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(prefix,StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..],"N",out _))throw new InvalidOperationException("Kurtarma dosyası bu formun yönetilen klasöründe değil.");
        ProjectStorage.EnsureWritableDestination(full);return full;
    }
    private static FileStream AcquireCandidate(string path)
    {
        var lockPath=Path.ChangeExtension(path,".lock");ProjectStorage.EnsureWritableDestination(lockPath);
        try { return new FileStream(lockPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None); }
        catch(IOException ex) { throw new IOException("Kurtarma kaydı başka bir oturumda kullanılıyor veya kilidi okunamıyor; kayıt korundu.",ex); }
    }
    internal static string[] Load(string directory,string path,string kind)
    {
        var full=Candidate(directory,path,kind);using var lockFile=AcquireCandidate(full);
        using var file=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read);
        _=FileIdentity.CaptureOwnedFile(file.SafeFileHandle);
        if(file.Length<=0 || file.Length>MaximumFileBytes)throw new InvalidDataException("Kurtarma kaydı boyutu geçersiz.");
        using var reader=new StreamReader(file,new UTF8Encoding(false,true),false);var text=reader.ReadToEnd();
        if(!text.StartsWith(Prefix,StringComparison.Ordinal))throw new InvalidDataException("Kurtarma kaydı biçimi desteklenmiyor.");
        byte[] plain;
        try { plain=ProtectedData.Unprotect(Convert.FromBase64String(text[Prefix.Length..]),null,DataProtectionScope.CurrentUser); }
        catch(Exception ex) when(ex is FormatException or CryptographicException) { throw new InvalidDataException("Kurtarma kaydı bozuk veya bu Windows hesabıyla açılamıyor; özgün dosya korundu.",ex); }
        try
        {
            using var json=JsonDocument.Parse(plain,new JsonDocumentOptions { MaxDepth=4 });var root=json.RootElement;
            ProjectStorage.RejectDuplicateProperties(root,CancellationToken.None);
            if(root.ValueKind!=JsonValueKind.Object || root.EnumerateObject().Count()!=3 || !root.TryGetProperty("version",out var version) || !version.TryGetInt32(out var v) || v!=1 ||
                !root.TryGetProperty("kind",out var k) || k.ValueKind!=JsonValueKind.String || k.GetString()!=kind || !root.TryGetProperty("values",out var values) ||
                values.ValueKind!=JsonValueKind.Array || values.GetArrayLength()!=ValidateKind(kind))throw new InvalidDataException("Kurtarma formu alanları geçersiz.");
            return values.EnumerateArray().Select(item=>item.ValueKind==JsonValueKind.String ? DecodeText(item.GetString()!) : throw new InvalidDataException("Kurtarma alanı geçersiz.")).ToArray();
        }
        catch(Exception ex) when(ex is JsonException or FormatException or InvalidOperationException)
        { throw new InvalidDataException("Kurtarma kaydı okunamadı; özgün dosya korundu.",ex); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    internal static void Delete(string directory,string path,string kind)
    {
        var full=Candidate(directory,path,kind);using var lockFile=AcquireCandidate(full);
        FileIdentity.OwnedFileStamp stamp;string hash;
        using(var input=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            if(input.Length>MaximumFileBytes)throw new InvalidDataException("Kurtarma kaydı boyutu değişti; dosya korundu.");
            stamp=FileIdentity.CaptureOwnedFile(input.SafeFileHandle);hash=Convert.ToHexString(SHA256.HashData(input));
        }
        if(!RecoveryDeleteOwned.TryDelete(full,stamp,hash,null))throw new IOException("Kurtarma kaydı bulunamadı.");
    }
    public void Dispose() { if(disposed)return;disposed=true;lease.Dispose(); }
}
