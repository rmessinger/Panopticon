using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Panopticon.Content.Importing;
using Panopticon.Graphics;

namespace Panopticon.Graphics.Animation;

/// <summary>
/// Format-neutral runtime animation asset: duration plus unbound target-indexed channels.
/// Conversion from imported values lives in the engine-owned materializer; no W3D parsing here.
/// </summary>
public sealed class ModelAnimation : BaseAsset
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly ConditionalWeakTable<ModelBoneHierarchy, BoundModelAnimation> _boundCache = new();

    private readonly ConditionalWeakTable<Model, BoundModelAnimation> _modelBoundCache = new();

    private readonly IReadOnlyList<ImportedMorphAnimationChannel> _morphChannels;

    public TimeSpan Duration { get; }

    public ModelAnimationChannel[] Channels { get; }

    internal ModelAnimation(
        string name,
        TimeSpan duration,
        ModelAnimationChannel[] channels,
        IReadOnlyList<ImportedMorphAnimationChannel> morphChannels = null)
    {
        SetNameAndInstanceId("ModelAnimation", name);

        Duration = duration;
        Channels = channels;
        _morphChannels = morphChannels;
    }

    /// <summary>
    /// Binds this animation against a hierarchy, cached by hierarchy object identity. Out-of-range
    /// direct-index channels are omitted from the binding and warned about once per hierarchy;
    /// named-target failures always throw.
    /// </summary>
    public BoundModelAnimation Bind(ModelBoneHierarchy hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);

        return _boundCache.GetValue(hierarchy, _ => ComputeBinding(hierarchy));
    }

    /// <summary>
    /// Binds this animation against a model, cached by model identity. Bone channels resolve as in
    /// <see cref="Bind(ModelBoneHierarchy)"/>; sub-object and material-slot channels resolve against
    /// the model's sub-objects, so all target validation happens before playback. Named-target
    /// failures always throw.
    /// </summary>
    public BoundModelAnimation Bind(Model model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return _modelBoundCache.GetValue(model, _ => ComputeModelBinding(model));
    }

    private BoundModelAnimation ComputeModelBinding(Model model)
    {
        Dictionary<string, int> boneNameIndex = null;
        var clips = new List<AnimationClip>(Channels.Length);
        for (var i = 0; i < Channels.Length; i++)
        {
            var channel = Channels[i];
            switch (channel.Target.Kind)
            {
                case ModelAnimationTargetKind.DirectIndex:
                    {
                        var boneIndex = channel.Target.DirectIndex;
                        if (boneIndex >= model.BoneHierarchy.Bones.Length)
                        {
                            Logger.Warn(
                                $"Animation '{Name}' omits channel {i}: direct index {boneIndex} is out of range for hierarchy '{model.BoneHierarchy.Name}' with {model.BoneHierarchy.Bones.Length} bones.");
                            continue;
                        }

                        clips.Add(new AnimationClip(channel.ClipType, boneIndex, channel.Keyframes, channel.Interpolation));
                        break;
                    }

                case ModelAnimationTargetKind.BoneName:
                    {
                        boneNameIndex ??= BuildBoneNameIndex(model.BoneHierarchy);
                        var boneName = channel.Target.BoneName;
                        if (!boneNameIndex.TryGetValue(boneName, out var boneIndex))
                        {
                            throw new InvalidDataException(
                                $"Animation '{Name}' targets bone '{boneName}', which does not exist in hierarchy '{model.BoneHierarchy.Name}'.");
                        }

                        if (boneIndex == AmbiguousBoneIndex)
                        {
                            throw new InvalidDataException(
                                $"Animation '{Name}' targets bone '{boneName}', which matches multiple bones in hierarchy '{model.BoneHierarchy.Name}' differing only by case.");
                        }

                        clips.Add(new AnimationClip(channel.ClipType, boneIndex, channel.Keyframes, channel.Interpolation));
                        break;
                    }

                case ModelAnimationTargetKind.SubObjectName:
                    {
                        var subObjectIndex = ResolveSubObjectIndex(model, channel.Target.SubObjectName, i);
                        clips.Add(new AnimationClip(
                            channel.ClipType,
                            AnimationClipTarget.SubObject,
                            subObjectIndex,
                            -1,
                            channel.Keyframes,
                            channel.Interpolation));
                        break;
                    }

                case ModelAnimationTargetKind.MaterialSlot:
                    {
                        var subObjectIndex = ResolveSubObjectIndex(model, channel.Target.SubObjectName, i);
                        var materialIndex = channel.Target.MaterialIndex;
                        var mesh = model.SubObjects[subObjectIndex].RenderObject as ModelMesh;
                        if (mesh is null || materialIndex >= mesh.MaterialCount)
                        {
                            throw new InvalidDataException(
                                $"Animation '{Name}' channel {i} targets material slot {materialIndex} of sub-object '{model.SubObjects[subObjectIndex].FullName}', which has {mesh?.MaterialCount ?? 0} materials.");
                        }

                        clips.Add(new AnimationClip(
                            channel.ClipType,
                            AnimationClipTarget.MaterialSlot,
                            subObjectIndex,
                            materialIndex,
                            channel.Keyframes,
                            channel.Interpolation));
                        break;
                    }
            }
        }

        return new BoundModelAnimation(Name, Duration, clips.ToArray(), BuildMorphTracks(model));
    }

    private MorphWeightTrack[] BuildMorphTracks(Model model)
    {
        if (_morphChannels is not { Count: > 0 })
        {
            return Array.Empty<MorphWeightTrack>();
        }

        var tracks = new List<MorphWeightTrack>(_morphChannels.Count);
        for (var i = 0; i < _morphChannels.Count; i++)
        {
            tracks.Add(BindMorphChannel(model, _morphChannels[i], i));
        }

        return tracks.ToArray();
    }

    private MorphWeightTrack BindMorphChannel(Model model, ImportedMorphAnimationChannel channel, int channelIndex)
    {
        for (var i = 0; i < model.SubObjects.Length; i++)
        {
            if (!string.Equals(model.SubObjects[i].FullName, channel.PrimitiveId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (model.SubObjects[i].RenderObject is not ModelMesh mesh || mesh.MorphTargetSet is not { } targetSet)
            {
                throw new InvalidDataException(
                    $"Animation '{Name}' morph channel {channelIndex} targets primitive '{channel.PrimitiveId}', which has no morph target set.");
            }

            var slots = new int[channel.TargetNames.Count];
            for (var t = 0; t < channel.TargetNames.Count; t++)
            {
                var slot = -1;
                for (var s = 0; s < targetSet.TargetNames.Length; s++)
                {
                    if (string.Equals(targetSet.TargetNames[s], channel.TargetNames[t], StringComparison.Ordinal))
                    {
                        slot = s;
                        break;
                    }
                }

                if (slot < 0)
                {
                    throw new InvalidDataException(
                        $"Animation '{Name}' morph channel {channelIndex} targets morph target '{channel.TargetNames[t]}', which does not exist on primitive '{channel.PrimitiveId}'.");
                }

                slots[t] = slot;
            }

            var keyframes = new MorphWeightKeyframe[channel.Times.Count];
            for (var k = 0; k < channel.Times.Count; k++)
            {
                keyframes[k] = new MorphWeightKeyframe(
                    TimeSpan.FromSeconds(channel.Times[k]),
                    channel.WeightVectors[k]);
            }

            var interpolation = channel.Interpolation == ImportedAnimationInterpolation.Step
                ? AnimationInterpolation.Step
                : AnimationInterpolation.Linear;

            return new MorphWeightTrack(i, channel.PrimitiveId, slots, keyframes, interpolation);
        }

        throw new InvalidDataException(
            $"Animation '{Name}' morph channel {channelIndex} targets primitive '{channel.PrimitiveId}', which does not exist in model '{model.Name}'.");
    }

    private int ResolveSubObjectIndex(Model model, string name, int channelIndex)
    {
        for (var i = 0; i < model.SubObjects.Length; i++)
        {
            if (string.Equals(model.SubObjects[i].FullName, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        var match = -1;
        for (var i = 0; i < model.SubObjects.Length; i++)
        {
            if (!string.Equals(model.SubObjects[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match >= 0)
            {
                throw new InvalidDataException(
                    $"Animation '{Name}' channel {channelIndex} targets sub-object '{name}', which is ambiguous in model '{model.Name}': it matches '{model.SubObjects[match].FullName}' and '{model.SubObjects[i].FullName}'.");
            }

            match = i;
        }

        if (match < 0)
        {
            throw new InvalidDataException(
                $"Animation '{Name}' channel {channelIndex} targets sub-object '{name}', which does not exist in model '{model.Name}'.");
        }

        return match;
    }

    private BoundModelAnimation ComputeBinding(ModelBoneHierarchy hierarchy)
    {
        if (_morphChannels is { Count: > 0 })
        {
            throw new NotSupportedException(
                $"Animation '{Name}' carries morph weight channels; morph animations must bind against a model.");
        }

        Dictionary<string, int> boneNameIndex = null;
        var clips = new List<AnimationClip>(Channels.Length);
        for (var i = 0; i < Channels.Length; i++)
        {
            var channel = Channels[i];
            int boneIndex;
            if (channel.Target.Kind == ModelAnimationTargetKind.DirectIndex)
            {
                boneIndex = channel.Target.DirectIndex;
                if (boneIndex >= hierarchy.Bones.Length)
                {
                    Logger.Warn(
                        $"Animation '{Name}' omits channel {i}: direct index {boneIndex} is out of range for hierarchy '{hierarchy.Name}' with {hierarchy.Bones.Length} bones.");
                    continue;
                }
            }
            else if (channel.Target.Kind == ModelAnimationTargetKind.BoneName)
            {
                boneNameIndex ??= BuildBoneNameIndex(hierarchy);
                var boneName = channel.Target.BoneName;
                if (!boneNameIndex.TryGetValue(boneName, out boneIndex))
                {
                    throw new InvalidDataException(
                        $"Animation '{Name}' targets bone '{boneName}', which does not exist in hierarchy '{hierarchy.Name}'.");
                }

                if (boneIndex == AmbiguousBoneIndex)
                {
                    throw new InvalidDataException(
                        $"Animation '{Name}' targets bone '{boneName}', which matches multiple bones in hierarchy '{hierarchy.Name}' differing only by case.");
                }
            }
            else
            {
                throw new NotSupportedException(
                    $"Animation '{Name}' channel {i} uses a {channel.Target.Kind} target, which does not address a bone; animations with sub-object or material channels must bind against a model.");
            }

            clips.Add(new AnimationClip(channel.ClipType, boneIndex, channel.Keyframes, channel.Interpolation));
        }

        return new BoundModelAnimation(Name, Duration, clips.ToArray());
    }

    private static Dictionary<string, int> BuildBoneNameIndex(ModelBoneHierarchy hierarchy)
    {
        // Real content (EE2 LHA10_siegegun) contains unrelated bones with
        // duplicate names; ambiguity is only fatal for names actually targeted,
        // so duplicates mark the entry instead of rejecting the hierarchy.
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < hierarchy.Bones.Length; i++)
        {
            var bone = hierarchy.Bones[i];
            if (bone.Name is null)
            {
                continue;
            }

            if (index.TryGetValue(bone.Name, out _))
            {
                index[bone.Name] = AmbiguousBoneIndex;
            }
            else
            {
                index.Add(bone.Name, i);
            }
        }

        return index;
    }

    private const int AmbiguousBoneIndex = -2;
}
