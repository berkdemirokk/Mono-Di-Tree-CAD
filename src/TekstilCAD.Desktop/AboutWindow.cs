using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace TekstilCAD.Desktop;

/// <summary>
/// Read-only product information: version, preview status, data location and third-party notices.
/// It changes no project or setting; the only side effects are opening a folder or file in Windows
/// and copying the support summary to the clipboard, each on an explicit button press.
/// </summary>
internal sealed class AboutWindow : Window
{
    internal const string NoticesFileName = "THIRD_PARTY_NOTICES.txt";

    internal TextBlock VersionText { get; } = new() { FontSize = 14, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    internal TextBox DataPath { get; } = new() { IsReadOnly = true, Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap };
    internal Button OpenData { get; } = new() { Content = "Veri klasörünü aç", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 6) };
    internal Button OpenLogs { get; } = new() { Content = "Hata günlüklerini aç", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 6) };
    internal Button OpenNotices { get; } = new() { Content = "Bildirim dosyasını aç", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 6, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
    internal Button CopySummary { get; } = new() { Content = "Sürüm bilgisini kopyala", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0) };
    internal TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap, Foreground = StudioTheme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };

    /// <summary>Shows a folder in File Explorer; replaced during background verification.</summary>
    internal Func<string, bool> OpenFolder { get; set; } = ShowInExplorer;
    /// <summary>Opens the notices text file with its registered viewer; replaced during background verification.</summary>
    internal Func<string, bool> OpenFile { get; set; } = OpenWithWindows;
    /// <summary>Copies text to the clipboard; replaced during background verification.</summary>
    internal Action<string> CopyText { get; set; } = Clipboard.SetText;

    internal AboutWindow(Window owner)
    {
        Owner = owner; Title = "Hakkında"; Width = 580; Height = 600; MinWidth = 440; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; StudioTheme.Apply(this);
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;

        var actions = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = true };
        var close = new Button { Content = "Kapat", IsCancel = true, IsDefault = true, Padding = new Thickness(16, 8, 16, 8) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right); actions.Children.Add(close);
        DockPanel.SetDock(CopySummary, Dock.Right); actions.Children.Add(CopySummary);
        AutomationProperties.SetName(Status, "Hakkında penceresi durumu");
        AutomationProperties.SetLiveSetting(Status, AutomationLiveSetting.Polite);
        actions.Children.Add(Status);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);

        var panel = new StackPanel();
        var viewer = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true };
        KeyboardNavigation.SetIsTabStop(viewer, true); AutomationProperties.SetName(viewer, "Ürün ve sürüm bilgileri"); root.Children.Add(viewer);

        panel.Children.Add(new TextBlock { Text = AppInfo.ProductName, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = StudioTheme.Accent });
        panel.Children.Add(new TextBlock { Text = "TekstilCAD Studio · Windows masaüstü", Foreground = StudioTheme.Muted, Margin = new Thickness(0, 2, 0, 0) });
        VersionText.Text = VersionLine();
        AutomationProperties.SetName(VersionText, VersionText.Text);
        panel.Children.Add(VersionText);

        panel.Children.Add(new Border
        {
            Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 14, 0, 0),
            Child = new TextBlock
            {
                Text = "Geliştirme önizlemesi. Ticari üretim için tamamlanmış veya sertifikalı değildir; üretimden önce ölçüleri, çıktıları ve makine uyumunu bağımsız olarak doğrulayın.",
                Foreground = StudioTheme.Warning, TextWrapping = TextWrapping.Wrap
            }
        });

        panel.Children.Add(Heading("ÇALIŞMA ORTAMI"));
        panel.Children.Add(new TextBlock { Text = EnvironmentLine(), TextWrapping = TextWrapping.Wrap });

        panel.Children.Add(Heading("VERİ KLASÖRÜ"));
        panel.Children.Add(new TextBlock { Text = "Projeler, kurtarma kayıtları, simülasyon verileri ve hata günlükleri bu klasörde tutulur. Uygulama klasöründen bağımsızdır; uygulamayı güncellemek bu verileri silmez.", TextWrapping = TextWrapping.Wrap });
        DataPath.Text = AppPaths.DataDirectory;
        AutomationProperties.SetName(DataPath, "Veri klasörü yolu");
        panel.Children.Add(DataPath);
        var folders = new WrapPanel();
        AutomationProperties.SetHelpText(OpenData, "Klasörü Windows Dosya Gezgini'nde açar; hiçbir dosyayı değiştirmez.");
        AutomationProperties.SetHelpText(OpenLogs, "Beklenmeyen hata günlüklerinin klasörünü açar. Günlükler yalnız bu bilgisayarda tutulur.");
        OpenData.Click += (_, _) => ShowFolder(AppPaths.DataDirectory, "Veri klasörü henüz oluşturulmadı; ilk kayıt veya kurtarma kaydıyla oluşur.");
        OpenLogs.Click += (_, _) => ShowFolder(AppPaths.LogsDirectory, "Henüz hata günlüğü yok.");
        folders.Children.Add(OpenData); folders.Children.Add(OpenLogs); panel.Children.Add(folders);

        panel.Children.Add(Heading("AÇIK KAYNAK BİLDİRİMLERİ"));
        panel.Children.Add(new TextBlock { Text = "Clipper2 — Boost Software License 1.0. .NET çalışma zamanı ve WPF — MIT. Tam metinler uygulama klasöründeki " + NoticesFileName + " ve CLIPPER2-LICENSE.txt dosyalarındadır.", TextWrapping = TextWrapping.Wrap });
        var notices = Path.Combine(AppContext.BaseDirectory, NoticesFileName);
        OpenNotices.IsEnabled = File.Exists(notices);
        if (!OpenNotices.IsEnabled) OpenNotices.ToolTip = "Bildirim dosyası bu kurulumda bulunamadı.";
        OpenNotices.Click += (_, _) => ShowNotices(notices);
        panel.Children.Add(OpenNotices);

        CopySummary.Click += (_, _) =>
        {
            try { CopyText(SupportSummary()); Announce("Sürüm bilgisi panoya kopyalandı."); }
            catch (Exception) { Announce("Pano kullanılamadı; bilgiyi bu pencereden elle aktarın."); }
        };
    }

    internal static string VersionLine() =>
        "Sürüm " + AppInfo.DisplayVersion +
        (AppInfo.SourceRevision is { } revision ? " · derleme " + revision : "") +
        (AppPaths.IsIsolatedPreview ? " · yalıtılmış önizleme" : "");

    internal static string EnvironmentLine() =>
        ".NET " + Environment.Version + " · " + Environment.OSVersion.VersionString + (Environment.Is64BitProcess ? " · x64" : " · x86");

    /// <summary>Plain text for a support request. It contains no project content or file names.</summary>
    internal static string SupportSummary() =>
        AppInfo.ProductName + Environment.NewLine + VersionLine() + Environment.NewLine + EnvironmentLine();

    private void ShowFolder(string path, string missing)
    {
        try
        {
            // Only an existing directory is handed over, and only as an argument to File Explorer, so a
            // file or a program that happens to carry the folder's name can never be started from here.
            if (!Directory.Exists(path)) { Announce(missing); return; }
            Announce(OpenFolder(path) ? "Windows'ta açıldı." : "Windows bu konumu açamadı.");
        }
        catch (Exception) { Announce("Windows bu konumu açamadı."); }
    }

    private void ShowNotices(string path)
    {
        try
        {
            if (!File.Exists(path)) { Announce("Bildirim dosyası bulunamadı."); return; }
            Announce(OpenFile(path) ? "Windows'ta açıldı." : "Windows bu dosyayı açamadı.");
        }
        catch (Exception) { Announce("Windows bu dosyayı açamadı."); }
    }

    private void Announce(string text)
    {
        Status.Text = text;
        AutomationProperties.SetName(Status, "Hakkında penceresi durumu: " + text);
    }

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = StudioTheme.Muted, Margin = new Thickness(0, 18, 0, 6) };

    private static bool ShowInExplorer(string directory)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
        start.ArgumentList.Add(directory);
        using var process = Process.Start(start);
        return process is not null;
    }

    // The path is the fixed notices .txt beside the executable, so its registered viewer is a text editor.
    private static bool OpenWithWindows(string path)
    {
        using var process = Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        return true;
    }
}
