using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TekstilCAD.MachineGateway;

/// <summary>
/// Experimental Windows-only atomic directory creation. The open handle pins
/// the created directory and the validated ancestors until disposal. This is
/// not yet integrated into SimulationQueue and is not a crash-durability or
/// defense against privileged kernel/volume manipulation guarantee.
/// </summary>
public sealed class AtomicJobDirectory : IDisposable
{
    private const uint ReadAttributes = 0x80, AddSubdirectory = 0x4;
    private const uint ChildAccess = 0x80 | 0x1 | 0x2 | 0x4 | 0x00100000;
    private const uint ShareRead = 1, OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
    private const uint FileCreate = 2, FileDirectoryFile = 1, FileSynchronousNonAlert = 0x20;
    private const ulong FileCreated = 2;
    private readonly List<SafeFileHandle> ancestors = [];
    private SafeFileHandle? child;
    private bool disposed;
    public string DirectoryPath { get; private set; } = "";
    public bool IsOpen => !disposed && child is { IsInvalid: false, IsClosed: false };
    internal SafeFileHandle DirectoryHandle => child ?? throw new ObjectDisposedException(nameof(AtomicJobDirectory));
    private AtomicJobDirectory() { }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public uint Length; public IntPtr RootDirectory; public IntPtr ObjectName;
        public uint Attributes; public IntPtr SecurityDescriptor; public IntPtr SecurityQualityOfService;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock { public IntPtr Status; public UIntPtr Information; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(out IntPtr file, uint access, ref ObjectAttributes attributes,
        out IoStatusBlock status, IntPtr allocationSize, uint fileAttributes, uint shareAccess,
        uint createDisposition, uint createOptions, IntPtr extendedAttributes, uint extendedAttributesLength);

    /// <summary>
    /// Atomically creates one GUID-named child of an existing, local directory.
    /// Existing names, inaccessible roots, reparse points, or unverified handles
    /// fail closed. Partial created directories are deliberately never deleted.
    /// </summary>
    public static AtomicJobDirectory Create(string parentDirectory, Guid jobId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Atomik iş klasörü yalnız Windows üzerinde desteklenir.");
        if (jobId == Guid.Empty) throw new ArgumentException("Geçerli iş kimliği gerekli.", nameof(jobId));
        var parent = SimulationQueue.ValidateOutputDirectory(parentDirectory);
        var root = Path.GetPathRoot(parent) ?? throw new IOException("Klasör kökü doğrulanamadı.");
        if (root.StartsWith(@"\\", StringComparison.Ordinal) || root.Length != 3 || root[1] != ':' || root[2] != '\\')
            throw new IOException("Yerel sürücü yolu gerekli.");
        var instance = new AtomicJobDirectory();
        try
        {
            var components = parent[root.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(part => part.Length != 0).ToArray();
            var current = root;
            instance.OpenAncestor(current, components.Length == 0);
            for (int i = 0; i < components.Length; i++)
            {
                current = Path.Combine(current, components[i]);
                instance.OpenAncestor(current, i == components.Length - 1);
            }
            var name = jobId.ToString("N");
            instance.CreateChild(name);
            instance.DirectoryPath = Path.Combine(parent, name);
            return instance;
        }
        catch { instance.Dispose(); throw; }
    }
    private void OpenAncestor(string path, bool isParent)
    {
        // No write or delete sharing: another handle requesting directory
        // metadata mutation or rename cannot coexist with this guard.
        var handle = CreateFileW(@"\\?\" + path, ReadAttributes | (isParent ? AddSubdirectory : 0), ShareRead,
            IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastWin32Error(); handle.Dispose();
            throw new IOException("İş klasörünün üst yolu güvenli açılamadı.", new Win32Exception(code));
        }
        try { ValidateDirectoryHandle(handle); ancestors.Add(handle); }
        catch { handle.Dispose(); throw; }
    }
    private static void ValidateDirectoryHandle(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException("Klasör tanıtıcısı doğrulanamadı.", new Win32Exception(Marshal.GetLastWin32Error()));
        const uint directoryFlag = 0x10, reparseFlag = 0x400;
        if ((info.Attributes & directoryFlag) == 0 || (info.Attributes & reparseFlag) != 0)
            throw new IOException("Klasör veya üst yolu yönlendirilmiş; işlem yapılmadı.");
    }
    private void CreateChild(string name)
    {
        var buffer = Marshal.StringToHGlobalUni(name);
        var unicodePointer = IntPtr.Zero;
        try
        {
            var unicode = new UnicodeString { Length = checked((ushort)(name.Length * 2)), MaximumLength = checked((ushort)(name.Length * 2)), Buffer = buffer };
            unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(unicode, unicodePointer, false);
            var attributes = new ObjectAttributes
            {
                Length = (uint)Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = ancestors[^1].DangerousGetHandle(), ObjectName = unicodePointer,
                Attributes = 0x40 // OBJ_CASE_INSENSITIVE
            };
            int status = NtCreateFile(out var raw, ChildAccess, ref attributes, out var io, IntPtr.Zero,
                0, ShareRead, FileCreate, FileDirectoryFile | FileSynchronousNonAlert, IntPtr.Zero, 0);
            if (status < 0)
                throw new IOException($"İş klasörü oluşturulamadı (NTSTATUS 0x{unchecked((uint)status):X8}); mevcut klasöre dokunulmadı.");
            if (raw == IntPtr.Zero || raw == new IntPtr(-1)) throw new IOException("Yeni klasör tanıtıcısı geçersiz.");
            child = new SafeFileHandle(raw, ownsHandle: true);
            if (io.Information.ToUInt64() != FileCreated)
                throw new IOException("Yeni klasör oluşturulduğu doğrulanamadı; klasör korundu.");
            ValidateDirectoryHandle(child);
        }
        finally
        {
            if (unicodePointer != IntPtr.Zero) Marshal.FreeHGlobal(unicodePointer);
            Marshal.FreeHGlobal(buffer);
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        child?.Dispose(); child = null;
        for (int i = ancestors.Count - 1; i >= 0; i--) ancestors[i].Dispose();
        ancestors.Clear();
    }
}
