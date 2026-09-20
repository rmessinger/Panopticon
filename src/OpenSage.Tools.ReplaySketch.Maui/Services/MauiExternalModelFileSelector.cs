#nullable enable

using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.ReplaySketch.Maui.Services;

/// <summary>
/// MAUI file selection for the Model tab through <see cref="FilePicker"/> with .gltf/.glb
/// guidance. Picker cancellation and unsupported platforms return null (no change).
/// </summary>
public sealed class MauiExternalModelFileSelector : IExternalModelFileSelector
{
    public async Task<string?> SelectModelFileAsync(CancellationToken cancellationToken)
    {
        return await PickAsync("model").ConfigureAwait(true);
    }

    public async Task<string?> SelectAnimationFileAsync(CancellationToken cancellationToken)
    {
        return await PickAsync("animation").ConfigureAwait(true);
    }

    private static async Task<string?> PickAsync(string role)
    {
        try
        {
            var customTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, [".gltf", ".glb"] },
                    { DevicePlatform.macOS, ["gltf", "glb"] },
                    { DevicePlatform.MacCatalyst, ["gltf", "glb"] },
                    { DevicePlatform.iOS, ["public.data"] },
                    { DevicePlatform.Android, ["application/octet-stream"] },
                });

            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = $"Select a {role} document (.gltf or .glb)",
                FileTypes = customTypes,
            }).ConfigureAwait(true);

            return result?.FullPath;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // Picker unavailable on this platform; treat as cancellation.
            return null;
        }
    }
}
