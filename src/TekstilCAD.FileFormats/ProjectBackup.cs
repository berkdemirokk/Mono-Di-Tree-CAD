using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace TekstilCAD.FileFormats;

/// <summary>Summary of successfully archived or restored project bytes.</summary>
public sealed record ProjectBackupSummary(int FileCount, long TotalBytes, IReadOnlyList<string> FileNames)
{
    public const string Scope = "Yalnız seçilen klasördeki üst düzey .tcad dosyaları; alt klasörler, diğer projeler, kurtarma, kuyruk ve ayarlar dahil değildir. Çok dosyalı anlık görüntü atomik değildir.";
}

/// <summary>Portable backup of top-level .tcad project files only; other data is excluded. Multi-file consistency is not atomic.</summary>
public static class ProjectBackup
{
    private const long MaxFile = 64L * 1024 * 1024, MaxTotal = 256L * 1024 * 1024;
    private const int MaxFiles = 1000;
    private sealed record Entry(string Name, long Length, string Sha256);
    private sealed record Manifest(int Version, Entry[] Files);

    public static void Create(string projectDirectory, string archivePath) => CreateWithSummary(projectDirectory, archivePath);

    /// <summary>Creates the backup and returns a summary only after successful publication.</summary>
    public static ProjectBackupSummary CreateWithSummary(string projectDirectory, string archivePath) =>
        CreateWithSummaryCore(projectDirectory, archivePath, null);

    // The callback belongs to this invocation only and is used by isolated tests
    // to exercise replacement between closing the staging file and publishing it.
    private static ProjectBackupSummary CreateWithSummaryCore(string projectDirectory, string archivePath, Action<string>? beforePublishForVerification)
        => CreateWithSummaryForVerification(projectDirectory, archivePath, beforePublishForVerification, null);

    private static ProjectBackupSummary CreateWithSummaryForVerification(string projectDirectory, string archivePath, Action<string>? beforePublishForVerification, Action<string>? afterLockedForVerification)
    {
        var source = Path.GetFullPath(projectDirectory);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        ProjectStorage.EnsureWritableDestination(Path.Combine(source, "backup-probe.tcad"));
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("Bağlantılı proje klasörü desteklenmiyor.");
        var files = Directory.GetFiles(source, "*.tcad", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) throw new InvalidDataException("Yedeklenecek proje bulunamadı.");
        if (files.Length > MaxFiles) throw new InvalidDataException("Yedek dosya sayısı sınırı aşıldı.");
        var entries = new List<Entry>(); long total = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var target = Path.GetFullPath(archivePath); ProjectStorage.EnsureWritableDestination(target);
        if (target.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Yedeği proje klasörü dışında oluşturun.");
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Yedek hedefi zaten mevcut.");
        var temporary = target + ".staging-" + Guid.NewGuid().ToString("N");
        ProjectStorage.EnsureWritableDestination(temporary);
        FileIdentity.OwnedFileStamp? ownedTemporary = null;
        string archiveHash;
        long archiveLength;
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            if (OperatingSystem.IsWindows()) ownedTemporary = FileIdentity.CaptureOwnedFile(output.SafeFileHandle);
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var file in files)
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Bağlantılı proje dosyası desteklenmiyor.");
                    var name = Path.GetFileName(file); ValidateName(name);
                    if (!names.Add(name)) throw new InvalidDataException("Yinelenen proje adı.");
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (stream.Length > MaxFile || total > MaxTotal - stream.Length) throw new InvalidDataException("Yedek boyut sınırı aşıldı.");
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long length = 0;
                    using (var item = zip.CreateEntry("projects/" + name).Open())
                    {
                        var chunk = new byte[81920]; int read;
                        while ((read = stream.Read(chunk, 0, chunk.Length)) != 0)
                        {
                            if (length > MaxFile - read || total > MaxTotal - length - read) throw new InvalidDataException("Yedek boyut sınırı aşıldı.");
                            item.Write(chunk, 0, read); hash.AppendData(chunk, 0, read); length += read;
                        }
                    }
                    total += length;
                    var digest = Convert.ToHexString(hash.GetHashAndReset());
                    // Bind validation to the exact bytes streamed into the archive.
                    var loaded = ProjectStorage.LoadWithVersion(file);
                    if (!string.Equals(loaded.Version.Sha256, digest, StringComparison.Ordinal)) throw new IOException("Proje yedekleme sırasında değişti.");
                    entries.Add(new Entry(name, length, digest));
                }
                using var manifest = zip.CreateEntry("manifest.json").Open(); JsonSerializer.Serialize(manifest, new Manifest(1, entries.ToArray()));
            }
            VerifySourceUnchanged(source, entries.Select(e => e.Name).ToArray(), entries.Select(e => e.Sha256).ToArray());
            output.Flush(true);
            archiveLength = output.Length;
            output.Position = 0;
            archiveHash = HashBounded(output, archiveLength);
        }
        // Failed staging paths remain untouched for inspection.
        beforePublishForVerification?.Invoke(temporary);
        if (OperatingSystem.IsWindows())
        {
            if (ownedTemporary is not { } stamp) throw new IOException("Geçici yedek dosyası doğrulanamadı.");
            ProjectBackupAtomic.Publish(temporary, target, stamp, archiveLength, archiveHash, afterLockedForVerification);
        }
        else
        {
            ProjectStorage.EnsureWritableDestination(temporary);
            ProjectStorage.EnsureWritableDestination(target);
            using (var staged = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
                if (staged.Length != archiveLength || !string.Equals(HashBounded(staged, archiveLength), archiveHash, StringComparison.Ordinal))
                    throw new IOException("Geçici yedek içeriği değişti; yedek oluşturulmadı.");
            File.Move(temporary, target, false);
        }
        return new ProjectBackupSummary(entries.Count, entries.Sum(e => e.Length), Array.AsReadOnly(entries.Select(e => e.Name).ToArray()));
    }

    private static void VerifySourceUnchanged(string source, string[] names, string[] hashes)
    {
        var current = Directory.GetFiles(source, "*.tcad", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!current.SequenceEqual(names, StringComparer.OrdinalIgnoreCase)) throw new IOException("Proje listesi yedekleme sırasında değişti.");
        for (var i = 0; i < names.Length; i++)
        {
            var file = Path.Combine(source, names[i]);
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Proje dosyası bağlantıya dönüştü.");
            using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > MaxFile || !string.Equals(HashBounded(input, MaxFile), hashes[i], StringComparison.Ordinal)) throw new IOException("Proje yedekleme sırasında değişti.");
        }
    }

    // Length can change after it is inspected when a non-cooperating writer already
    // holds a compatible handle. Bound the bytes actually consumed as well.
    private static string HashBounded(Stream input, long limit)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long count = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (count > limit - read) throw new IOException("Proje yedekleme sırasında boyut sınırını aştı.");
            hash.AppendData(buffer, 0, read);
            count += read;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static void Restore(string archivePath, string destinationDirectory) => RestoreWithSummary(archivePath, destinationDirectory);

    /// <summary>Restores to a new directory and returns a summary only after successful publication.</summary>
    public static ProjectBackupSummary RestoreWithSummary(string archivePath, string destinationDirectory) =>
        RestoreWithSummaryCore(archivePath, destinationDirectory, null);

    // Per-call boundary hook for isolated replacement tests; never global.
    private static ProjectBackupSummary RestoreWithSummaryCore(string archivePath, string destinationDirectory, Action<string>? beforePublishForVerification)
    {
        var target = Path.GetFullPath(destinationDirectory);
        ProjectStorage.EnsureWritableDestination(Path.Combine(target, "restore-probe.tcad"));
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Geri yükleme hedefi mevcut olmamalıdır.");
        using var zip = ZipFile.OpenRead(Path.GetFullPath(archivePath));
        if (zip.Entries.Count > MaxFiles + 1) throw new InvalidDataException("Yedek dosya sayısı sınırı aşıldı.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in zip.Entries) if (!seen.Add(item.FullName) || item.FullName.Contains('\\') || item.FullName.StartsWith('/') || item.FullName.Contains(':') || item.FullName.Split('/').Any(x => x is ".." or "." or "")) throw new InvalidDataException("Güvensiz yedek yolu.");
        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Yedek manifesti yok.");
        if (manifestEntry.Length > 1024 * 1024) throw new InvalidDataException("Manifest çok büyük.");
        Manifest manifest;
        try
        {
            using var reader = manifestEntry.Open();
            using var document = JsonDocument.Parse(ReadManifestBytes(reader, manifestEntry.Length));
            RejectDuplicateManifestProperties(document.RootElement);
            manifest = document.RootElement.Deserialize<Manifest>() ?? throw new InvalidDataException("Geçersiz manifest.");
        }
        catch (JsonException ex)
        {
            // Keep the public exception type stable across JSON parser implementations.
            throw new JsonException("Yedek manifesti geçersiz.", ex);
        }
        if (manifest.Version != 1 || manifest.Files is null || manifest.Files.Length == 0 || manifest.Files.Length > MaxFiles || zip.Entries.Count != manifest.Files.Length + 1) throw new InvalidDataException("Desteklenmeyen veya boş yedek içeriği.");
        var restored = new List<Entry>(); long total = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meta in manifest.Files)
        {
            if (meta is null) throw new InvalidDataException("Geçersiz yedek kaydı."); ValidateName(meta.Name);
            if (!names.Add(meta.Name) || meta.Length < 0 || meta.Length > MaxFile || (total += meta.Length) > MaxTotal) throw new InvalidDataException("Geçersiz yedek boyutu veya adı.");
            var entry = zip.GetEntry("projects/" + meta.Name) ?? throw new InvalidDataException("Proje yedekte eksik.");
            if (entry.Length != meta.Length) throw new InvalidDataException("Proje boyutu uyuşmuyor.");
            using var stream = entry.Open(); using var buffer = new MemoryStream(); var chunk = new byte[81920]; int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) != 0) { if (buffer.Length + read > meta.Length) throw new InvalidDataException("Proje boyutu uyuşmuyor."); buffer.Write(chunk, 0, read); }
            var bytes = buffer.ToArray();
            if (bytes.LongLength != meta.Length || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), meta.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Proje özeti uyuşmuyor.");
            restored.Add(meta);
        }
        var stage = target + ".staging-" + Guid.NewGuid().ToString("N");
        ProjectStorage.EnsureWritableDestination(Path.Combine(stage, "restore-probe.tcad"));
        CreateOwnedDirectory(stage);
        var ownedStage = OperatingSystem.IsWindows() ? FileIdentity.CaptureOwnedDirectory(stage) : (FileIdentity.OwnedDirectoryStamp?)null;
        {
            foreach (var meta in restored)
            {
                if (OperatingSystem.IsWindows() && (ownedStage is not { } current || !FileIdentity.IsOwnedDirectory(stage, current)))
                    throw new IOException("Geçici geri yükleme klasörü değişti; proje yayımlanmadı.");
                var file = Path.Combine(stage, meta.Name); ProjectStorage.EnsureWritableDestination(file);
                using (var input = zip.GetEntry("projects/" + meta.Name)!.Open()) using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { var chunk = new byte[81920]; long written = 0; int read; while ((read = input.Read(chunk, 0, chunk.Length)) != 0) { if (written > meta.Length - read) throw new InvalidDataException("Proje boyutu uyuşmuyor."); output.Write(chunk, 0, read); written += read; } if (written != meta.Length) throw new InvalidDataException("Proje boyutu uyuşmuyor."); output.Flush(true); }
                var staged = ProjectStorage.LoadWithVersion(file);
                if (!string.Equals(staged.Version.Sha256, meta.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Geri yüklenen proje özeti uyuşmuyor.");
            }
            beforePublishForVerification?.Invoke(stage);
            ProjectStorage.EnsureWritableDestination(Path.Combine(stage, "restore-probe.tcad"));
            ProjectStorage.EnsureWritableDestination(Path.Combine(target, "restore-probe.tcad"));
            if (OperatingSystem.IsWindows() && (ownedStage is not { } stamp || !FileIdentity.IsOwnedDirectory(stage, stamp)))
                throw new IOException("Geçici geri yükleme klasörü değişti; proje yayımlanmadı.");
            if (OperatingSystem.IsWindows())
            {
                if (ownedStage is not { } finalStamp) throw new IOException("Geçici geri yükleme klasörü doğrulanamadı.");
                ProjectBackupAtomic.PublishDirectory(stage, target, finalStamp, () => VerifyRestoreStage(stage, restored));
            }
            else
            {
                VerifyRestoreStage(stage, restored);
                Directory.Move(stage, target); // Requires an absent destination; never replaces it.
            }
        }
        // On failure retain the uniquely created stage for inspection. Never delete paths
        // which another process could have replaced while restoration was running.
        return new ProjectBackupSummary(restored.Count, restored.Sum(e => e.Length), Array.AsReadOnly(restored.Select(e => e.Name).ToArray()));
    }

    private static byte[] ReadManifestBytes(Stream input, long declaredLength)
    {
        const int limit = 1024 * 1024;
        if (declaredLength < 0 || declaredLength > limit) throw new InvalidDataException("Manifest çok büyük.");
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = input.Read(chunk, 0, chunk.Length)) != 0)
        {
            if (buffer.Length > limit - read || buffer.Length > declaredLength - read)
                throw new InvalidDataException("Manifest boyutu uyuşmuyor.");
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length != declaredLength) throw new InvalidDataException("Manifest boyutu uyuşmuyor.");
        return buffer.ToArray();
    }

    private static void RejectDuplicateManifestProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Manifest yinelenen alan içeriyor.");
                RejectDuplicateManifestProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectDuplicateManifestProperties(item);
        }
    }

    // Validation performed during extraction can become stale before publication.
    // Check the entire staging directory again after the publication boundary hook.
    // This is a bounded snapshot check, not a lock against non-cooperating writers.
    private static void VerifyRestoreStage(string stage, IReadOnlyList<Entry> expected)
    {
        var expectedByName = expected.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(stage))
        {
            if (seen.Count >= MaxFiles) throw new IOException("Geri yükleme klasöründe beklenmeyen dosyalar var.");
            var name = Path.GetFileName(path);
            if (!seen.Add(name) || !expectedByName.TryGetValue(name, out var meta))
                throw new IOException("Geri yükleme klasöründe beklenmeyen dosya var.");
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("Geri yükleme dosyası bağlantıya veya klasöre dönüştü.");
            if (OperatingSystem.IsWindows()) FileIdentity.RejectMultipleLinksForReplacement(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != meta.Length || total > MaxTotal - meta.Length)
                throw new IOException("Geri yüklenen proje boyutu değişti.");
            total += meta.Length;
            if (!string.Equals(HashBounded(stream, MaxFile), meta.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Geri yüklenen proje içeriği değişti.");
        }
        if (seen.Count != expectedByName.Count) throw new IOException("Geri yüklenen proje eksik.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, IntPtr securityAttributes);
    [DllImport("libc", SetLastError = true, EntryPoint = "mkdir")]
    private static extern int Mkdir(string path, uint mode);
    private static void CreateOwnedDirectory(string path)
    {
        // Unlike Directory.CreateDirectory this fails if the name already exists, even
        // if a concurrent process creates it immediately before this call.
        bool created = OperatingSystem.IsWindows() ? CreateDirectoryW(path, IntPtr.Zero) : Mkdir(path, 448) == 0;
        if (!created) throw new IOException("Geri yükleme için yeni geçici klasör oluşturulamadı.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
    }
    private static void ValidateName(string? name)
    {
        // Use Windows' forbidden characters explicitly: Path.GetInvalidFileNameChars()
        // depends on the host OS, but archives must be portable to Windows as well.
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name != Path.GetFileName(name) || name.EndsWith(' ') || name.EndsWith('.') || !name.EndsWith(".tcad", StringComparison.OrdinalIgnoreCase) || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))) throw new InvalidDataException("Geçersiz proje dosyası adı.");
        var stem = name.Split('.')[0].TrimEnd(' ', '.');
        if (System.Text.RegularExpressions.Regex.IsMatch(stem, "^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new InvalidDataException("Ayrılmış proje dosyası adı.");
    }
}
