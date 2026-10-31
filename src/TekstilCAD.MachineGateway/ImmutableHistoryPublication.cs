using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TekstilCAD.FileFormats;

namespace TekstilCAD.MachineGateway;

// Windows-only publication of the exact open file. Directory handles deny
// delete-sharing while held, so path ancestors cannot be renamed or replaced.
internal sealed class ImmutableHistoryPublication : IDisposable
{
    private const uint ReadAttributes = 0x80, GenericWrite = 0x40000000, Delete = 0x10000;
    private const uint ShareRead = 1, ShareWrite = 2, OpenExisting = 3, CreateNew = 1;
    private const uint BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
    private readonly List<SafeFileHandle> directories = [];
    private readonly string directory;
    private ImmutableHistoryPublication(string path) { directory = path; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, IntPtr information, uint size);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RenameLayout
    {
        public byte ReplaceIfExists;
        public IntPtr RootDirectory;
        public uint FileNameLength;
        public char FileName;
    }
    private static IOException Failure(string message) => new(message, new Win32Exception(Marshal.GetLastWin32Error()));

    internal static ImmutableHistoryPublication Open(string directory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Güvenli sürüm yayını Windows gerektirir.");
        var guard = new ImmutableHistoryPublication(directory);
        try
        {
            var root = Path.GetPathRoot(directory) ?? throw new IOException("Klasör kökü doğrulanamadı.");
            string current = root;
            guard.OpenDirectory(current);
            foreach (var component in directory[root.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (component.Length == 0) continue;
                current = Path.Combine(current, component);
                guard.OpenDirectory(current);
            }
            return guard;
        }
        catch { guard.Dispose(); throw; }
    }
    private void OpenDirectory(string path)
    {
        var handle = CreateFileW(path, ReadAttributes, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw Failure("Klasör kilitlenemedi."); }
        if (!GetFileInformationByHandle(handle, out var info)) { handle.Dispose(); throw Failure("Klasör kimliği okunamadı."); }
        const uint directoryAttribute = 0x10, reparseAttribute = 0x400;
        if ((info.Attributes & directoryAttribute) == 0 || (info.Attributes & reparseAttribute) != 0)
        { handle.Dispose(); throw new IOException("Klasör veya üst yolu yönlendirilmiş; yayın yapılmadı."); }
        directories.Add(handle);
    }
    internal void Publish(string tempName, string targetName, byte[] bytes, Action? afterWrite, CancellationToken token)
    {
        var tempPath = Path.Combine(directory, tempName);
        using var handle = CreateFileW(tempPath, GenericWrite | Delete, 0, IntPtr.Zero, CreateNew, OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) throw Failure("Geçici dosya oluşturulamadı.");
        using var stream = new FileStream(handle, FileAccess.Write, 4096, false);
        var stamp = FileIdentity.CaptureOwnedFile(handle);
        stream.Write(bytes); stream.Flush(true);
        afterWrite?.Invoke();
        token.ThrowIfCancellationRequested();
        if (FileIdentity.CaptureOwnedFile(handle) != stamp) throw new IOException("Geçici dosya kimliği değişti; yayın yapılmadı.");
        // The Win32 wrapper accepts an absolute destination with RootDirectory
        // null. Every ancestor remains held without delete sharing.
        var targetPath = Path.Combine(directory, targetName);
        var name = Encoding.Unicode.GetBytes(targetPath);
        int nameOffset = checked((int)Marshal.OffsetOf<RenameLayout>(nameof(RenameLayout.FileName)));
        // Include a zero UTF-16 terminator in the buffer, while FileNameLength
        // remains the byte length of the path itself.
        int size = Math.Max(Marshal.SizeOf<RenameLayout>(), checked(nameOffset + name.Length + sizeof(char)));
        IntPtr data = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, data, size);
            // RootDirectory stays null; non-null handles are rejected by the
            // Win32 wrapper on the tested Windows configuration.
            Marshal.WriteInt32(data, checked((int)Marshal.OffsetOf<RenameLayout>(nameof(RenameLayout.FileNameLength))), name.Length);
            Marshal.Copy(name, 0, IntPtr.Add(data, nameOffset), name.Length);
            // FileRenameInfo (3), ReplaceIfExists remains FALSE.
            if (!SetFileInformationByHandle(handle, 3, data, (uint)size)) throw Failure("Sürüm dosyası yayımlanamadı; geçici dosya korundu.");
        }
        finally { Marshal.FreeHGlobal(data); }
    }
    public void Dispose() { for (var i = directories.Count - 1; i >= 0; i--) directories[i].Dispose(); directories.Clear(); }
}

