#nullable enable

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>Package format constants for the H3 map package container.</summary>
public static class MapPackageFormat
{
    public const string PackageFormatName = "panopticon-map-package";

    public const int CurrentPackageVersion = 1;

    public const string ManifestFileName = "manifest.json";

    public const string EncodingFloat32Le = "float32-le";

    public const string EncodingUint8 = "uint8";

    public const string EncodingRgba8 = "rgba8";

    public const string LayerKindHeights = "heights";

    public const string LayerKindMaterials = "materials";

    public const string LayerKindMasks = "masks";

    public const string LayerKindImagery = "imagery";
}
