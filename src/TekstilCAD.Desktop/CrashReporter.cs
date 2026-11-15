using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>
/// Last-resort record of an exception that no command handler caught. It only adds a local log
/// file and an explanation: the process still terminates exactly as before, so recovery drafts
/// keep the crash behaviour that the process-recovery scripts exercise. Nothing is sent anywhere.
/// </summary>
internal static class CrashReporter
{
    internal const int MaximumLogFiles = 50;
    private const int MaximumLogCharacters = 200_000;
    private static int dialogShown;

    /// <summary>Attach to the interactive application only; verification modes stay untouched.</summary>
    public static void Attach(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.DispatcherUnhandledException += (_, e) =>
        {
            // Handled stays false: continuing with a half-applied edit could save a damaged project.
            Notify(TryWriteLog(e.Exception, "Arayüz iş parçacığı", AppPaths.LogsDirectory, DateTimeOffset.Now));
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error)
                Notify(TryWriteLog(error, "Arka plan iş parçacığı", AppPaths.LogsDirectory, DateTimeOffset.Now));
        };
        // An unobserved task failure does not end the process, so it is recorded without a dialog.
        TaskScheduler.UnobservedTaskException += (_, e) =>
            TryWriteLog(e.Exception, "Gözlenmeyen arka plan görevi", AppPaths.LogsDirectory, DateTimeOffset.Now);
    }

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
        text.AppendLine("Zaman: " + now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        text.AppendLine("Sürüm: " + AppInfo.DisplayVersion + (AppInfo.SourceRevision is { } revision ? " · derleme " + revision : ""));
        text.AppendLine("Çalışma zamanı: .NET " + Environment.Version + " · " + Environment.OSVersion.VersionString + (Environment.Is64BitProcess ? " · x64" : " · x86"));
        text.AppendLine("Kaynak: " + source);
        text.AppendLine();
        text.AppendLine(error.ToString());
        return text.ToString();
    }

    private static void Prune(string directory, string keep)
    {
        try
        {
            var logs = new DirectoryInfo(directory).GetFiles("hata-*.txt").OrderByDescending(file => file.Name, StringComparer.Ordinal).ToList();
            foreach (var old in logs.Skip(MaximumLogFiles))
                if (!string.Equals(old.FullName, keep, StringComparison.OrdinalIgnoreCase)) old.Delete();
        }
        catch (Exception) { /* Old logs are only housekeeping. */ }
    }

    private static void Notify(string? logPath)
    {
        if (Interlocked.Exchange(ref dialogShown, 1) != 0) return;
        try
        {
            var log = logPath is null
                ? "Hata günlüğü yazılamadı."
                : "Hata günlüğü (yalnız bu bilgisayarda tutulur):" + Environment.NewLine + logPath;
            MessageBox.Show(
                "Beklenmeyen bir hata oluştu ve uygulama kapanacak." + Environment.NewLine + Environment.NewLine +
                "Uygulamayı yeniden açtığınızda varsa kurtarma kayıtları listelenir; her değişikliğin kurtarılacağı garanti değildir." +
                Environment.NewLine + Environment.NewLine + log,
                "Mono Di Tree CAD kapanıyor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception) { /* The process is already ending. */ }
    }
}
