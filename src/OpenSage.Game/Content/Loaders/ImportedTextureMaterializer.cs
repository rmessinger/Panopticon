#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenSage.Content.Importing;
using OpenSage.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Veldrid;
using Veldrid.ImageSharp;

namespace OpenSage.Content.Loaders;

/// <summary>
/// Uploads decoded imported images as ordinary RGBA8 UNorm textures and creates Veldrid
/// samplers, caching both by complete identity: embedded images by immutable content bytes,
/// external images by scoped virtual path, samplers by every field. Owned by the model
/// materialization lifetime; nothing survives disposal and no process-global state exists.
/// Color-space roles never duplicate texture data: all uploads are UNorm.
/// </summary>
internal sealed class ImportedTextureMaterializer : DisposableBase
{
    // Cached PBR materials reference uploaded textures and outlive any single mesh, so the
    // shared materializer lives as long as its load context (weak key, no process-global state).
    private static readonly ConditionalWeakTable<AssetLoadContext, ImportedTextureMaterializer> SharedByContext = new();

    public static ImportedTextureMaterializer GetShared(AssetLoadContext context)
    {
        return SharedByContext.GetValue(context, static c => new ImportedTextureMaterializer(c));
    }

    private readonly AssetLoadContext _context;
    private readonly Dictionary<ExternalPathKey, Texture> _externalTextures = new();
    private readonly Dictionary<EmbeddedContentKey, Texture> _embeddedTextures = new();
    private readonly Dictionary<SamplerKey, Sampler> _samplers = new();

    public ImportedTextureMaterializer(AssetLoadContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Texture GetTexture(ImportedImageSource source, bool generateMipmaps = false)
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(ImportedTextureMaterializer));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (source.Kind == ImportedImageSourceKind.VirtualPath)
        {
            var pathKey = new ExternalPathKey(source.VirtualPath, generateMipmaps);
            if (!_externalTextures.TryGetValue(pathKey, out var texture))
            {
                texture = CreateTexture(source, generateMipmaps);
                _externalTextures.Add(pathKey, texture);
            }

            return texture;
        }
        else
        {
            var contentKey = new EmbeddedContentKey(source.EmbeddedBytes.ToArray(), generateMipmaps);
            if (!_embeddedTextures.TryGetValue(contentKey, out var texture))
            {
                texture = CreateTexture(source, generateMipmaps);
                _embeddedTextures.Add(contentKey, texture);
            }

            return texture;
        }
    }

    public Sampler GetSampler(ImportedSampler sampler)
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(ImportedTextureMaterializer));
        }

        if (sampler is null)
        {
            throw new ArgumentNullException(nameof(sampler));
        }

        var key = new SamplerKey(
            sampler.AddressModeU,
            sampler.AddressModeV,
            sampler.MinFilter,
            sampler.MagFilter,
            sampler.MipFilter);
        if (!_samplers.TryGetValue(key, out var veldridSampler))
        {
            veldridSampler = _context.GraphicsDevice!.ResourceFactory.CreateSampler(
                new SamplerDescription
                {
                    Filter = ToVeldridFilter(sampler.MinFilter, sampler.MagFilter, sampler.MipFilter),
                    AddressModeU = ToVeldridAddressMode(sampler.AddressModeU),
                    AddressModeV = ToVeldridAddressMode(sampler.AddressModeV),
                    AddressModeW = SamplerAddressMode.Wrap,
                    MaximumAnisotropy = 1,
                });
            _samplers.Add(key, veldridSampler);
        }

        return veldridSampler;
    }

    private Texture CreateTexture(ImportedImageSource source, bool generateMipmaps)
    {
        var decoded = ImportedImageDecoder.Decode(
            source,
            _context.FileSystem,
            generateMipmaps);

        // CPU mip generation through the existing ImageSharp path keeps uploads deterministic
        // and device-independent.
        using var image = Image.LoadPixelData<Rgba32>(
            decoded.RgbaPixels.ToArray(),
            decoded.Width,
            decoded.Height);
        var imageSharpTexture = new ImageSharpTexture(image, generateMipmaps);

        var texture = imageSharpTexture.CreateDeviceTexture(
            _context.GraphicsDevice!,
            _context.GraphicsDevice!.ResourceFactory);

        return texture;
    }

    private static SamplerFilter ToVeldridFilter(
        ImportedFilterMode minFilter,
        ImportedFilterMode magFilter,
        ImportedFilterMode mipFilter)
    {
        var min = minFilter == ImportedFilterMode.Nearest ? "Point" : "Linear";
        var mag = magFilter == ImportedFilterMode.Nearest ? "Point" : "Linear";
        var mip = mipFilter == ImportedFilterMode.Nearest ? "Point" : "Linear";
        var name = $"Min{min}_Mag{mag}_Mip{mip}";
        return (SamplerFilter)Enum.Parse(typeof(SamplerFilter), name);
    }

    private static SamplerAddressMode ToVeldridAddressMode(ImportedAddressMode addressMode)
    {
        return addressMode switch
        {
            ImportedAddressMode.Repeat => SamplerAddressMode.Wrap,
            ImportedAddressMode.MirroredRepeat => SamplerAddressMode.Mirror,
            ImportedAddressMode.Clamp => SamplerAddressMode.Clamp,
            _ => throw new ArgumentOutOfRangeException(nameof(addressMode)),
        };
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        if (disposeManagedResources)
        {
            foreach (var texture in _externalTextures.Values)
            {
                texture.Dispose();
            }
            _externalTextures.Clear();

            foreach (var texture in _embeddedTextures.Values)
            {
                texture.Dispose();
            }
            _embeddedTextures.Clear();

            foreach (var sampler in _samplers.Values)
            {
                sampler.Dispose();
            }
            _samplers.Clear();
        }

        base.Dispose(disposeManagedResources);
    }

    private readonly record struct ExternalPathKey(string Path, bool GenerateMipmaps);

    private readonly record struct SamplerKey(
        ImportedAddressMode AddressModeU,
        ImportedAddressMode AddressModeV,
        ImportedFilterMode MinFilter,
        ImportedFilterMode MagFilter,
        ImportedFilterMode MipFilter);

    /// <summary>
    /// Value-equality key over embedded content bytes (FNV-1a hash plus full byte comparison),
    /// so equivalent embedded images share one upload while different bytes never collide.
    /// </summary>
    private readonly struct EmbeddedContentKey : IEquatable<EmbeddedContentKey>
    {
        private readonly byte[] _bytes;
        private readonly bool _generateMipmaps;

        public EmbeddedContentKey(byte[] bytes, bool generateMipmaps)
        {
            _bytes = bytes;
            _generateMipmaps = generateMipmaps;
        }

        public bool Equals(EmbeddedContentKey other)
        {
            return _generateMipmaps == other._generateMipmaps
                && _bytes.Length == other._bytes.Length
                && _bytes.AsSpan().SequenceEqual(other._bytes);
        }

        public override bool Equals(object? obj) => obj is EmbeddedContentKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_generateMipmaps);
            hash.AddBytes(_bytes);
            return hash.ToHashCode();
        }
    }
}
