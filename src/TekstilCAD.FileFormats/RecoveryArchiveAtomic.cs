using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TekstilCAD.FileFormats;

/// <summary>Windows-only verified rename of a locked, single-link recovery file.</summary>
internal static class RecoveryArchiveAtomic
{
    [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, IntPtr data, uint size);
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfo info);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
    }

    internal static void Move(string source, string target, string expectedHash, Action<string>? afterLockedAndHashed, Action<string>? beforeArchiveCreation = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Kurtarma kaydının güvenli arşivlenmesi bu platformda desteklenmiyor; özgün kayıt korundu.");
        var sourceDirectory=Path.GetDirectoryName(Path.GetFullPath(source))!;
        var targetDirectory=Path.GetDirectoryName(Path.GetFullPath(target))!;
        if (!string.Equals(Path.TrimEndingDirectorySeparator(targetDirectory),
                Path.TrimEndingDirectorySeparator(Path.Combine(sourceDirectory,"archived")),StringComparison.OrdinalIgnoreCase))
            throw new IOException("Arşiv klasörü kurtarma klasörüne ait değil; özgün kayıt korundu.");
        var targetName=Path.GetFullPath(target);
        // Pin every path component from the drive root down. Holding only the
        // leaf directory does not stop an ancestor from being renamed and a
        // different directory being installed at the same full path.
        // Pin the existing recovery directory and every ancestor before the
        // first filesystem mutation. The new leaf is then verified and pinned
        // under that same chain for the entire verified rename.
        using var directories=DirectoryChain.Open(sourceDirectory);
        beforeArchiveCreation?.Invoke(targetDirectory);
        Directory.CreateDirectory(LongPath(targetDirectory));
        directories.Add(targetDirectory);
        ProjectStorage.EnsureWritableDestination(target);
        // Excluding FILE_SHARE_WRITE and FILE_SHARE_DELETE prevents another process
        // from changing, unlinking or replacing the verified source while open.
        const uint genericRead=0x80000000, delete=0x00010000, shareRead=1, openExisting=3, openReparse=0x00200000;
        using var handle=CreateFile(LongPath(source),genericRead|delete,shareRead,IntPtr.Zero,openExisting,openReparse,IntPtr.Zero);
        if(handle.IsInvalid) throw Win32Error("Kurtarma kaydı güvenle kilitlenemedi; özgün kayıt korundu.");
        _=FileIdentity.CaptureOwnedFile(handle);
        using var stream=new FileStream(handle,FileAccess.Read,4096,isAsync:false);
        if(stream.Length>RecoveryEnvelope.MaximumBytes) throw new InvalidDataException("Kurtarma kaydı boyut sınırını aşıyor; özgün kayıt korundu.");
        var actual=Convert.ToHexString(SHA256.HashData(stream));
        if(!string.Equals(actual,expectedHash,StringComparison.Ordinal))
            throw new IOException("Kurtarma kaydı açıldıktan sonra değişti; daha yeni kayıt korunuyor. Tekrar tarayın.");
        afterLockedAndHashed?.Invoke(source);
        Rename(handle,targetName);
    }

    /// <summary>Directories are opened parent-first without delete sharing and held through rename.</summary>
    private sealed class DirectoryChain : IDisposable
    {
        private readonly List<SafeFileHandle> handles = new();

        internal static DirectoryChain Open(string path)
        {
            var chain=new DirectoryChain();
            try
            {
                var full=Path.GetFullPath(path);
                var root=Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\",StringComparison.Ordinal))
                    throw new IOException("Arşiv yolu güvenle doğrulanamadı; özgün kayıt korundu.");
                chain.Add(root);
                var current=root;
                var relative=Path.GetRelativePath(root,full);
                if (relative!=".")
                    foreach(var component in relative.Split([Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar],StringSplitOptions.RemoveEmptyEntries))
                    {
                        if(component is "." or "..")
                            throw new IOException("Arşiv yolu güvenle doğrulanamadı; özgün kayıt korundu.");
                        current=Path.Combine(current,component);
                        chain.Add(current);
                    }
                return chain;
            }
            catch { chain.Dispose(); throw; }
        }

        internal void Add(string path)
        {
            const uint readAttributes=0x80, shareRead=1, shareWrite=2, openExisting=3;
            const uint backupSemantics=0x02000000, openReparse=0x00200000;
            var handle=CreateFile(LongPath(path),readAttributes,shareRead|shareWrite,IntPtr.Zero,openExisting,backupSemantics|openReparse,IntPtr.Zero);
            if(handle.IsInvalid)
            {
                var error=Win32Error("Arşiv yolu güvenle kilitlenemedi; özgün kayıt korundu.");
                handle.Dispose(); throw error;
            }
            handles.Add(handle);
            if(!GetFileInformationByHandle(handle,out var info))
                throw Win32Error("Arşiv yolu doğrulanamadı; özgün kayıt korundu.");
            if((info.Attributes&0x10)==0 || (info.Attributes&0x400)!=0)
                throw new IOException("Arşiv yolunda güvenli olmayan bir klasör var; özgün kayıt korundu.");
        }

        public void Dispose()
        {
            for(var i=handles.Count-1;i>=0;i--)handles[i].Dispose();
            handles.Clear();
        }
    }

    private static string LongPath(string path)
    {
        var full=Path.GetFullPath(path);
        if(full.StartsWith(@"\\",StringComparison.Ordinal)) throw new IOException("Ağ dosyasında güvenli arşivleme desteklenmiyor; özgün kayıt korundu.");
        return @"\\?\"+full;
    }

    private static void Rename(SafeFileHandle file,string name)
    {
        var bytes=Encoding.Unicode.GetBytes(name);
        // FILE_RENAME_INFO: BOOLEAN, padding, HANDLE, DWORD, WCHAR[].
        int rootOffset=IntPtr.Size==8?8:4, lengthOffset=rootOffset+IntPtr.Size, nameOffset=lengthOffset+4;
        int size=Math.Max(IntPtr.Size==8?24:16,checked(nameOffset+bytes.Length+2));
        var memory=Marshal.AllocHGlobal(size);
        try
        {
            for(var i=0;i<size;i++) Marshal.WriteByte(memory,i,0);
            // The destination directory remains open without delete sharing while the full target path is resolved.
            Marshal.WriteInt32(memory,lengthOffset,bytes.Length);
            Marshal.Copy(bytes,0,IntPtr.Add(memory,nameOffset),bytes.Length);
            if(!SetFileInformationByHandle(file,3,memory,(uint)size))
                throw Win32Error("Kurtarma kaydı güvenle arşivlenemedi; özgün kayıt korundu.");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static IOException Win32Error(string message) => new(message,new Win32Exception(Marshal.GetLastWin32Error()));
}
