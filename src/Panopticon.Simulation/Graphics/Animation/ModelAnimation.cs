using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace Panopticon.Graphics.Animation;

/// <summary>
/// Format-neutral runtime animation asset: duration plus unbound target-indexed channels.
/// Conversion from imported values lives in the engine-owned materializer; no W3D parsing here.
/// </summary>
public sealed class ModelAnimation : BaseAsset
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly ConditionalWeakTable<ModelBoneHierarchy, BoundModelAnimation> _boundCache = new();

    public TimeSpan Duration { get; }

    public ModelAnimationChannel[] Channels { get; }

    internal ModelAnimation(string name, TimeSpan duration, ModelAnimationChannel[] channels)
    {
        SetNameAndInstanceId("ModelAnimation", name);

        Duration = duration;
        Channels = channels;
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

    private BoundModelAnimation ComputeBinding(ModelBoneHierarchy hierarchy)
    {
        Dictionary<string, int>? boneNameIndex = null;
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
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < hierarchy.Bones.Length; i++)
        {
            var bone = hierarchy.Bones[i];
            if (bone.Name is null)
            {
                continue;
            }

            if (index.TryGetValue(bone.Name, out var existingIndex))
            {
                throw new InvalidDataException(
                    $"Hierarchy '{hierarchy.Name}' contains bones '{hierarchy.Bones[existingIndex].Name}' at index {existingIndex} and '{bone.Name}' at index {i} differing only by case; named animation targets would be ambiguous.");
            }

            index.Add(bone.Name, i);
        }

        return index;
    }
}
