using Panopticon.Graphics.Shaders;

namespace Panopticon.Settings;

public sealed class LightSettings
{
    internal readonly GlobalShaderResources.LightingConstantsPS LightsPS;

    internal LightSettings(
        in GlobalShaderResources.LightingConfiguration terrainLightsPS,
        in GlobalShaderResources.LightingConfiguration objectLightsPS)
    {
        LightsPS.Terrain = terrainLightsPS;
        LightsPS.Object = objectLightsPS;
    }
}
