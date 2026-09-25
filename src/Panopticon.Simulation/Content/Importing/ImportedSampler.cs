namespace Panopticon.Content.Importing;

public enum ImportedAddressMode
{
    Repeat,
    MirroredRepeat,
    Clamp,
}

public enum ImportedFilterMode
{
    Nearest,
    Linear,
}

/// <summary>
/// Immutable sampler description in engine semantics. U/V address modes plus minification,
/// magnification, and mip filters.
/// </summary>
public sealed class ImportedSampler
{
    public ImportedAddressMode AddressModeU { get; }

    public ImportedAddressMode AddressModeV { get; }

    public ImportedFilterMode MinFilter { get; }

    public ImportedFilterMode MagFilter { get; }

    public ImportedFilterMode MipFilter { get; }

    public ImportedSampler(
        ImportedAddressMode addressModeU = ImportedAddressMode.Repeat,
        ImportedAddressMode addressModeV = ImportedAddressMode.Repeat,
        ImportedFilterMode minFilter = ImportedFilterMode.Linear,
        ImportedFilterMode magFilter = ImportedFilterMode.Linear,
        ImportedFilterMode mipFilter = ImportedFilterMode.Linear)
    {
        AddressModeU = addressModeU;
        AddressModeV = addressModeV;
        MinFilter = minFilter;
        MagFilter = magFilter;
        MipFilter = mipFilter;
    }
}
