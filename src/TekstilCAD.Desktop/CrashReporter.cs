using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>
/// Records an exception that ends the process and explains it at the next start.
/// Nothing is shown at the moment of failure: a dialog there would pump the dispatcher, and the
/// autosave timer could then replace the last consistent recovery draft with the state the
/// exception left behind. The process terminates exactly as it did before. Nothing is sent anywhere.
/// </summary>
internal static class CrashReporter
{
    internal const int MaximumLogFiles = 50;
    private const int MaximumLogCharacters = 200_000;
    private const int MaximumUnobservedLogs = 5;
    private const string NoticeFileName = "bekleyen-hata-bildirimi.txt";
    // Only names this class generates are pruned or accepted from a notice.
    private static readonly Regex LogName = new(@"^hata-\d{8}-\d{6}-\d{3}-[0-9a-f]{8}\.txt$", RegexOptions.CultureInvariant);
    private static int unobservedLogs;

    /// <summary>Attach to the interactive application only; verification modes stay untouched.</summary>
    public static void Attach()
    {
        // Only an exception that nothing caught reaches this event. An exception raised inside a modal
        // dialog also passes through the dispatcher's own event, but the command that opened the dialog
        // still catches it and the application carries on; that event cannot tell the two apart, so it
        // is deliberately not used.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is not Exception error) return;
            var log = TryWriteLog(error, e.IsTerminating ? "Yakalanmayan hata; uygulama kapandı" : "Yakalanmayan hata",
                AppPaths.LogsDirectory, DateTimeOffset.Now);
            if (e.IsTerminating) TryLeaveNotice(log, AppPaths.LogsDirectory);
        };
        // An unobserved task failure does not end the process; a few are kept for diagnosis.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            if (Interlocked.Increment(ref unobservedLogs) <= MaximumUnobservedLogs)
                TryWriteLog(e.Exception, "Gözlenmeyen arka plan görevi; uygulama çalışmaya devam etti", AppPaths.LogsDirectory, DateTimeOffset.Now);
        };
    }

    /// <summary>Before the main window opens, explain that the previous session ended unexpectedly.</summary>
    public static void ShowPendingNotice()
    {
        var (ended, log) = TakePendingNotice(AppPaths.LogsDirectory);
        if (!ended) return;
        try { MessageBox.Show(NoticeText(log), AppInfo.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning); }
        catch (Exception) { /* Starting the application matters more than this notice. */ }
    }

    internal static string NoticeText(string? logPath) =>
        "Önceki oturum beklenmeyen bir hatayla kapandı." + Environment.NewLine + Environment.NewLine +
        "Kaydedilmemiş çalışma için kurtarma kaydı varsa açılışta listelenir; her değişikliğin kurtarılacağı garanti değildir." +
        Environment.NewLine + Environment.NewLine +
        (logPath is null
            ? "Hata günlüğü yazılamadı ya da bulunamadı."
            : "Hata günlüğü (yalnız bu bilgisayarda tutulur):" + Environment.NewLine + logPath);

    /// <summary>Write one log file and return its path, or null when the log could not be written.</summary>
    internal static string? TryWriteLog(Exception error, string source, string directory, DateTimeOffset now)
    {
        try
        {
            var path = Path.Combine(directory, "hata-" + now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
            ProjectStorage.EnsureWritableDestination(path);
            Directory.CreateDirectory(directory);
            var text = Describe(error, source, now);
            if (text.Length > MaximumLogCharacters) text = text[..MaximumLogCharacters] + Environment.NewLine + "[günlük kısaltıldı]";
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
                writer.Write(text);
            Prune(directory, path);
            return path;
        }
        catch (Exception)
        {
            // Reporting must never replace the original failure with a second one.
            return null;
        }
    }

    internal static string Describe(Exception error, string source, DateTimeOffset now)
    {
        var text = new StringBuilder();
        text.AppendLine("Mono Di Tree CAD — beklenmeyen hata günlüğü");
        text.AppendLine("Bu dosya dosya yolları ve proje adları içerebilir; paylaşmadan önce gözden geçirin.");
        text.AppendLine("Zaman: " + now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        text.AppendLine("Sürüm: " + AppInfo.DisplayVersion + (AppInfo.SourceRevision is { } revision ? " · derleme " + revision : ""));
        text.AppendLine("Çalışma zamanı: .NET " + Environment.Version + " · " + Environment.OSVersion.VersionString + (Environment.Is64BitProcess ? " · x64" : " · x86"));
        text.AppendLine("Kaynak: " + source);
        text.AppendLine();
        text.AppendLine(error.ToString());
        return text.ToString();
    }

    /// <summary>Remember, for the next start, that this session ended with an unhandled exception.</summary>
    internal static void TryLeaveNotice(string? logPath, string directory)
    {
        try
        {
            var notice = Path.Combine(directory, NoticeFileName);
            ProjectStorage.EnsureWritableDestination(notice);
            Directory.CreateDirectory(directory);
            File.WriteAllText(notice, logPath is null ? "" : Path.GetFileName(logPath), new UTF8Encoding(false));
        }
        catch (Exception) { /* The process is ending; the log itself may still exist. */ }
    }

    /// <summary>Read and clear the notice a previous session left. Returns whether there was one and,
    /// when its log still exists, the path of that log.</summary>
    internal static (bool Ended, string? LogPath) TakePendingNotice(string directory)
    {
        try
        {
            var notice = Path.Combine(directory, NoticeFileName);
            if (!File.Exists(notice)) return (false, null);
            string name;
            using (var reader = new StreamReader(notice, Encoding.UTF8))
            {
                var buffer = new char[128];
                name = new string(buffer, 0, reader.Read(buffer, 0, buffer.Length)).Trim();
            }
            File.Delete(notice);
            // A name this class did not generate is never turned into a path.
            if (!LogName.IsMatch(name)) return (true, null);
            var log = Path.Combine(directory, name);
            return (true, File.Exists(log) ? log : null);
        }
        catch (Exception) { return (false, null); }
    }

    private static void Prune(string directory, string keep)
    {
        try
        {
            // By write time, newest first; the name breaks ties. A clock set back makes new logs look
            // old, which this housekeeping does not try to detect; the log just written is always kept.
            var logs = new DirectoryInfo(directory).GetFiles("hata-*.txt").Where(file => LogName.IsMatch(file.Name))
                .OrderByDescending(file => file.LastWriteTimeUtc).ThenByDescending(file => file.Name, StringComparer.Ordinal).ToList();
            foreach (var old in logs.Skip(MaximumLogFiles))
                if (!string.Equals(old.FullName, keep, StringComparison.OrdinalIgnoreCase)) old.Delete();
        }
        catch (Exception) { /* Old logs are only housekeeping. */ }
    }
}
