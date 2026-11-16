using System.Reflection;

namespace TekstilCAD.Desktop;

/// <summary>Version facts read from the built assembly, shown in the About window and crash logs.</summary>
internal static class AppInfo
{
    public const string ProductName = "Mono Di Tree CAD";

    /// <summary>Numeric version such as 0.4.0, without the source revision suffix.</summary>
    public static string DisplayVersion { get; }

    /// <summary>Short source revision when the build recorded one; null for builds without Git metadata.</summary>
    public static string? SourceRevision { get; }

    static AppInfo()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        (DisplayVersion, SourceRevision) = Parse(informational, assembly.GetName().Version);
    }

    internal static (string Version, string? Revision) Parse(string? informational, Version? fallback)
    {
        var fallbackText = fallback is null ? "bilinmiyor" : fallback.ToString(3);
        if (string.IsNullOrWhiteSpace(informational)) return (fallbackText, null);
        var plus = informational.IndexOf('+');
        var version = (plus < 0 ? informational : informational[..plus]).Trim();
        if (version.Length == 0) version = fallbackText;
        if (plus < 0) return (version, null);
        var revision = informational[(plus + 1)..].Trim();
        // Only a hexadecimal Git revision is shown; other build metadata is not guessed at. A value
        // made of digits alone is more likely a date or a build counter than a revision.
        if (revision.Length < 7 || !revision.All(Uri.IsHexDigit) || !revision.Any(char.IsLetter)) return (version, null);
        return (version, revision[..Math.Min(12, revision.Length)].ToLowerInvariant());
    }
}
