using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.ReplaySketch.Avalonia.Services;

/// <summary>
/// <see cref="IMainThreadDispatcher"/> backed by Avalonia's UI thread dispatcher.
/// </summary>
public sealed class AvaloniaMainThreadDispatcher : IMainThreadDispatcher
{
    public Task InvokeOnMainThreadAsync(Action action)
    {
        var completion = new TaskCompletionSource();
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }
}

/// <summary>
/// File-backed <see cref="IPreferencesStore"/>: a small JSON dictionary under the
/// application-data root. Avalonia has no platform Preferences API.
/// </summary>
public sealed class AvaloniaPreferencesStore : IPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public AvaloniaPreferencesStore()
    {
        _path = Path.Combine(PlatformPaths.AppDataBase, "replaysketch-avalonia-preferences.json");
    }

    public string Get(string key, string defaultValue)
    {
        var values = ReadAll();
        return values.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public void Set(string key, string value)
    {
        var values = ReadAll();
        values[key] = value;
        WriteAll(values);
    }

    private Dictionary<string, string> ReadAll()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path), Options) ?? [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void WriteAll(Dictionary<string, string> values)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(values, Options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Preferences are best-effort; never crash the app over them.
        }
    }
}

/// <summary>
/// <see cref="IProcessLauncher"/> over <see cref="System.Diagnostics.Process"/>.
/// </summary>
public sealed class SystemProcessLauncher : IProcessLauncher
{
    public void Start(System.Diagnostics.ProcessStartInfo psi)
    {
        System.Diagnostics.Process.Start(psi);
    }
}
