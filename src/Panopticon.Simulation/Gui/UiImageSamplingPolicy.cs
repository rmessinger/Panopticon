using Panopticon.Graphics;
using Veldrid;

namespace Panopticon.Gui;

/// <summary>
/// Enhanced-only sampling policy for authored UI art. Mapped images are the pre-rendered
/// chrome bitmaps (control bar pieces, icons, pips); under the dynamic UI mode they draw
/// with the settings-driven anisotropic clamp sampler so minified chrome keeps detail.
/// Legacy returns null, keeping the linear clamp sampler the 2D pass began with.
/// </summary>
internal static class UiImageSamplingPolicy
{
    public static Sampler SelectMappedImageSampler(StandardGraphicsResources standardGraphicsResources)
    {
        return standardGraphicsResources.Settings.UiMode == UiMode.DynamicHud
            ? standardGraphicsResources.Aniso4xClampSampler
            : null;
    }
}
