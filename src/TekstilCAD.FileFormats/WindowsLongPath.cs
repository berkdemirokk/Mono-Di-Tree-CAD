using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TekstilCAD.FileFormats;

/// <summary>Expands an existing local Windows path's 8.3 components; never creates files.</summary>
internal static class WindowsLongPath
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetLongPathNameW")]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint capacity);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryDosDeviceW")]
    private static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int maximumLength);

    // A SUBST/DefineDosDevice alias can map an apparently local drive into a
    // protected directory while its long path remains rooted at that alias.
    // Accept only ordinary local volume devices; reject uncertain mappings.
    internal static bool IsOrdinaryVolumeMapping(string mapping) =>
        Regex.IsMatch(mapping, @"^\\Device\\HarddiskVolume[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string RequireOrdinaryDrive(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (root is null || root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\')
            throw new IOException("Yerel disk yolu doğrulanamadı; kayıt durduruldu.");
        var buffer = new StringBuilder(32768);
        if (QueryDosDevice(root[..2], buffer, buffer.Capacity) == 0)
            throw new IOException("Disk eşlemesi doğrulanamadı; kayıt durduruldu.", new Win32Exception(Marshal.GetLastWin32Error()));
        var mapping=buffer.ToString();
        if (!IsOrdinaryVolumeMapping(mapping))
            throw new IOException("Yönlendirilmiş veya desteklenmeyen disk yoluna yazılamaz; normal yerel disk seçin.");
        return mapping;
    }

    // Multiple drive letters can point directly to the same volume device.
    // Compare their volume-relative paths as well as their textual drive paths.
    internal static bool IsProtectedOnSameVolume(string candidate, string candidateDevice, string root, string rootDevice)
    {
        if (!string.Equals(candidateDevice,rootDevice,StringComparison.OrdinalIgnoreCase)) return false;
        var normalizedCandidate=Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedRoot=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (normalizedCandidate.Length < 3 || normalizedRoot.Length < 3 || normalizedCandidate[1] != ':' || normalizedRoot[1] != ':')
            throw new IOException("Korumalı disk yolu doğrulanamadı; kayıt durduruldu.");
        var relativeCandidate=normalizedCandidate[2..];
        var relativeRoot=normalizedRoot[2..];
        return relativeCandidate.Equals(relativeRoot,StringComparison.OrdinalIgnoreCase) ||
               relativeCandidate.StartsWith(relativeRoot + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }

    internal static string ResolveExisting(string path)
    {
        var full = Path.GetFullPath(path);
        // The caller has already rejected UNC and device paths. Extended syntax
        // permits resolving long local paths without altering the user's path.
        var extended = @"\\?\" + full;
        var capacity = Math.Max(260, extended.Length + 1);
        while (true)
        {
            var output = new StringBuilder(capacity);
            var length = GetLongPathName(extended, output, (uint)capacity);
            if (length == 0) throw new IOException("Klasörün gerçek uzun yolu doğrulanamadı; kayıt durduruldu.", new Win32Exception(Marshal.GetLastWin32Error()));
            if (length < capacity)
            {
                var result = output.ToString();
                if (!result.StartsWith(@"\\?\", StringComparison.Ordinal) || result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Klasörün yerel uzun yolu doğrulanamadı; kayıt durduruldu.");
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(result[4..]));
            }
            if (length >= 32767) throw new IOException("Klasör yolu doğrulanamayacak kadar uzun.");
            capacity = checked((int)length + 1);
        }
    }
}
