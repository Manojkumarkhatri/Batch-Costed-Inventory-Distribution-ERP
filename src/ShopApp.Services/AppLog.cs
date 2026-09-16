using System.Text;

namespace ShopApp.Services;

/// <summary>
/// A rolling daily log file.
///
/// Deliberately not Serilog. On a single-machine app the whole requirement is
/// "write a line to a file, never throw, never block the UI, keep a month" -
/// which is eighty lines. A logging framework brings configuration, sinks and
/// a version to keep current, for a shop PC that will never read a structured
/// event.
///
/// The one thing that matters: logging must never be able to break the app it
/// is meant to diagnose. Every method here swallows its own failures.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string _folder = "";
    private static int _keepDays = 30;

    /// <summary>Where today's file lives. Shown to the user after a crash.</summary>
    public static string Folder => _folder;

    public static string TodayFile =>
        Path.Combine(_folder, $"shopapp-{DateTime.Today:yyyy-MM-dd}.log");

    /// <summary>
    /// Called once at startup, before anything that might fail. Old files are
    /// pruned here rather than on a timer: the app is opened every morning, so
    /// startup is the only schedule it needs.
    /// </summary>
    public static void Start(string folder, int keepDays = 30)
    {
        try
        {
            _folder = folder;
            _keepDays = keepDays;
            Directory.CreateDirectory(folder);
            Prune();
        }
        catch
        {
            // A log that cannot be written is not a reason to refuse to run.
            _folder = "";
        }
    }

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{Describe(ex)}");

    /// <summary>
    /// An exception with everything worth having: the type, the message, the
    /// stack, and every inner exception. A report that says only "object
    /// reference not set" costs an afternoon.
    /// </summary>
    public static string Describe(Exception ex)
    {
        var sb = new StringBuilder();
        var depth = 0;

        for (Exception? e = ex; e is not null; e = e.InnerException, depth++)
        {
            var indent = new string(' ', depth * 2);
            sb.AppendLine($"{indent}{e.GetType().FullName}: {e.Message}");
            if (!string.IsNullOrWhiteSpace(e.StackTrace))
                sb.AppendLine(indent + e.StackTrace.Replace(Environment.NewLine,
                                                            Environment.NewLine + indent));
        }

        return sb.ToString().TrimEnd();
    }

    private static void Write(string level, string message)
    {
        if (string.IsNullOrEmpty(_folder)) return;

        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";

            // One process, but several threads: report generation and the
            // backup both run off the UI thread.
            lock (Gate)
            {
                File.AppendAllText(TodayFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Disk full, file locked, folder gone. None of it is worth a crash.
        }
    }

    private static void Prune()
    {
        var cutoff = DateTime.Today.AddDays(-_keepDays);

        foreach (var file in Directory.GetFiles(_folder, "shopapp-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
            catch
            {
                // Locked or in use. It will be caught on a later run.
            }
        }
    }
}
