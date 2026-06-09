using System;
using System.Diagnostics;
using System.IO;

namespace OpenSage.Tools.ReplaySketch.Services;

public static class ReplaySketchLog
{
    private static readonly object s_sync = new();

    public static string LogPath { get; } = CreateLogPath();

    public static void Write(string message)
    {
        var line = $"{DateTimeOffset.Now:O} [pid:{Environment.ProcessId}] {message}";

        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (s_sync)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            Debug.WriteLine(line);
        }
        catch (UnauthorizedAccessException)
        {
            Debug.WriteLine(line);
        }
        catch (NotSupportedException)
        {
            Debug.WriteLine(line);
        }
    }

    public static void WriteException(string context, Exception exception)
    {
        Write($"{context}: {exception}");
    }

    private static string CreateLogPath()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDirectory))
        {
            baseDirectory = Path.GetTempPath();
        }

        return Path.Combine(baseDirectory, "OpenSAGE", "ReplaySketch", "replaysketch.log");
    }
}
