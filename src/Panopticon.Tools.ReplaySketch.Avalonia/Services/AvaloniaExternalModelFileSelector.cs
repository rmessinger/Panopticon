using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tools.ReplaySketch.Avalonia.Services;

/// <summary>
/// MAUI-free file selection for the Model tab through Avalonia's StorageProvider with
/// .gltf/.glb guidance. Picker cancellation returns null (no change).
/// </summary>
public sealed class AvaloniaExternalModelFileSelector : IExternalModelFileSelector
{
    private readonly Func<IStorageProvider> _storageProvider;

    public AvaloniaExternalModelFileSelector(Func<IStorageProvider> storageProvider)
    {
        _storageProvider = storageProvider;
    }

    public async Task<string?> SelectModelFileAsync(CancellationToken cancellationToken)
    {
        return await PickAsync("Select a model document (.gltf or .glb)").ConfigureAwait(true);
    }

    public async Task<string?> SelectAnimationFileAsync(CancellationToken cancellationToken)
    {
        return await PickAsync("Select an animation document (.gltf or .glb)").ConfigureAwait(true);
    }

    private async Task<string?> PickAsync(string title)
    {
        try
        {
            var files = await _storageProvider().OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("glTF document")
                    {
                        Patterns = ["*.gltf", "*.glb"],
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

/// <summary>
/// Exposes the active window's StorageProvider for selectors constructed before the view
/// is attached to the visual tree.
/// </summary>
public static class StorageProviderSource
{
    private static TopLevel? TopLevelInstance;

    public static void Register(TopLevel topLevel)
    {
        TopLevelInstance = topLevel;
    }

    public static IStorageProvider Current =>
        (TopLevelInstance ?? throw new InvalidOperationException("No TopLevel registered.")).StorageProvider;
}
