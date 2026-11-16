using TekstilCAD.Core.Theme;

namespace TekstilCAD.Desktop;

/// <summary>
/// The one place that changes and saves the colour theme and the text size. The About window, the simple view header button and
/// the command box all go through it, so each of them repaints the windows and writes <c>ui.json</c> the same way.
/// </summary>
internal static class InterfaceSettings
{
    private const string NotSaved = " Seçim kaydedilemedi; uygulama yeniden açılınca önceki görünüm geri gelir.";

    internal static bool IsLight => ReferenceEquals(StudioTheme.Current, ThemePalette.Light);

    /// <summary>Switches between the dark and the light palette; returns the sentence that tells what happened.</summary>
    internal static string ToggleTheme() => UseTheme(IsLight ? ThemePalette.Dark : ThemePalette.Light);

    /// <summary>Applies a palette to every open window, saves the choice and returns the sentence that tells what happened.</summary>
    internal static string UseTheme(ThemePalette palette)
    {
        StudioTheme.Use(palette);
        var saved = Save(preferences => preferences with { Theme = palette.Name });
        return (palette == ThemePalette.Light ? "Açık görünüm kullanılıyor." : "Koyu görünüm kullanılıyor.") + (saved ? "" : NotSaved);
    }

    /// <summary>Saves the text size (percent); false when the file could not be written.</summary>
    internal static bool SaveTextScale(int percent) => Save(preferences => preferences with { TextScale = percent });

    private static bool Save(Func<UiPreferences, UiPreferences> change)
        => change(UiPreferences.Load(AppPaths.UiPreferencesPath)).Save(AppPaths.UiPreferencesPath);
}
