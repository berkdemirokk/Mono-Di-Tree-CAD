namespace TekstilCAD.Core.Theme;

/// <summary>The recent-files list kept in <see cref="UiPreferences"/>: newest first, no duplicates, a fixed size. Pure list logic.</summary>
public static class RecentProjects
{
    /// <summary>Puts a path first (moving an existing entry), keeping at most <see cref="UiPreferences.MaximumRecent"/> entries.</summary>
    public static IReadOnlyList<string> Add(IEnumerable<string> current, string path)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (string.IsNullOrWhiteSpace(path)) return Normalize(current);
        return Normalize(current.Prepend(path));
    }

    /// <summary>Removes an entry (for example a file that no longer exists).</summary>
    public static IReadOnlyList<string> Remove(IEnumerable<string> current, string path)
    {
        ArgumentNullException.ThrowIfNull(current);
        return Normalize(current.Where(entry => !Same(entry, path)));
    }

    /// <summary>Drops blank and over-long entries and duplicates (Windows paths compare without regard to case) and caps the length.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var result = new List<string>(UiPreferences.MaximumRecent);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry) || entry.Length > 520 || result.Any(existing => Same(existing, entry))) continue;
            result.Add(entry);
            if (result.Count == UiPreferences.MaximumRecent) break;
        }
        return result;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
