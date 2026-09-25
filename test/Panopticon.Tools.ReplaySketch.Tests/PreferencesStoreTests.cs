using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Tests for <see cref="MemoryPreferencesStore"/> — the in-memory seam used to test
/// preference-dependent ViewModel logic without a MAUI runtime.
///
/// Note: tests for MainViewModel.LoadMapFromFolder's unconditional preference-save
/// behaviour require referencing the MAUI project (net10.0-maccatalyst), which this
/// test project (net10.0) cannot do. That coverage is tracked as a follow-up once
/// MainViewModel is moved to Core or a multi-TFM test project is introduced (P0-1c).
/// </summary>
public class PreferencesStoreTests
{
    [Fact]
    public void MemoryPreferencesStore_Get_ReturnsDefault_WhenKeyNotSet()
    {
        var store = new MemoryPreferencesStore();
        Assert.Equal("default", store.Get("missing_key", "default"));
    }

    [Fact]
    public void MemoryPreferencesStore_SetThenGet_ReturnsStoredValue()
    {
        var store = new MemoryPreferencesStore();
        store.Set("key", "value");
        Assert.Equal("value", store.Get("key", "default"));
    }

    [Fact]
    public void MemoryPreferencesStore_SetTwice_ReturnsLatestValue()
    {
        var store = new MemoryPreferencesStore();
        store.Set("key", "first");
        store.Set("key", "second");
        Assert.Equal("second", store.Get("key", "default"));
    }

    [Fact]
    public void MemoryPreferencesStore_MultipleKeys_AreStoredIndependently()
    {
        var store = new MemoryPreferencesStore();
        store.Set("key_a", "alpha");
        store.Set("key_b", "beta");
        Assert.Equal("alpha", store.Get("key_a", string.Empty));
        Assert.Equal("beta", store.Get("key_b", string.Empty));
    }
}
