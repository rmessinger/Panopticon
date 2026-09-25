using System;
using System.IO;
using System.Text;

namespace Panopticon.Input;

public sealed class InputLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    public InputLogger()
    {
        string logDirectory = Path.Combine(@"E:\log", "panopticon");
        Directory.CreateDirectory(logDirectory);
        string fileName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt";
        string filePath = Path.Combine(logDirectory, fileName);
        _writer = new StreamWriter(filePath, false, Encoding.UTF8);
        _writer.AutoFlush = true;
        _writer.WriteLine($"Input Log - Started at {DateTime.Now}");
        _writer.WriteLine("----------------------------------------");
    }

    public void LogKeyboardInput(string key, bool isKeyDown)
    {
        string action = isKeyDown ? "pressed" : "released";
        _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Key {action}: {key}");
    }

    public void LogMouseInput(int x, int y, string button = null, bool isButtonDown = false, int wheelDelta = 0)
    {
        if (button != null)
        {
            string action = isButtonDown ? "pressed" : "released";
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Mouse {button} {action} at ({x}, {y})");
        }
        else if (wheelDelta != 0)
        {
            string direction = wheelDelta > 0 ? "up" : "down";
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Mouse wheel {direction} ({wheelDelta}) at ({x}, {y})");
        }
        else
        {
            _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Mouse moved to ({x}, {y})");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer.WriteLine($"Input Log - Ended at {DateTime.Now}");
            _writer.Close();
            _writer.Dispose();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }
}
