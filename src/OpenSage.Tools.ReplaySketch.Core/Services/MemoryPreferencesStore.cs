using System.Collections.Generic;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// In-memory <see cref="IPreferencesStore"/> implementation for use in tests.
/// </summary>
public sealed class MemoryPreferencesStore : IPreferencesStore
{
    private readonly Dictionary<string, string> _store = new();

    public string Get(string key, string defaultValue) =>
        _store.TryGetValue(key, out var value) ? value : defaultValue;

    public void Set(string key, string value) => _store[key] = value;
}
