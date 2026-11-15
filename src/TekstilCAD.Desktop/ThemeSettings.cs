using System.IO;
using System.Text.Json;

namespace TekstilCAD.Desktop;

/// <summary>
/// The saved colour theme: a tiny JSON file in the application data folder. It is read only by the interactive
/// start path (<c>App.OnStartup</c>) and written only when the user picks a theme, so the headless verification
/// and every <c>MainWindow</c> constructor are independent of it. A missing, unreadable or corrupt file means Dark.
/// </summary>
internal static class ThemeSettings
{
    internal const string FileName = "theme.json";
    private const int MaxBytes = 4096;

    internal static string FilePath => Path.Combine(AppPaths.DataDirectory, FileName);

    private sealed class Stored { public string? Theme { get; set; } }

    /// <summary>Reads the saved theme; never throws. Anything unexpected gives <see cref="ThemeKind.Dark"/>.</summary>
    internal static ThemeKind Load(string? path = null)
    {
        try
        {
            path ??= FilePath;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes) return ThemeKind.Dark;
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
            return string.Equals(stored?.Theme, nameof(ThemeKind.Light), StringComparison.Ordinal) ? ThemeKind.Light : ThemeKind.Dark;
        }
        catch (Exception) { return ThemeKind.Dark; }
    }

    /// <summary>Writes the theme through a temporary file so a crash never leaves a half-written file. Returns false on failure.</summary>
    internal static bool Save(ThemeKind kind, string? path = null)
    {
        string? temporary = null;
        try
        {
            path ??= FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Stored { Theme = kind.ToString() }));
            File.Move(temporary, path, overwrite: true);
            temporary = null;
            return true;
        }
        catch (Exception) { return false; }
        finally
        {
            if (temporary is not null) { try { File.Delete(temporary); } catch (Exception) { } }
        }
    }
}
