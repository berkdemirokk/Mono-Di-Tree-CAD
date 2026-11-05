using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

// No caller-supplied paths. Open each directory itself, reject reparse points,
// and deny deletion/rename while creating the new file. Fail closed elsewhere.
internal static class SafeResearchOutput
{
    private const string Root = @"C:\Projects\TekstilCAD\work\synthetic-svg";
    private const string Output = Root + @"\experiments\synthetic-svg\test-output";
    private const uint ReadAttributes = 0x80, ShareRead = 1, ShareWrite = 2;
    private const uint OpenExisting = 3, BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
    private const uint DirectoryAttribute = 0x10, ReparseAttribute = 0x400;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleInfo info);
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleInfo
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    internal static string WriteNew(string id, string svg)
    {
        if (!OperatingSystem.IsWindows() || id is not ("synthetic-base" or "synthetic-distinct" or "synthetic-third"))
            throw new InvalidOperationException("Unsupported research output environment.");
        // The root is explicit and is not derived from the binary location. No
        // directory is created: absence of the prepared test directory fails.
        string[] paths = [@"C:\", @"C:\Projects", @"C:\Projects\TekstilCAD", @"C:\Projects\TekstilCAD\work", Root,
            Root + @"\experiments", Root + @"\experiments\synthetic-svg", Output];
        var held = new List<SafeFileHandle>();
        try
        {
            foreach (string path in paths)
            {
                var handle = CreateFileW(path, ReadAttributes, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Cannot lock research output directory."); }
                held.Add(handle);
                if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & DirectoryAttribute) == 0 || (info.Attributes & ReparseAttribute) != 0)
                    throw new IOException("Research output directory is invalid or redirected.");
            }
            string file = Path.Combine(Output, $"{id}-{Guid.NewGuid():N}.svg");
            using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(svg);
            return file;
        }
        finally { foreach (var handle in held) handle.Dispose(); }
    }
}
