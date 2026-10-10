using System.Diagnostics;
using System.IO;

namespace FullscreenStart.Services;

/// <summary>Small append-only log in %AppData%\FullscreenStart\log.txt (trimmed when it grows past 512 KB).</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(string directory)
    {
        _path = Path.Combine(directory, "log.txt");
        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length > 512 * 1024)
                File.Move(_path, _path + ".old", overwrite: true);
        }
        catch (IOException)
        {
        }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
        Debug.WriteLine(line);
        if (_path == null) return;
        lock (Gate)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine); }
            catch (IOException) { }
        }
    }
}
