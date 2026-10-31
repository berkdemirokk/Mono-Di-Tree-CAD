using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TekstilCAD.FileFormats;

/// <summary>Compares existing Windows file identities; it is not a lock against subsequent path changes.</summary>
public static class FileIdentity
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);

    [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern SafeFileHandle OpenFileNoFollow(string path, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr template);

    // Open the directory entry itself, not a symlink target. File.OpenHandle
    // follows file reparse points and can otherwise return a matching ID for
    // a symlink that points at the original file after a path swap.
    private static SafeFileHandle? OpenExistingNoFollow(string path)
    {
        var full=Path.GetFullPath(path);
        if (full.StartsWith(@"\\",StringComparison.Ordinal))
            throw new IOException("Yerel dosya kimliği doğrulanamadı.");
        const uint readAttributes=0x80, shareAll=1|2|4, openExisting=3;
        const uint openReparsePoint=0x00200000, backupSemantics=0x02000000;
        var handle=OpenFileNoFollow(@"\\?\"+full,readAttributes,shareAll,IntPtr.Zero,openExisting,openReparsePoint|backupSemantics,IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error=Marshal.GetLastWin32Error(); handle.Dispose();
        if (error is 2 or 3) return null;
        // Preserve the Win32 HRESULT so callers can distinguish an exclusively
        // locked live recovery lease from an inaccessible or corrupt record.
        throw new IOException("Dosya kimliği güvenle açılamadı.",unchecked((int)(0x80070000u | (uint)error)));
    }

    /// <summary>Identity of an owned Windows temporary file, not a lock on its path.</summary>
    public readonly record struct OwnedFileStamp(uint VolumeSerial, uint IndexHigh, uint IndexLow);

    internal readonly record struct OwnedDirectoryStamp(uint VolumeSerial, uint IndexHigh, uint IndexLow);

    // Capture the directory entry itself. Directory handles need backup semantics;
    // OpenExistingNoFollow also ensures a junction is not followed.
    internal static OwnedDirectoryStamp CaptureOwnedDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var handle = OpenExistingNoFollow(path) ?? throw new IOException("Geçici klasör bulunamadı.");
        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException("Geçici klasörün kimliği doğrulanamadı.", new Win32Exception(Marshal.GetLastWin32Error()));
        const uint directory = 0x10, reparse = 0x400;
        if ((info.Attributes & directory) == 0 || (info.Attributes & reparse) != 0)
            throw new IOException("Geçici klasörün sahipliği doğrulanamadı.");
        return new OwnedDirectoryStamp(info.VolumeSerial, info.IndexHigh, info.IndexLow);
    }

    internal static bool IsOwnedDirectory(string path, OwnedDirectoryStamp stamp) => CaptureOwnedDirectory(path) == stamp;

    /// <summary>Capture the identity of an already-open, single-link regular temporary file.</summary>
    public static OwnedFileStamp CaptureOwnedFile(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Dosya kimliği kontrolü Windows gerektirir.");
        ArgumentNullException.ThrowIfNull(handle);
        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException("Geçici dosyanın kimliği doğrulanamadı.", new Win32Exception(Marshal.GetLastWin32Error()));
        const uint directoryOrReparse = 0x10 | 0x400;
        if ((info.Attributes & directoryOrReparse) != 0 || info.LinkCount != 1)
            throw new IOException("Geçici dosyanın sahipliği doğrulanamadı.");
        return new OwnedFileStamp(info.VolumeSerial, info.IndexHigh, info.IndexLow);
    }

    /// <summary>Checks the current path still identifies the captured single-link file.
    /// Another process can still replace the path after this check.</summary>
    public static bool IsOwnedFile(string path, OwnedFileStamp stamp)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Dosya kimliği kontrolü Windows gerektirir.");
        using var handle=OpenExistingNoFollow(path);
        return handle is not null && CaptureOwnedFile(handle)==stamp;
    }

    /// <summary>Reject mutation of a multiply linked existing file. A different hard link may
    /// belong to a protected installation. This check does not lock the path against later replacement.</summary>
    public static void RejectMultipleLinksForReplacement(string destination)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Dosya bağlantı sayısı kontrolü Windows gerektirir.");
        var handle=OpenExistingNoFollow(destination);
        if (handle is null) return;
        using (handle)
        {
            if (!GetFileInformationByHandle(handle, out var info))
                throw new IOException("Hedef dosyanın bağlantı sayısı doğrulanamadı; kayıt durduruldu.", new Win32Exception(Marshal.GetLastWin32Error()));
            if ((info.Attributes & (0x10 | 0x400)) != 0 || info.LinkCount != 1)
                throw new IOException("Birden çok sabit bağlantısı olan dosya değiştirilemez; farklı bir dosya seçin.");
        }
    }

    /// <summary>Returns false for an absent destination. An unreadable source or destination fails closed.</summary>
    public static bool SameExistingFile(string source, string destination)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Dosya kimliği kontrolü Windows gerektirir.");
        using var sourceHandle = File.OpenHandle(Path.GetFullPath(source), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        SafeFileHandle destinationHandle;
        try { destinationHandle = File.OpenHandle(Path.GetFullPath(destination), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        using (destinationHandle)
        {
            if (!GetFileInformationByHandle(sourceHandle, out var first)) throw new IOException("Açık projenin dosya kimliği okunamadı.", new Win32Exception(Marshal.GetLastWin32Error()));
            if (!GetFileInformationByHandle(destinationHandle, out var second)) throw new IOException("Hedef dosyanın kimliği okunamadı.", new Win32Exception(Marshal.GetLastWin32Error()));
            return first.VolumeSerial == second.VolumeSerial && first.IndexHigh == second.IndexHigh && first.IndexLow == second.IndexLow;
        }
    }
}
