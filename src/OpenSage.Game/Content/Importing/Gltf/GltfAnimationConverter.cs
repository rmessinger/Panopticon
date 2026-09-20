#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SharpGLTF.Schema2;

namespace OpenSage.Content.Importing.Gltf;

/// <summary>
/// Converts one indexed glTF animation into named C2 channels whose values are offsets from
/// the source document's rest transform, under the repository's row-vector convention:
/// offset * rest reconstructs the authored local sample.
/// </summary>
internal static class GltfAnimationConverter
{
    public static ImportedAnimation Convert(
        int animationIndex,
        string modelPath,
        GltfLoadedDocument document,
        GltfScenePlan plan,
        GltfImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        var animations = document.Root.LogicalAnimations;
        if (animationIndex < 0 || animationIndex >= animations.Count)
        {
            throw new InvalidDataException(
                $"Animation index {animationIndex} is outside document '{modelPath}', which has {animations.Count} animations.");
        }

        var animation = animations[animationIndex];
        var modelName = Path.GetFileNameWithoutExtension(modelPath);
        var name = string.IsNullOrEmpty(animation.Name)
            ? $"{modelName}.Animation{animationIndex}"
            : animation.Name!;

        var basis = GltfTransformConverter.CreateBasis(options.UnitsPerMeter);
        if (animation.Channels.Count > options.MaxChannelCount)
        {
            throw new InvalidDataException(
                $"Animation '{name}' has {animation.Channels.Count} channels; the limit is {options.MaxChannelCount}.");
        }

        var nodePlanBySourceIndex = plan.Nodes.ToDictionary(n => n.SourceNodeIndex);
        var targetedNodeIndices = new HashSet<int>();
        var targetedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var channels = new List<ImportedAnimationChannel>();
        var duration = TimeSpan.Zero;

        foreach (var channel in animation.Channels)
        {
            var targetNode = channel.TargetNode;
            if (!nodePlanBySourceIndex.TryGetValue(targetNode.LogicalIndex, out var nodePlan))
            {
                throw new InvalidDataException(
                    $"Animation '{name}' targets node '{targetNode.Name}', which is outside the selected scene.");
            }

            if (string.IsNullOrEmpty(nodePlan.AuthoredName))
            {
                throw new InvalidDataException(
                    $"Animation '{name}' targets an unnamed node; animation targets require authored names.");
            }

            if (!targetedNodeIndices.Add(targetNode.LogicalIndex) && !targetedNames.Add(nodePlan.AuthoredName!))
            {
                throw new InvalidDataException(
                    $"Animation '{name}' targets node name '{nodePlan.AuthoredName}', which is duplicated under case-insensitive comparison.");
            }

            var rest = targetNode.LocalTransform;
            var restRotation = rest.Rotation;
            var restTranslation = rest.Translation;

            switch (channel.TargetNodePath)
            {
                case PropertyPath.translation:
                    {
                        var sampler = channel.GetTranslationSampler();
                        var interpolation = MapInterpolation(sampler.InterpolationMode, name);
                        var samples = sampler.GetLinearKeys().ToList();
                        var keys = CollectTimes(samples.Select(k => (k.Key, k.Value)), name, ref duration, options.MaxKeyframeCount);
                        var offsets = new Vector3[samples.Count];
                        for (var i = 0; i < samples.Count; i++)
                        {
                            offsets[i] = ComputeTranslationOffset(samples[i].Value, restTranslation, restRotation, basis);
                        }

                        channels.Add(CreateChannel(nodePlan.AuthoredName!, ImportedAnimationChannelType.TranslationX, keys, i => offsets[i].X, interpolation));
                        channels.Add(CreateChannel(nodePlan.AuthoredName!, ImportedAnimationChannelType.TranslationY, keys, i => offsets[i].Y, interpolation));
                        channels.Add(CreateChannel(nodePlan.AuthoredName!, ImportedAnimationChannelType.TranslationZ, keys, i => offsets[i].Z, interpolation));
                        break;
                    }

                case PropertyPath.rotation:
                    {
                        var sampler = channel.GetRotationSampler();
                        var interpolation = MapInterpolation(sampler.InterpolationMode, name);
                        var samples = sampler.GetLinearKeys().ToList();
                        var keys = CollectTimes(samples.Select(k => (k.Key, k.Value.X)), name, ref duration, options.MaxKeyframeCount);
                        var rotations = new Quaternion[samples.Count];
                        for (var i = 0; i < samples.Count; i++)
                        {
                            rotations[i] = ComputeRotationOffset(samples[i].Value, restRotation, basis);
                        }

                        channels.Add(new ImportedAnimationChannel(
                            ImportedAnimationTarget.CreateBoneName(nodePlan.AuthoredName!),
                            ImportedAnimationChannelType.Quaternion,
                            keys.Select((time, i) => ImportedKeyframe.FromQuaternion(time, rotations[i])).ToArray(),
                            interpolation));
                        break;
                    }

                case PropertyPath.scale:
                    {
                        var sampler = channel.GetScaleSampler();
                        var hasNonIdentity = sampler.GetLinearKeys().Any(k => k.Value != Vector3.One);
                        if (hasNonIdentity)
                        {
                            throw new NotSupportedException(
                                $"Animation '{name}' animates non-identity scale, which is outside the supported subset.");
                        }

                        break;
                    }

                case PropertyPath.weights:
                    throw new NotSupportedException(
                        $"Animation '{name}' animates morph weights, which are outside the supported subset.");

                default:
                    throw new NotSupportedException(
                        $"Animation '{name}' uses unsupported channel path '{channel.TargetNodePath}'.");
            }
        }

        if (channels.Count == 0)
        {
            throw new InvalidDataException($"Animation '{name}' contains no supported channels.");
        }

        return new ImportedAnimation(name, duration, channels);
    }

    private static ImportedAnimationChannel CreateChannel(
        string boneName,
        ImportedAnimationChannelType channelType,
        List<TimeSpan> keys,
        Func<int, float> component,
        ImportedAnimationInterpolation interpolation)
    {
        var keyframes = new ImportedKeyframe[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            keyframes[i] = ImportedKeyframe.FromFloat(keys[i], component(i));
        }

        return new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateBoneName(boneName),
            channelType,
            keyframes,
            interpolation);
    }

    private static List<TimeSpan> CollectTimes<T>(
        IEnumerable<(float Time, T Scrap)> keys,
        string animationName,
        ref TimeSpan duration,
        int maxKeyframeCount)
    {
        var result = new List<TimeSpan>();
        foreach (var (time, _) in keys)
        {
            if (!float.IsFinite(time) || time < 0)
            {
                throw new InvalidDataException(
                    $"Animation '{animationName}' has a non-finite or negative keyframe time ({time}).");
            }

            var span = TimeSpan.FromSeconds(time);

            if (result.Count > 0 && span < result[^1])
            {
                throw new InvalidDataException(
                    $"Animation '{animationName}' keyframe times must be monotonically increasing.");
            }

            result.Add(span);
            if (span > duration)
            {
                duration = span;
            }

            if (result.Count > maxKeyframeCount)
            {
                throw new InvalidDataException(
                    $"Animation '{animationName}' exceeds the keyframe limit of {maxKeyframeCount} in one channel.");
            }
        }

        return result;
    }

    /// <summary>
    /// Rotation offset: animated * inverse(rest), then basis-conjugated, so that under the
    /// repository's row-vector composition offset * rest reconstructs the authored rotation.
    /// </summary>
    private static Quaternion ComputeRotationOffset(Quaternion animated, Quaternion rest, Matrix4x4 basis)
    {
        var offset = Quaternion.Concatenate(Quaternion.Inverse(rest), animated);
        return GltfTransformConverter.ConvertRotation(offset, basis);
    }

    /// <summary>
    /// Translation offset: (animated - rest) rotated by inverse(rest), then converted with
    /// units and basis.
    /// </summary>
    private static Vector3 ComputeTranslationOffset(Vector3 animated, Vector3 rest, Quaternion restRotation, Matrix4x4 basis)
    {
        var offset = Vector3.Transform(animated - rest, Quaternion.Inverse(restRotation));
        return GltfTransformConverter.ConvertPosition(offset, basis);
    }

    private static ImportedAnimationInterpolation MapInterpolation(AnimationInterpolationMode mode, string animationName)
    {
        return mode switch
        {
            AnimationInterpolationMode.LINEAR => ImportedAnimationInterpolation.Linear,
            AnimationInterpolationMode.STEP => ImportedAnimationInterpolation.Step,
            _ => throw new NotSupportedException(
                $"Animation '{animationName}' uses {mode} interpolation; only LINEAR and STEP are supported."),
        };
    }
}
