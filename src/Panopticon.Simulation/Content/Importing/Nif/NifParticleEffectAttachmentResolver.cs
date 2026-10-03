#nullable enable

using System;
using System.IO;
using System.Numerics;
using Panopticon.Graphics;
using Panopticon.Graphics.ParticleSystems;

namespace Panopticon.Content.Importing.Nif;

internal enum NifParticleAttachmentKind
{
    WorldSpawn,
    FollowBone,
}

/// <summary>
/// One resolved effect placement: a fixed world matrix for world spawns and one-shot effects,
/// or a follow delegate that recomposes the bone world matrix with offset and scale per read.
/// </summary>
internal sealed class NifParticleAttachmentPlan
{
    public NifParticleEffectBinding Binding { get; }

    public NifParticleAttachmentKind Kind { get; }

    /// <summary>Resolved bone index; -1 for world spawns.</summary>
    public int BoneIndex { get; }

    /// <summary>Fixed matrix for <see cref="NifParticleAttachmentKind.WorldSpawn"/> plans.</summary>
    public Matrix4x4 FixedWorldMatrix { get; }

    /// <summary>Follow delegate for <see cref="NifParticleAttachmentKind.FollowBone"/> plans; null otherwise.</summary>
    public ParticleSystem.GetMatrixReferenceDelegate? FollowMatrix { get; }

    private NifParticleAttachmentPlan(
        NifParticleEffectBinding binding,
        NifParticleAttachmentKind kind,
        int boneIndex,
        Matrix4x4 fixedWorldMatrix,
        ParticleSystem.GetMatrixReferenceDelegate? followMatrix)
    {
        Binding = binding;
        Kind = kind;
        BoneIndex = boneIndex;
        FixedWorldMatrix = fixedWorldMatrix;
        FollowMatrix = followMatrix;
    }

    internal static NifParticleAttachmentPlan CreateWorldSpawn(
        NifParticleEffectBinding binding,
        in Matrix4x4 worldOrigin)
    {
        var matrix = Compose(binding, worldOrigin);
        return new NifParticleAttachmentPlan(binding, NifParticleAttachmentKind.WorldSpawn, -1, matrix, null);
    }

    internal static NifParticleAttachmentPlan CreateFollowBone(
        NifParticleEffectBinding binding,
        int boneIndex,
        Func<int, Matrix4x4> getBoneWorldMatrix)
    {
        ArgumentNullException.ThrowIfNull(getBoneWorldMatrix);

        var source = new FollowMatrixSource(binding, boneIndex, getBoneWorldMatrix);
        return new NifParticleAttachmentPlan(
            binding,
            NifParticleAttachmentKind.FollowBone,
            boneIndex,
            Matrix4x4.Identity,
            source.GetMatrix);
    }

    /// <summary>Captures the current bone matrix once; used for one-shot bone-attached effects.</summary>
    internal static NifParticleAttachmentPlan CreateOneShotBoneSpawn(
        NifParticleEffectBinding binding,
        int boneIndex,
        Func<int, Matrix4x4> getBoneWorldMatrix)
    {
        var matrix = Compose(binding, getBoneWorldMatrix(boneIndex));
        return new NifParticleAttachmentPlan(binding, NifParticleAttachmentKind.WorldSpawn, boneIndex, matrix, null);
    }

    // Keep the attachment offset independent of effect scale and rotate it with the bone.
    // LocalOffset retains its existing world-space semantics.
    private static Matrix4x4 Compose(NifParticleEffectBinding binding, in Matrix4x4 worldMatrix)
    {
        return Matrix4x4.CreateScale(binding.WorldScale)
            * Matrix4x4.CreateTranslation(binding.BoneLocalOffset)
            * worldMatrix
            * Matrix4x4.CreateTranslation(binding.LocalOffset);
    }

    private sealed class FollowMatrixSource
    {
        private readonly NifParticleEffectBinding _binding;
        private readonly int _boneIndex;
        private readonly Func<int, Matrix4x4> _getBoneWorldMatrix;
        private Matrix4x4 _composed;

        public FollowMatrixSource(
            NifParticleEffectBinding binding,
            int boneIndex,
            Func<int, Matrix4x4> getBoneWorldMatrix)
        {
            _binding = binding;
            _boneIndex = boneIndex;
            _getBoneWorldMatrix = getBoneWorldMatrix;
            _composed = NifParticleAttachmentPlan.Compose(binding, getBoneWorldMatrix(boneIndex));
        }

        public ref readonly Matrix4x4 GetMatrix()
        {
            _composed = NifParticleAttachmentPlan.Compose(_binding, _getBoneWorldMatrix(_boneIndex));
            return ref _composed;
        }
    }
}

/// <summary>
/// Resolves DDF effect bindings against the C4c runtime bone hierarchy (WI-C4h-05). Names match
/// case-insensitively; missing or duplicate names fail before any ParticleSystemManager.Create.
/// </summary>
internal static class NifParticleEffectAttachmentResolver
{
    /// <summary>
    /// Resolves the binding's target bone: the unique root for bone-less bindings, otherwise the
    /// unique case-insensitive name match.
    /// </summary>
    public static int ResolveBoneIndex(ModelBone[] bones, NifParticleEffectBinding binding)
    {
        if (bones == null || bones.Length == 0)
        {
            throw new InvalidOperationException("Bone hierarchy is empty.");
        }

        if (string.IsNullOrEmpty(binding.TargetBoneName))
        {
            return ResolveRoot(bones, binding);
        }

        var matchIndex = -1;
        var matchCount = 0;
        for (var i = 0; i < bones.Length; i++)
        {
            if (string.Equals(bones[i].Name, binding.TargetBoneName, StringComparison.OrdinalIgnoreCase))
            {
                matchIndex = i;
                matchCount++;
            }
        }

        return matchCount switch
        {
            1 => matchIndex,
            0 => throw BindFailure(binding, $"the model hierarchy has no bone named '{binding.TargetBoneName}'"),
            _ => throw BindFailure(
                binding,
                $"the model hierarchy has {matchCount} bones named '{binding.TargetBoneName}'"),
        };
    }

    /// <summary>World-spawn plan: fixed matrix captured at resolve time.</summary>
    public static NifParticleAttachmentPlan ResolveToWorldSpawn(
        NifParticleEffectBinding binding,
        in Matrix4x4 worldOrigin)
    {
        if (binding.FollowTarget)
        {
            throw new InvalidOperationException(
                $"Particle effect '{binding.EffectName}' from {binding.DdfSource} follows a bone and cannot resolve to a fixed world matrix.");
        }

        return NifParticleAttachmentPlan.CreateWorldSpawn(binding, worldOrigin);
    }

    /// <summary>
    /// Bone-attached plan: follow bindings read the live bone matrix per update; one-shot
    /// bindings capture the current bone matrix once.
    /// </summary>
    public static NifParticleAttachmentPlan ResolveToBone(
        NifParticleEffectBinding binding,
        ModelBone[] bones,
        Func<int, Matrix4x4> getBoneWorldMatrix)
    {
        if (!binding.FollowTarget && string.IsNullOrEmpty(binding.TargetBoneName))
        {
            throw new InvalidOperationException(
                $"Particle effect '{binding.EffectName}' from {binding.DdfSource} has no target bone.");
        }

        var boneIndex = ResolveBoneIndex(bones, binding);

        return binding.FollowTarget
            ? NifParticleAttachmentPlan.CreateFollowBone(binding, boneIndex, getBoneWorldMatrix)
            : NifParticleAttachmentPlan.CreateOneShotBoneSpawn(binding, boneIndex, getBoneWorldMatrix);
    }

    private static int ResolveRoot(ModelBone[] bones, NifParticleEffectBinding binding)
    {
        var rootIndex = -1;
        var rootCount = 0;
        for (var i = 0; i < bones.Length; i++)
        {
            if (bones[i].Parent == null)
            {
                rootIndex = i;
                rootCount++;
            }
        }

        return rootCount switch
        {
            1 => rootIndex,
            0 => throw BindFailure(binding, "the model hierarchy has no root bone"),
            _ => throw BindFailure(binding, $"the model hierarchy has {rootCount} root bones"),
        };
    }

    private static InvalidDataException BindFailure(NifParticleEffectBinding binding, string reason)
    {
        return new InvalidDataException(
            $"Particle effect '{binding.EffectName}' from {binding.DdfSource} cannot bind target '{binding.TargetBoneName ?? "<root>"}': {reason}.");
    }
}
