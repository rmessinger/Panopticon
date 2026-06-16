using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.ReplaySketch.Maui.Services;

/// <summary>
/// <see cref="IPreferencesStore"/> adapter that delegates to <see cref="Microsoft.Maui.Storage.Preferences.Default"/>.
/// Used as the production implementation in the MAUI app.
/// </summary>
internal sealed class MauiPreferencesStore : IPreferencesStore
{
    public string Get(string key, string defaultValue) =>
        Microsoft.Maui.Storage.Preferences.Default.Get(key, defaultValue);

    public void Set(string key, string value) =>
        Microsoft.Maui.Storage.Preferences.Default.Set(key, value);
}
