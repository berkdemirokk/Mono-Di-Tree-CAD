using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TekstilCAD.FileFormats;

// Pins the destination's entire directory chain and renames the verified file
// handle itself. The rename does not replace an existing destination.
internal static class ProjectBackupAtomic
{
    [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfo info);
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, IntPtr data, uint size);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
    }

    internal static void Publish(string source, string target, FileIdentity.OwnedFileStamp stamp, long length, string hash, Action<string>? afterLocked)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(target))!;
        var root = Path.GetPathRoot(directory);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("Yedek yolu güvenle doğrulanamadı.");
        var handles = new List<SafeFileHandle>();
        try
        {
            void Pin(string path)
            {
                const uint readAttributes=0x80, shareReadWrite=3, openExisting=3, backupSemantics=0x02000000, openReparse=0x00200000;
                var h=CreateFile(@"\\?\"+path,readAttributes,shareReadWrite,IntPtr.Zero,openExisting,backupSemantics|openReparse,IntPtr.Zero);
                if(h.IsInvalid) { var error=Error(); h.Dispose(); throw error; }
                handles.Add(h);
                if (!GetFileInformationByHandle(h,out var info)) throw Error();
                if ((info.Attributes&0x10)==0 || (info.Attributes&0x400)!=0) throw new IOException("Yedek yolunda güvenli olmayan bir klasör var.");
            }
            Pin(root);
            var current=root;
            var relative=Path.GetRelativePath(root,directory);
            if(relative!=".")
                foreach(var part in relative.Split([Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar],StringSplitOptions.RemoveEmptyEntries))
                {
                    current=Path.Combine(current,part);
                    Pin(current);
                }
            ProjectStorage.EnsureWritableDestination(source);
            ProjectStorage.EnsureWritableDestination(target);
            const uint genericRead=0x80000000, delete=0x00010000, shareRead=1, open=3, reparse=0x00200000;
            using var handle=CreateFile(@"\\?\"+Path.GetFullPath(source),genericRead|delete,shareRead,IntPtr.Zero,open,reparse,IntPtr.Zero);
            if(handle.IsInvalid) throw Error();
            if(FileIdentity.CaptureOwnedFile(handle)!=stamp) throw new IOException("Geçici yedek dosyası değişti.");
            using var stream=new FileStream(handle,FileAccess.Read,4096,isAsync:false);
            if(stream.Length!=length || !string.Equals(Convert.ToHexString(SHA256.HashData(stream)),hash,StringComparison.Ordinal))
                throw new IOException("Geçici yedek içeriği değişti.");
            afterLocked?.Invoke(source);
            var bytes=Encoding.Unicode.GetBytes(Path.GetFullPath(target));
            int rootOffset=IntPtr.Size==8?8:4, lengthOffset=rootOffset+IntPtr.Size, nameOffset=lengthOffset+4;
            int size=Math.Max(IntPtr.Size==8?24:16,checked(nameOffset+bytes.Length+2));
            var memory=Marshal.AllocHGlobal(size);
            try
            {
                for(var i=0;i<size;i++) Marshal.WriteByte(memory,i,0);
                Marshal.WriteInt32(memory,lengthOffset,bytes.Length);
                Marshal.Copy(bytes,0,IntPtr.Add(memory,nameOffset),bytes.Length);
                if(!SetFileInformationByHandle(handle,3,memory,(uint)size)) throw Error();
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        finally { for(var i=handles.Count-1;i>=0;i--) handles[i].Dispose(); }
    }
    // Pin the path and rename the verified directory handle itself. This does not
    // prevent concurrent modifications to the directory's contents.
    internal static void PublishDirectory(string source, string target, FileIdentity.OwnedDirectoryStamp stamp, Action verifyContents)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(target))!;
        var root = Path.GetPathRoot(directory);
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("Geri yükleme yolu güvenle doğrulanamadı.");
        var handles = new List<SafeFileHandle>();
        try
        {
            void Pin(string path)
            {
                const uint readAttributes = 0x80, shareReadWrite = 3, openExisting = 3, backup = 0x02000000, reparse = 0x00200000;
                var h = CreateFile(@"\\?\" + path, readAttributes, shareReadWrite, IntPtr.Zero, openExisting, backup | reparse, IntPtr.Zero);
                if (h.IsInvalid) { var error = Error(); h.Dispose(); throw error; }
                handles.Add(h);
                if (!GetFileInformationByHandle(h, out var info)) throw Error();
                if ((info.Attributes & 0x10) == 0 || (info.Attributes & 0x400) != 0)
                    throw new IOException("Geri yükleme yolunda güvenli olmayan bir klasör var.");
            }
            Pin(root);
            var current = root;
            var relative = Path.GetRelativePath(root, directory);
            if (relative != ".")
                foreach (var part in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
                {
                    current = Path.Combine(current, part);
                    Pin(current);
                }
            ProjectStorage.EnsureWritableDestination(Path.Combine(source, "restore-probe.tcad"));
            ProjectStorage.EnsureWritableDestination(Path.Combine(target, "restore-probe.tcad"));
            const uint delete = 0x00010000, readAttributes = 0x80, shareReadWrite = 3, open = 3, backup = 0x02000000, reparse = 0x00200000;
            using var handle = CreateFile(@"\\?\" + Path.GetFullPath(source), delete | readAttributes, shareReadWrite, IntPtr.Zero, open, backup | reparse, IntPtr.Zero);
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info)) throw Error();
            if ((info.Attributes & 0x10) == 0 || (info.Attributes & 0x400) != 0 ||
                new FileIdentity.OwnedDirectoryStamp(info.VolumeSerial, info.IndexHigh, info.IndexLow) != stamp)
                throw new IOException("Geçici geri yükleme klasörü değişti; proje yayımlanmadı.");
            verifyContents();
            var bytes = Encoding.Unicode.GetBytes(Path.GetFullPath(target));
            int rootOffset = IntPtr.Size == 8 ? 8 : 4, lengthOffset = rootOffset + IntPtr.Size, nameOffset = lengthOffset + 4;
            int size = Math.Max(IntPtr.Size == 8 ? 24 : 16, checked(nameOffset + bytes.Length + 2));
            var memory = Marshal.AllocHGlobal(size);
            try
            {
                for (var i = 0; i < size; i++) Marshal.WriteByte(memory, i, 0);
                Marshal.WriteInt32(memory, lengthOffset, bytes.Length);
                Marshal.Copy(bytes, 0, IntPtr.Add(memory, nameOffset), bytes.Length);
                if (!SetFileInformationByHandle(handle, 3, memory, (uint)size)) throw Error();
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        finally { for (var i = handles.Count - 1; i >= 0; i--) handles[i].Dispose(); }
    }
    private static IOException Error() => new("Yedek güvenle yayımlanamadı.",new Win32Exception(Marshal.GetLastWin32Error()));
}
