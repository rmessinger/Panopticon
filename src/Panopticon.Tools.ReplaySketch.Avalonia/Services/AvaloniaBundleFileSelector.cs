using Avalonia.Platform.Storage;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.ReplaySketch.Avalonia.Services;

/// <summary>
/// Run-bundle selection for the Results tab through Avalonia's StorageProvider; accepts the
/// bundle directory or its run.json manifest. Picker cancellation returns null (no change).
/// WI-A5-04.
/// </summary>
public sealed class AvaloniaBundleFileSelector(Func<IStorageProvider> storageProvider) : IBundleFileSelector
{
    public async Task<string?> SelectBundleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var files = await storageProvider().OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select a run bundle manifest (run.json)",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Run bundle manifest")
                    {
                        Patterns = ["run.json"],
                    },
                ],
            });
            if (files.Count == 0)
            {
                return null;
            }

            return files[0].TryGetLocalPath();
        }
        catch (Exception exception) when (exception is InvalidOperationException or TaskCanceledException)
        {
            // Picker unavailable or cancelled; treat as no change.
            return null;
        }
    }
}
