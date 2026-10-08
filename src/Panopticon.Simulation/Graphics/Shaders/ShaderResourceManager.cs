using System.Collections.Generic;
using Panopticon.Diagnostics;
using Panopticon.Rendering;
using Veldrid;

namespace Panopticon.Graphics.Shaders;

internal sealed class ShaderResourceManager : DisposableBase
{
    private readonly Dictionary<string, ShaderMaterialShaderResources> _shaderMaterialResources;

    /// <summary>
    /// Effective alpha-to-coverage mode for this session: the coverage preset is
    /// requested AND the session's pipelines actually render multisampled. At one
    /// sample the coverage preset falls back to the hard cutoff with coverage
    /// mips (WI-G5-02).
    /// </summary>
    /// <summary>
    /// Preset-level coverage mode: coverage-preserving mipmaps are selected for alpha-tested
    /// diffuse textures even at a single sample, where the discard path stays a hard cutoff.
    /// </summary>
    public readonly bool AlphaTestCoverageMode;

    /// <summary>
    /// Effective alpha-to-coverage mode: coverage preset plus a multisampled store. Selects
    /// the A2C pipeline pair and the cutout discard path.
    /// </summary>
    public readonly bool AlphaTestCoverageActive;

    public readonly GlobalShaderResources Global;
    public readonly MeshShaderResources Mesh;

    public readonly RadiusCursorDecalShaderResources RadiusCursor;

    public readonly FixedFunctionShaderResources FixedFunction;
    public readonly PbrMetallicRoughnessShaderResources PbrMetallicRoughness;
    public readonly MeshDepthShaderResources MeshDepth;
    public readonly ParticleShaderResources Particle;
    public readonly RoadShaderResources Road;
    public readonly SpriteShaderResources Sprite;
    public readonly TerrainShaderResources Terrain;
    public readonly WaterShaderResources Water;

    public ShaderResourceManager(
        GraphicsDevice graphicsDevice,
        StandardGraphicsResources standardGraphicsResources,
        ShaderSetStore shaderSetStore)
    {
        using (GameTrace.TraceDurationEvent("ShaderResourceManager()"))
        {
            AlphaTestCoverageMode = standardGraphicsResources.Settings.AlphaTestMode == AlphaTestMode.CoveragePreserving;

            AlphaTestCoverageActive = AlphaTestCoverageMode
                && shaderSetStore.OutputDescription.SampleCount != TextureSampleCount.Count1;

            Global = AddDisposable(new GlobalShaderResources(graphicsDevice));
            Mesh = AddDisposable(new MeshShaderResources(graphicsDevice));

            RadiusCursor = AddDisposable(new RadiusCursorDecalShaderResources(graphicsDevice, standardGraphicsResources.Aniso4xClampSampler));

            FixedFunction = AddDisposable(new FixedFunctionShaderResources(shaderSetStore));
            PbrMetallicRoughness = AddDisposable(new PbrMetallicRoughnessShaderResources(shaderSetStore));
            MeshDepth = AddDisposable(new MeshDepthShaderResources(shaderSetStore));
            Particle = AddDisposable(new ParticleShaderResources(shaderSetStore));
            Road = AddDisposable(new RoadShaderResources(shaderSetStore));
            Sprite = AddDisposable(new SpriteShaderResources(shaderSetStore));
            Terrain = AddDisposable(new TerrainShaderResources(shaderSetStore));
            Water = AddDisposable(new WaterShaderResources(shaderSetStore));

            _shaderMaterialResources = new Dictionary<string, ShaderMaterialShaderResources>
            {
                { "NormalMapped", AddDisposable(new NormalMappedShaderResources(shaderSetStore)) },
                { "Simple", AddDisposable(new SimpleShaderResources(shaderSetStore)) }
            };
        }
    }

    public ShaderMaterialShaderResources GetShaderMaterialResources(string name)
    {
        return _shaderMaterialResources[name];
    }
}
