#nullable enable

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>
/// Source of the layer and retained-section payloads staged by
/// <see cref="MapPackageDirectoryWriter"/>. Provided data must match the manifest
/// entries' declared byte lengths and digests; the writer verifies both after staging.
/// </summary>
public interface IMapPackagePayloadProvider
{
    /// <summary>Height samples for one heights layer entry, apron included.</summary>
    float[] GetHeights(MapPackageLayerEntry entry);

    /// <summary>Palette index bytes for one materials layer entry.</summary>
    byte[] GetMaterials(MapPackageLayerEntry entry);

    /// <summary>Raw flag bytes for one masks layer entry.</summary>
    byte[] GetMasks(MapPackageLayerEntry entry);

    /// <summary>Raw RGBA8 pixel bytes for one imagery layer entry.</summary>
    byte[] GetImagery(MapPackageLayerEntry entry);

    /// <summary>Opaque bytes for one retained section entry.</summary>
    byte[] GetRetainedSection(MapPackageRetainedEntry entry);
}
