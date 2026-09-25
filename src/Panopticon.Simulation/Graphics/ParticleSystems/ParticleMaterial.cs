using System.Numerics;
using Panopticon.Graphics.Shaders;
using Panopticon.Rendering;

namespace Panopticon.Graphics.ParticleSystems;

internal static class ShaderSetStoreExtensions
{
    public static ParticleShaderResources GetParticleShaderSet(this ShaderSetStore store)
    {
        return store.GetShaderSet(() => new ParticleShaderResources(store));
    }
}

public struct ParticleRenderItemConstantsVS
{
    public Matrix4x4 WorldMatrix;
}
