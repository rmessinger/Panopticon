#nullable enable

using System;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// Immutable import configuration shared by the glTF model and animation importers.
/// </summary>
public sealed class GltfImportOptions
{
    /// <summary>
    /// World units per glTF meter. OpenSAGE's internal distance unit is the foot.
    /// </summary>
    public float UnitsPerMeter { get; }

    public long MaxRootDocumentBytes { get; }

    public long MaxDataUriBytes { get; }

    public static GltfImportOptions Default { get; } = new();

    public GltfImportOptions(
        float unitsPerMeter = 1f / 0.3048f,
        long maxRootDocumentBytes = 16 * 1024 * 1024,
        long maxDataUriBytes = 16 * 1024 * 1024)
    {
        if (float.IsNaN(unitsPerMeter) || float.IsInfinity(unitsPerMeter) || unitsPerMeter <= 0)
        {
            throw new ArgumentException("Units per meter must be a positive finite value.", nameof(unitsPerMeter));
        }

        if (maxRootDocumentBytes <= 0)
        {
            throw new ArgumentException("Maximum root document bytes must be positive.", nameof(maxRootDocumentBytes));
        }

        if (maxDataUriBytes <= 0)
        {
            throw new ArgumentException("Maximum data-URI bytes must be positive.", nameof(maxDataUriBytes));
        }

        UnitsPerMeter = unitsPerMeter;
        MaxRootDocumentBytes = maxRootDocumentBytes;
        MaxDataUriBytes = maxDataUriBytes;
    }
}
