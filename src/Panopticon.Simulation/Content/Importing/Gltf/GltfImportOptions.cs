#nullable enable

using System;
using Panopticon;

namespace Panopticon.Content.Importing.Gltf;

/// <summary>
/// Immutable import configuration shared by the glTF model and animation importers.
/// </summary>
public sealed class GltfImportOptions
{
    /// <summary>
    /// World units per glTF meter. Panopticon's internal distance unit is the foot.
    /// </summary>
    public float UnitsPerMeter { get; }

    public long MaxRootDocumentBytes { get; }

    public long MaxDataUriBytes { get; }

    public long MaxExternalResourceBytes { get; }

    public long MaxAggregateExternalResourceBytes { get; }

    public int MaxNodeCount { get; }

    public int MaxMeshCount { get; }

    public int MaxPrimitiveCount { get; }

    public int MaxVertexCount { get; }

    public int MaxIndexCount { get; }

    public int MaxJointCount { get; }

    public int MaxAnimationCount { get; }

    public int MaxChannelCount { get; }

    public int MaxKeyframeCount { get; }

    public int MaxImageDimension { get; }

    public int MaxDataUriNestingDepth { get; }

    public static GltfImportOptions Default { get; } = new();

    public GltfImportOptions(
        float unitsPerMeter = 1f / 0.3048f,
        long maxRootDocumentBytes = 16 * 1024 * 1024,
        long maxDataUriBytes = 16 * 1024 * 1024,
        long maxExternalResourceBytes = 16 * 1024 * 1024,
        long maxAggregateExternalResourceBytes = 64 * 1024 * 1024,
        int maxNodeCount = 1_000_000,
        int maxMeshCount = 100_000,
        int maxPrimitiveCount = 1_000_000,
        int maxVertexCount = 10_000_000,
        int maxIndexCount = 30_000_000,
        int maxJointCount = 512,
        int maxAnimationCount = 1_000,
        int maxChannelCount = 10_000,
        int maxKeyframeCount = 1_000_000,
        int maxImageDimension = 16_384,
        int maxDataUriNestingDepth = 8)
    {
        Ensure.FinitePositive(unitsPerMeter, "Units per meter must be a positive finite value.");

        Ensure.GreaterThan(maxRootDocumentBytes, 0L, "Maximum root document bytes must be positive.");

        Ensure.GreaterThan(maxDataUriBytes, 0L, "Maximum data-URI bytes must be positive.");

        Ensure.GreaterThan(maxExternalResourceBytes, 0L, "Maximum external resource bytes must be positive.");

        Ensure.GreaterThan(maxAggregateExternalResourceBytes, 0L, "Maximum aggregate external resource bytes must be positive.");

        foreach (var (value, name) in new[]
        {
            (maxNodeCount, nameof(maxNodeCount)),
            (maxMeshCount, nameof(maxMeshCount)),
            (maxPrimitiveCount, nameof(maxPrimitiveCount)),
            (maxVertexCount, nameof(maxVertexCount)),
            (maxIndexCount, nameof(maxIndexCount)),
            (maxJointCount, nameof(maxJointCount)),
            (maxAnimationCount, nameof(maxAnimationCount)),
            (maxChannelCount, nameof(maxChannelCount)),
            (maxKeyframeCount, nameof(maxKeyframeCount)),
            (maxImageDimension, nameof(maxImageDimension)),
            (maxDataUriNestingDepth, nameof(maxDataUriNestingDepth)),
        })
        {
            Ensure.GreaterThan(value, 0, $"Limit '{name}' must be positive.", name);
        }

        UnitsPerMeter = unitsPerMeter;
        MaxRootDocumentBytes = maxRootDocumentBytes;
        MaxDataUriBytes = maxDataUriBytes;
        MaxExternalResourceBytes = maxExternalResourceBytes;
        MaxAggregateExternalResourceBytes = maxAggregateExternalResourceBytes;
        MaxNodeCount = maxNodeCount;
        MaxMeshCount = maxMeshCount;
        MaxPrimitiveCount = maxPrimitiveCount;
        MaxVertexCount = maxVertexCount;
        MaxIndexCount = maxIndexCount;
        MaxJointCount = maxJointCount;
        MaxAnimationCount = maxAnimationCount;
        MaxChannelCount = maxChannelCount;
        MaxKeyframeCount = maxKeyframeCount;
        MaxImageDimension = maxImageDimension;
        MaxDataUriNestingDepth = maxDataUriNestingDepth;
    }
}
