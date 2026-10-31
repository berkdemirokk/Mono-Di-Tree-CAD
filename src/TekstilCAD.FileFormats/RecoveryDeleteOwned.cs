using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace TekstilCAD.FileFormats;

/// <summary>Remove only the exact snapshot created by this session, by its verified Windows handle.</summary>
internal static class RecoveryDeleteOwned
{
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file,int informationClass,IntPtr information,uint size);

    internal static bool TryDelete(string path,FileIdentity.OwnedFileStamp? expectedIdentity,string? expectedHash,Action<string>? beforeDelete)
    {
        if(!OperatingSystem.IsWindows())
        {
            try { _=File.GetAttributes(path); }
            catch(FileNotFoundException) { return false; }
            catch(DirectoryNotFoundException) { return false; }
            throw new PlatformNotSupportedException("Kurtarma kaydı bu platformda güvenle temizlenemiyor; özgün kayıt korundu.");
        }
        var full=Path.GetFullPath(path);
        if(full.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("Ağ kurtarma kaydı güvenle temizlenemiyor; özgün kayıt korundu.");
        const uint genericRead=0x80000000,delete=0x00010000,shareRead=1,openExisting=3,openReparse=0x00200000;
        using var handle=CreateFile(@"\\?\"+full,genericRead|delete,shareRead,IntPtr.Zero,openExisting,openReparse,IntPtr.Zero);
        if(handle.IsInvalid)
        {
            var error=Marshal.GetLastWin32Error();
            if(error is 2 or 3)return false;
            throw new IOException("Kurtarma kaydı güvenle kilitlenemedi; özgün kayıt korundu.",new Win32Exception(error));
        }
        var identity=FileIdentity.CaptureOwnedFile(handle);
        if(expectedIdentity is null || expectedHash is null || identity!=expectedIdentity.Value)
            throw new IOException("Kurtarma kaydı değişti; yeni kayıt korunuyor.");
        using var stream=new FileStream(handle,FileAccess.Read,4096,isAsync:false);
        if(stream.Length>RecoveryEnvelope.MaximumBytes)
            throw new IOException("Kurtarma kaydı boyutu değişti; yeni kayıt korunuyor.");
        var hash=Convert.ToHexString(SHA256.HashData(stream));
        if(!string.Equals(hash,expectedHash,StringComparison.Ordinal))
            throw new IOException("Kurtarma kaydı değişti; yeni kayıt korunuyor.");
        beforeDelete?.Invoke(full);
        // FILE_DISPOSITION_INFO.DeleteFile is a one-byte BOOLEAN. Delete is
        // committed to the exact open object, with no second path lookup.
        var info=Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(info,1);
            if(!SetFileInformationByHandle(handle,4,info,1))
                throw new IOException("Kurtarma kaydı güvenle temizlenemedi; özgün kayıt korundu.",new Win32Exception(Marshal.GetLastWin32Error()));
        }
        finally { Marshal.FreeHGlobal(info); }
        return true;
    }
}
