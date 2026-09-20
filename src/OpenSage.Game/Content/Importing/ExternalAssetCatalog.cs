#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenSage.Content.Importing;

/// <summary>Engine-owned model-space bounds: axis-aligned box plus its bounding sphere.</summary>
public sealed record ExternalModelBounds(
    Vector3 Min,
    Vector3 Max,
    Vector3 SphereCenter,
    float SphereRadius);

/// <summary>
/// Immutable, engine-owned summary of one external asset source. Contains only counts,
/// bounds, feature identifiers, animation descriptors, and non-fatal notes; no parser
/// objects, mutable collections, or runtime graphics assets.
/// </summary>
public sealed class ExternalAssetCatalog
{
    public string SourceDisplayName { get; }

    public string FormatId { get; }

    public int SceneCount { get; }

    public int NodeCount { get; }

    public int PrimitiveCount { get; }

    public long VertexCount { get; }

    public long IndexCount { get; }

    public int MaterialCount { get; }

    public int ImageCount { get; }

    public int SkinCount { get; }

    public int JointCount { get; }

    public ExternalModelBounds Bounds { get; }

    public IReadOnlyList<string> RequiredFeatureIds { get; }

    public IReadOnlyList<string> OptionalFeatureIds { get; }

    public IReadOnlyList<ExternalAnimationDescriptor> Animations { get; }

    public IReadOnlyList<ExternalAssetInspectionDiagnostic> Diagnostics { get; }

    public ExternalAssetCatalog(
        string sourceDisplayName,
        string formatId,
        int sceneCount,
        int nodeCount,
        int primitiveCount,
        long vertexCount,
        long indexCount,
        int materialCount,
        int imageCount,
        int skinCount,
        int jointCount,
        ExternalModelBounds bounds,
        IReadOnlyList<string> requiredFeatureIds,
        IReadOnlyList<string> optionalFeatureIds,
        IReadOnlyList<ExternalAnimationDescriptor> animations,
        IReadOnlyList<ExternalAssetInspectionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(sourceDisplayName))
        {
            throw new ArgumentException("Source display name must not be empty.", nameof(sourceDisplayName));
        }

        if (bounds is null)
        {
            throw new ArgumentNullException(nameof(bounds));
        }

        SourceDisplayName = sourceDisplayName;
        FormatId = formatId;
        SceneCount = sceneCount;
        NodeCount = nodeCount;
        PrimitiveCount = primitiveCount;
        VertexCount = vertexCount;
        IndexCount = indexCount;
        MaterialCount = materialCount;
        ImageCount = imageCount;
        SkinCount = skinCount;
        JointCount = jointCount;
        Bounds = bounds;
        RequiredFeatureIds = requiredFeatureIds.ToArray();
        OptionalFeatureIds = optionalFeatureIds.ToArray();
        Animations = animations.ToArray();
        Diagnostics = diagnostics.ToArray();
    }
}
