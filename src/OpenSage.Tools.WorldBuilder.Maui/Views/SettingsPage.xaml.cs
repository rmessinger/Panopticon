using CommunityToolkit.Maui.Storage;

namespace OpenSage.Tools.WorldBuilder.Maui.Views;

public partial class SettingsPage : ContentPage
{
    private const string PrefKeyGenerals = "GamePath_Generals";
    private const string PrefKeyZeroHour = "GamePath_ZeroHour";
    private const string PrefKeyBfme = "GamePath_Bfme";

    public SettingsPage()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        GeneralsPathEntry.Text = Preferences.Default.Get(PrefKeyGenerals, string.Empty);
        ZeroHourPathEntry.Text = Preferences.Default.Get(PrefKeyZeroHour, string.Empty);
        BfmePathEntry.Text = Preferences.Default.Get(PrefKeyBfme, string.Empty);

        // Pre-populate from environment variables if no saved value
        if (string.IsNullOrEmpty(GeneralsPathEntry.Text))
        {
            GeneralsPathEntry.Text = Environment.GetEnvironmentVariable("CNC_GENERALS_PATH") ?? string.Empty;
        }

        if (string.IsNullOrEmpty(ZeroHourPathEntry.Text))
        {
            ZeroHourPathEntry.Text = Environment.GetEnvironmentVariable("CNC_GENERALS_ZH_PATH") ?? string.Empty;
        }

        if (string.IsNullOrEmpty(BfmePathEntry.Text))
        {
            BfmePathEntry.Text = Environment.GetEnvironmentVariable("BFME_PATH") ?? string.Empty;
        }
    }

    private async void OnBrowseGenerals(object? sender, EventArgs e) =>
        await BrowseForFolder(GeneralsPathEntry);

    private async void OnBrowseZeroHour(object? sender, EventArgs e) =>
        await BrowseForFolder(ZeroHourPathEntry);

    private async void OnBrowseBfme(object? sender, EventArgs e) =>
        await BrowseForFolder(BfmePathEntry);

    private static async Task BrowseForFolder(Entry entry)
    {
        if (!(OperatingSystem.IsAndroid()
            || OperatingSystem.IsIOSVersionAtLeast(14)
            || OperatingSystem.IsMacCatalystVersionAtLeast(14)
            || OperatingSystem.IsWindows()))
        {
            return;
        }

        try
        {
            var result = await FolderPicker.Default.PickAsync(CancellationToken.None);
            if (result.IsSuccessful)
            {
                entry.Text = result.Folder.Path;
            }
        }
        catch (Exception)
        {
            // FolderPicker not available on this platform; let user type the path
        }
    }

    private async void OnCancel(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();

    private async void OnSave(object? sender, EventArgs e)
    {
        Preferences.Default.Set(PrefKeyGenerals, GeneralsPathEntry.Text ?? string.Empty);
        Preferences.Default.Set(PrefKeyZeroHour, ZeroHourPathEntry.Text ?? string.Empty);
        Preferences.Default.Set(PrefKeyBfme, BfmePathEntry.Text ?? string.Empty);
        await Navigation.PopModalAsync();
    }

    // Public helpers for reading saved paths elsewhere in the app.
    public static string? GetGeneralsPath() =>
        NonEmpty(Preferences.Default.Get(PrefKeyGenerals, string.Empty)
                 ?? Environment.GetEnvironmentVariable("CNC_GENERALS_PATH"));

    public static string? GetZeroHourPath() =>
        NonEmpty(Preferences.Default.Get(PrefKeyZeroHour, string.Empty)
                 ?? Environment.GetEnvironmentVariable("CNC_GENERALS_ZH_PATH"));

    public static string? GetBfmePath() =>
        NonEmpty(Preferences.Default.Get(PrefKeyBfme, string.Empty)
                 ?? Environment.GetEnvironmentVariable("BFME_PATH"));

    private static string? NonEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}
