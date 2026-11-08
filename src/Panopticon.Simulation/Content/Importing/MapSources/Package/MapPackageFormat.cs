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

    /// <summary>Maximum allowed size in bytes for a manifest.json file (64 MiB).</summary>
    public const long MaxManifestBytes = 67_108_864;

    /// <summary>Maximum allowed size in bytes for a manifest.json file (64 MiB).</summary>
    public const long MaxManifestBytesBytes = MaxManifestBytes;

    /// <summary>Maximum allowed lattice points along a single axis (1,048,576 points).</summary>
    public const int MaxLatticePointsPerAxis = 1_048_576;

    /// <summary>Maximum allowed total lattice points across the entire grid (268,435,456 points).</summary>
    public const long MaxLatticePointsTotal = 268_435_456;

    /// <summary>Maximum allowed size in bytes for a single layer or retained section payload file (1 GiB).</summary>
    public const long MaxPackageFileBytes = 1_073_741_824;
}
