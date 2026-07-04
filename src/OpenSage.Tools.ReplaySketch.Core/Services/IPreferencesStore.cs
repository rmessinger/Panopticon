namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Minimal key/value persistence seam used by <see cref="../../../Maui/ViewModels/MainViewModel"/>.
/// Kept in Core so the non-MAUI test project can reference and test it without a MAUI runtime.
/// </summary>
public interface IPreferencesStore
{
    string Get(string key, string defaultValue);
    void Set(string key, string value);
}
