#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Panopticon.Content.Importing;

/// <summary>
/// CPU-only attachment hint for one imported particle effect. DDF bindings (WI-C4h-05) fill
/// these; the importer itself never parses DDF tables.
/// </summary>
public sealed class ImportedParticleAttachment
{
    /// <summary>Target bone or tag name resolved case-insensitively; null attaches at a fixed world matrix.</summary>
    public string? TargetBoneName { get; }

    /// <summary>True samples the target bone world matrix every update; false spawns at a fixed matrix.</summary>
    public bool FollowTarget { get; }

    public Vector3 LocalOffset { get; }

    public Vector3 BoneLocalOffset { get; }

    /// <summary>DDF world scale applied to the effect; source units are engine-converted at materialization.</summary>
    public float WorldScale { get; }

    public bool OneShot { get; }

    public ImportedParticleAttachment(
        string? targetBoneName,
        bool followTarget,
        Vector3 localOffset,
        float worldScale,
        bool oneShot,
        Vector3 boneLocalOffset = default)
    {
        if (followTarget && string.IsNullOrEmpty(targetBoneName))
            throw new ArgumentException(
                "A following particle attachment requires a target bone or tag name.",
                nameof(targetBoneName));

        if (!float.IsFinite(localOffset.X) || !float.IsFinite(localOffset.Y) || !float.IsFinite(localOffset.Z))
            throw new ArgumentException("Attachment local offset must be finite.", nameof(localOffset));

        if (!float.IsFinite(worldScale) || worldScale <= 0.0f)
            throw new ArgumentException("Attachment world scale must be finite and positive.", nameof(worldScale));

        TargetBoneName = targetBoneName;
        if (!float.IsFinite(boneLocalOffset.X) || !float.IsFinite(boneLocalOffset.Y) || !float.IsFinite(boneLocalOffset.Z))
            throw new ArgumentException("Attachment bone-local offset must be finite.", nameof(boneLocalOffset));

        BoneLocalOffset = boneLocalOffset;
        FollowTarget = followTarget;
        LocalOffset = localOffset;
        WorldScale = worldScale;
        OneShot = oneShot;
    }

    /// <summary>World-origin one-shot spawn with unit scale.</summary>
    public static ImportedParticleAttachment CreateWorldSpawn()
    {
        return new ImportedParticleAttachment(null, followTarget: false, Vector3.Zero, 1.0f, oneShot: true);
    }
}
