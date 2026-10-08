using System;
using System.Collections.Generic;
using Veldrid;

namespace Panopticon.Rendering;

public sealed class ShaderSetStore : DisposableBase
{
    private readonly Dictionary<Type, ShaderSet> _cache = new();

    private byte _nextId;

    public readonly GraphicsDevice GraphicsDevice;
    public readonly OutputDescription OutputDescription;

    /// <summary>
    /// The output description with the sample count forced to one, for passes that share the
    /// settings formats but must stay single-sample (water maps, 2D layer).
    /// </summary>
    public readonly OutputDescription SingleSampleOutputDescription;

    /// <summary>
    /// Whether shaders sampling this store render into the linear colour space (G6): colour
    /// inputs decode at the sampling or upload boundary and the single sRGB transfer happens
    /// in the output pass, not in material shaders.
    /// </summary>
    public readonly bool LinearColourSpace;

    public ShaderSetStore(
        GraphicsDevice graphicsDevice,
        OutputDescription outputDescription,
        bool linearColourSpace = false)
    {
        GraphicsDevice = graphicsDevice;
        OutputDescription = outputDescription;
        LinearColourSpace = linearColourSpace;

        SingleSampleOutputDescription = new OutputDescription(
            outputDescription.DepthAttachment,
            outputDescription.ColorAttachments);
    }

    public T GetShaderSet<T>(Func<T> createShaderSet)
        where T : ShaderSet
    {
        var key = typeof(T);

        if (!_cache.TryGetValue(key, out var result))
        {
            result = AddDisposable(createShaderSet());
            _cache.Add(key, result);
        }

        return (T)result;
    }

    internal byte GetNextId()
    {
        return checked(_nextId++);
    }
}
