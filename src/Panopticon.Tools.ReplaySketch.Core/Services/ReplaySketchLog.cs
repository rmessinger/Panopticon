using System;
using System.Diagnostics;
using System.IO;

namespace Panopticon.Tools.ReplaySketch.Services;

public static class ReplaySketchLog
{
    private static readonly object Sync = new();

    // Resolved lazily on first Write: primary path first, temp fallback if primary is not writable.
    private static string? ActiveLogPath;

    /// <summary>
    /// The path where log entries are actually being written.
    /// Resolved on first <see cref="Write"/> call; reflects the fallback path if the primary
    /// location is not writable from this process.
    /// </summary>
    public static string LogPath => ActiveLogPath ?? BuildPrimaryPath();

    public static void Write(string message)
    {
        var line = $"{DateTimeOffset.Now:O} [pid:{Environment.ProcessId}] {message}";

        lock (Sync)
        {
            if (ActiveLogPath is not null)
            {
                TryAppendLine(ActiveLogPath, line);
                return;
            }

            // First write: probe primary path, then fall back to temp.
            var primary = BuildPrimaryPath();
            if (TryAppendLine(primary, line))
            {
                ActiveLogPath = primary;
                Console.Error.WriteLine($"[ReplaySketchLog] writing to {primary}");
                return;
            }

            var fallback = BuildFallbackPath();
            if (TryAppendLine(fallback, line))
            {
                ActiveLogPath = fallback;
                Console.Error.WriteLine($"[ReplaySketchLog] primary path not writable; writing to {fallback}");
                return;
            }

            // Both paths failed — debug output only.
            Console.Error.WriteLine($"[ReplaySketchLog] failed to write to any path: {line}");
            Debug.WriteLine(line);
        }
    }

    public static void WriteException(string context, Exception exception) =>
        Write($"{context}: {exception}");

    private static string BuildPrimaryPath() =>
        Path.Combine(PlatformPaths.AppDataBase, "Panopticon", "ReplaySketch", "replaysketch.log");

    private static string BuildFallbackPath() =>
        Path.Combine(Path.GetTempPath(), "Panopticon-ReplaySketch", "replaysketch.log");

    private static bool TryAppendLine(string path, string line)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(path, line + Environment.NewLine);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
