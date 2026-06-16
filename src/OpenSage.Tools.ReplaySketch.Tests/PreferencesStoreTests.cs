using System.IO;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tests.Tools.ReplaySketch;

/// <summary>
/// Tests for <see cref="MemoryPreferencesStore"/> and the preference-persistence
/// behaviour of <see cref="OpenSage.Tools.ReplaySketch.Maui.ViewModels.MainViewModel.LoadMapFromFolder"/>.
/// The ViewModel is constructed with a <see cref="MemoryPreferencesStore"/> so these tests
/// run headlessly without a MAUI runtime.
/// </summary>
public class PreferencesStoreTests
{
    // ── MemoryPreferencesStore ────────────────────────────────────────────────

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

    // ── MainViewModel.LoadMapFromFolder preference persistence ────────────────

    [Fact]
    public void LoadMapFromFolder_SavesPreference_EvenWhenMapLoadFails()
    {
        // Arrange: a directory that exists but contains no game files → map load will fail.
        var store = new MemoryPreferencesStore();
        var vm = new OpenSage.Tools.ReplaySketch.Maui.ViewModels.MainViewModel(store);
        var folderPath = Path.GetTempPath();

        // Act
        vm.LoadMapFromFolder(folderPath);

        // Assert: the preference is saved unconditionally before map loading is attempted.
        Assert.Equal(folderPath, store.Get("cnc_generals_path", string.Empty));
    }

    [Fact]
    public void LoadMapFromFolder_PersistsExactPath()
    {
        var store = new MemoryPreferencesStore();
        var vm = new OpenSage.Tools.ReplaySketch.Maui.ViewModels.MainViewModel(store);
        var folderPath = Path.Combine(Path.GetTempPath(), "ReplaySketchPrefTest_" + System.Guid.NewGuid());

        vm.LoadMapFromFolder(folderPath);

        Assert.Equal(folderPath, store.Get("cnc_generals_path", string.Empty));
    }
}
