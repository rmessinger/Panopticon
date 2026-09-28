#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.FileFormats.Nif;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// Builds one <see cref="ImportedAnimation"/> from a validated NIF
/// controller sequence: rest-relative channels per controlled block,
/// with samples converted to the engine frame. The runtime composes
/// offsets before the bone rest (row-vector), so a rotation offset
/// Concatenate(sample, Inverse(rest)) and a translation offset
/// Transform(sample - rest, Inverse(restRotation)) / restScale
/// reconstruct the authored sample exactly. Scale groups convert to
/// rest-relative Scale channels (sample / restScale); linear groups
/// keep their source key times, quadratic groups resample through
/// <see cref="NifKeyCurve"/> on the C4c 30 Hz time set.
/// </summary>
internal static class NifAnimationConverter
{
    private const float TimeTolerance = 1e-4f;
    private const float UnitScaleTolerance = 1e-4f;
    private const float AccumulationTolerance = 1e-5f;
    private const float SampleTimeEpsilon = 1e-6f;
    private const float SamplesPerSecond = 30f;
    private const ushort ExpectedControllerFlags = 0x2C;

    public static ImportedAnimation Convert(
        NifFile file,
        NifControllerSequence sequence,
        ImportedSkeleton skeleton,
        NifPropertyTargetMap targets,
        IReadOnlyDictionary<string, float> collapsedScaleNodes,
        string animationName,
        string assetKey)
    {
        var duration = ReadDuration(file, sequence, assetKey);
        var channels = new List<ImportedAnimationChannel>();
        var boundTargets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hasPropertyControllers = sequence.ControlledBlocks.Any(
            block => file.Blocks[block.ControllerReference] is not NifKeyframeController);

        foreach (var controlledBlock in sequence.ControlledBlocks)
        {
            var block = file.Blocks[controlledBlock.ControllerReference];
            if (block is not NifKeyframeController controller)
            {
                NifPropertyAnimationConverter.Convert(
                    file,
                    (NifTimeController)block,
                    controlledBlock.TargetName,
                    skeleton,
                    targets,
                    assetKey,
                    duration,
                    channels);
                continue;
            }

            ValidateController(controller, controlledBlock.TargetName, duration, assetKey, enforceFlags: !hasPropertyControllers);

            int boneIndex;
            try
            {
                boneIndex = ResolveTarget(skeleton, controlledBlock.TargetName, boundTargets, collapsedScaleNodes, assetKey);
            }
            catch (InvalidDataException) when (hasPropertyControllers)
            {
                // EE2 property clips routinely name rig nodes (for example
                // "root NonAccum") that the bound model lacks; such blocks
                // carry no convertible data for this model.
                continue;
            }

            var bone = skeleton.Bones[boneIndex];
            var data = (NifKeyframeData)file.Blocks[controller.DataReference];

            ValidateKeyTimes(data, controlledBlock.TargetName, duration, assetKey);
            ValidateScaleKeys(data, controlledBlock.TargetName, assetKey);

            if (string.Equals(
                    controlledBlock.TargetName,
                    sequence.AccumulationRootName,
                    StringComparison.OrdinalIgnoreCase))
            {
                ValidateAccumulationRootConstancy(data, duration, controlledBlock.TargetName, assetKey);
            }

            AddRotationChannel(data, bone, duration, channels);
            AddTranslationChannels(data, bone, channels);
            AddScaleChannel(data, bone, duration, channels);
        }

        return new ImportedAnimation(
            animationName,
            TimeSpan.FromSeconds(duration),
            channels);
    }

    private static float ReadDuration(NifFile file, NifControllerSequence sequence, string assetKey)
    {
        if (sequence.TextKeysReference < 0 ||
            file.Blocks[sequence.TextKeysReference] is not NifTextKeyExtraData textKeys)
        {
            throw new InvalidDataException(
                $"NIF animation '{assetKey}' has no text keys; the duration cannot be read.");
        }

        var startCount = 0;
        var endCount = 0;
        var duration = -1f;

        foreach (var key in textKeys.Keys)
        {
            if (string.Equals(key.Value, "start", StringComparison.OrdinalIgnoreCase))
            {
                startCount++;
                if (MathF.Abs(key.Time) > TimeTolerance)
                {
                    throw new InvalidDataException(
                        $"NIF animation '{assetKey}' has a 'start' text key at {key.Time.ToString(CultureInfo.InvariantCulture)} s instead of 0.");
                }
            }
            else if (string.Equals(key.Value, "end", StringComparison.OrdinalIgnoreCase))
            {
                endCount++;
                duration = key.Time;
            }
            else
            {
                throw new InvalidDataException(
                    $"NIF animation '{assetKey}' has an unexpected '{key.Value}' text key; only 'start' and 'end' are supported.");
            }
        }

        if (startCount != 1 || endCount != 1 || duration <= 0f)
        {
            throw new InvalidDataException(
                $"NIF animation '{assetKey}' must have exactly one 'start' text key at 0 s and one 'end' text key after 0 s.");
        }

        return duration;
    }

    private static void ValidateController(
        NifKeyframeController controller,
        string targetName,
        float duration,
        string assetKey,
        bool enforceFlags = true)
    {
        if (enforceFlags && controller.Flags != ExpectedControllerFlags)
        {
            throw Unsupported(
                $"target '{targetName}' has controller flags 0x{controller.Flags.ToString("X4", CultureInfo.InvariantCulture)} instead of 0x2C");
        }

        if (controller.Frequency != 1f)
        {
            throw Unsupported(
                $"target '{targetName}' has controller frequency {controller.Frequency.ToString(CultureInfo.InvariantCulture)} instead of 1");
        }

        if (controller.Phase != 0f)
        {
            throw Unsupported(
                $"target '{targetName}' has controller phase {controller.Phase.ToString(CultureInfo.InvariantCulture)} instead of 0");
        }

        if (controller.NextControllerReference != -1)
        {
            throw Unsupported($"target '{targetName}' has a chained next controller");
        }

        if (controller.TargetReference != -1)
        {
            throw Unsupported($"target '{targetName}' has a bound target reference");
        }

        if (controller.StartTime < -TimeTolerance ||
            controller.StopTime > duration + TimeTolerance)
        {
            throw new InvalidDataException(
                $"NIF animation '{assetKey}' target '{targetName}' has controller times [{controller.StartTime.ToString(CultureInfo.InvariantCulture)}, {controller.StopTime.ToString(CultureInfo.InvariantCulture)}] outside the clip duration 0..{duration.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static int ResolveTarget(
        ImportedSkeleton skeleton,
        string targetName,
        Dictionary<string, int> boundTargets,
        IReadOnlyDictionary<string, float> collapsedScaleNodes,
        string assetKey)
    {
        if (boundTargets.TryGetValue(targetName, out var existing))
        {
            throw new InvalidDataException(
                $"NIF animation '{assetKey}' targets bone '{skeleton.Bones[existing].Name}' more than once.");
        }

        for (var i = 0; i < skeleton.Bones.Count; i++)
        {
            if (string.Equals(skeleton.Bones[i].Name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                boundTargets[targetName] = i;
                return i;
            }
        }

        if (TryFindCollapsedScale(collapsedScaleNodes, targetName, out var restScale))
        {
            throw Unsupported(
                $"target '{targetName}' resolved to a collapsed node with singular rest scale {restScale.ToString(CultureInfo.InvariantCulture)}; animating singular-rest nodes needs an absolute track");
        }

        throw new InvalidDataException(
            $"NIF animation '{assetKey}' targets '{targetName}', which has no matching bone in the model skeleton.");
    }

    private static bool TryFindCollapsedScale(
        IReadOnlyDictionary<string, float> collapsedScaleNodes,
        string targetName,
        out float restScale)
    {
        if (collapsedScaleNodes.TryGetValue(targetName, out restScale))
        {
            return true;
        }

        foreach (var (name, scale) in collapsedScaleNodes)
        {
            if (string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                restScale = scale;
                return true;
            }
        }

        return false;
    }

    private static void ValidateKeyTimes(
        NifKeyframeData data,
        string targetName,
        float duration,
        string assetKey)
    {
        ValidateTimes(data.QuaternionRotationKeys?.Keys, key => key.Time, targetName, duration, assetKey, "rotation");
        ValidateTimes(data.XRotationKeys?.Keys, key => key.Time, targetName, duration, assetKey, "x rotation");
        ValidateTimes(data.YRotationKeys?.Keys, key => key.Time, targetName, duration, assetKey, "y rotation");
        ValidateTimes(data.ZRotationKeys?.Keys, key => key.Time, targetName, duration, assetKey, "z rotation");
        ValidateTimes(data.TranslationKeys?.Keys, key => key.Time, targetName, duration, assetKey, "translation");
        ValidateTimes(data.ScaleKeys?.Keys, key => key.Time, targetName, duration, assetKey, "scale");
    }

    private static void ValidateTimes<T>(
        IReadOnlyList<T>? keys,
        Func<T, float> getTime,
        string targetName,
        float duration,
        string assetKey,
        string groupName)
    {
        if (keys is null)
        {
            return;
        }

        var previous = float.NegativeInfinity;
        foreach (var key in keys)
        {
            var time = getTime(key);
            if (!float.IsFinite(time) ||
                time < -TimeTolerance ||
                time > duration + TimeTolerance ||
                time < previous)
            {
                throw new InvalidDataException(
                    $"NIF animation '{assetKey}' target '{targetName}' has a {groupName} key at {time.ToString(CultureInfo.InvariantCulture)} s, outside the clip range 0..{duration.ToString(CultureInfo.InvariantCulture)} or out of order.");
            }

            previous = time;
        }
    }

    private static void ValidateScaleKeys(NifKeyframeData data, string targetName, string assetKey)
    {
        if (data.ScaleKeys is null)
        {
            return;
        }

        foreach (var key in data.ScaleKeys.Keys)
        {
            if (!float.IsFinite(key.Value) || key.Value < 0f)
            {
                throw new InvalidDataException(
                    $"NIF animation '{assetKey}' target '{targetName}' has a scale key {key.Value.ToString(CultureInfo.InvariantCulture)}; scale keys must be finite and non-negative.");
            }
        }
    }

    private static void ValidateAccumulationRootConstancy(
        NifKeyframeData data,
        float duration,
        string targetName,
        string assetKey)
    {
        if (data.QuaternionRotationKeys is not null)
        {
            var first = data.QuaternionRotationKeys.Keys[0].Value;
            foreach (var key in data.QuaternionRotationKeys.Keys)
            {
                if (RotationAngle(first, key.Value) > AccumulationTolerance)
                {
                    throw VaryingAccumulationRoot(targetName, assetKey);
                }
            }
        }
        else if (HasXyzRotation(data))
        {
            var samples = NifRotationSampler.SampleXyz(data, duration);
            for (var i = 1; i < samples.Count; i++)
            {
                if (RotationAngle(samples[0].Rotation, samples[i].Rotation) > AccumulationTolerance)
                {
                    throw VaryingAccumulationRoot(targetName, assetKey);
                }
            }
        }

        if (data.TranslationKeys is not null)
        {
            var first = data.TranslationKeys.Keys[0].Value;
            foreach (var key in data.TranslationKeys.Keys)
            {
                if (Vector3.Distance(first, key.Value) > AccumulationTolerance)
                {
                    throw VaryingAccumulationRoot(targetName, assetKey);
                }
            }
        }
    }

    private static void AddRotationChannel(
        NifKeyframeData data,
        ImportedBone bone,
        float duration,
        List<ImportedAnimationChannel> channels)
    {
        if (data.QuaternionRotationKeys is not null)
        {
            var keyframes = new List<ImportedKeyframe>();
            foreach (var key in data.QuaternionRotationKeys.Keys)
            {
                keyframes.Add(ImportedKeyframe.FromQuaternion(
                    TimeSpan.FromSeconds(key.Time),
                    CreateRotationOffset(key.Value, bone)));
            }

            AddChannel(bone, ImportedAnimationChannelType.Quaternion, keyframes, channels);
        }
        else if (HasXyzRotation(data))
        {
            var samples = NifRotationSampler.SampleXyz(data, duration);
            var keyframes = new List<ImportedKeyframe>();
            foreach (var sample in samples)
            {
                keyframes.Add(ImportedKeyframe.FromQuaternion(
                    TimeSpan.FromSeconds(sample.Time),
                    CreateRotationOffset(sample.Rotation, bone)));
            }

            AddChannel(bone, ImportedAnimationChannelType.Quaternion, keyframes, channels);
        }
    }

    private static void AddTranslationChannels(
        NifKeyframeData data,
        ImportedBone bone,
        List<ImportedAnimationChannel> channels)
    {
        if (data.TranslationKeys is null)
        {
            return;
        }

        var x = new List<ImportedKeyframe>();
        var y = new List<ImportedKeyframe>();
        var z = new List<ImportedKeyframe>();
        foreach (var key in data.TranslationKeys.Keys)
        {
            var time = TimeSpan.FromSeconds(key.Time);
            var offset = CreateTranslationOffset(key.Value, bone);
            x.Add(ImportedKeyframe.FromFloat(time, offset.X));
            y.Add(ImportedKeyframe.FromFloat(time, offset.Y));
            z.Add(ImportedKeyframe.FromFloat(time, offset.Z));
        }

        AddChannel(bone, ImportedAnimationChannelType.TranslationX, x, channels);
        AddChannel(bone, ImportedAnimationChannelType.TranslationY, y, channels);
        AddChannel(bone, ImportedAnimationChannelType.TranslationZ, z, channels);
    }

    /// <summary>
    /// Converts a scale group into one rest-relative Scale channel. Linear
    /// groups keep their source key times; quadratic groups resample on
    /// the C4c 30 Hz time set through NifKeyCurve. Groups whose emitted
    /// offsets are all the identity scale carry no animation and are
    /// dropped, matching the C4c identity-group rule.
    /// </summary>
    private static void AddScaleChannel(
        NifKeyframeData data,
        ImportedBone bone,
        float duration,
        List<ImportedAnimationChannel> channels)
    {
        if (data.ScaleKeys is not { } group)
        {
            return;
        }

        var keyframes = new List<ImportedKeyframe>();
        if (group.Interpolation == NifKeyType.Linear)
        {
            foreach (var key in group.Keys)
            {
                keyframes.Add(ImportedKeyframe.FromFloat(
                    TimeSpan.FromSeconds(key.Time),
                    key.Value / bone.BindScale));
            }
        }
        else
        {
            foreach (var time in BuildScaleSampleTimes(group, duration))
            {
                keyframes.Add(ImportedKeyframe.FromFloat(
                    TimeSpan.FromSeconds(time),
                    NifKeyCurve.Evaluate(group, time) / bone.BindScale));
            }
        }

        foreach (var keyframe in keyframes)
        {
            if (MathF.Abs(keyframe.FloatValue - 1f) > UnitScaleTolerance)
            {
                AddChannel(bone, ImportedAnimationChannelType.Scale, keyframes, channels);
                return;
            }
        }
    }

    private static List<float> BuildScaleSampleTimes(NifKeyGroup<NifKey<float>> group, float duration)
    {
        var times = new List<float>();
        var stepCount = (int)MathF.Floor(SamplesPerSecond * duration);
        for (var k = 0; k <= stepCount; k++)
        {
            times.Add(k / SamplesPerSecond);
        }

        times.Add(duration);
        foreach (var key in group.Keys)
        {
            times.Add(key.Time);
        }

        times.Sort();

        var deduplicated = new List<float>(times.Count);
        foreach (var time in times)
        {
            if (deduplicated.Count == 0 || time - deduplicated[^1] > SampleTimeEpsilon)
            {
                deduplicated.Add(time);
            }
        }

        return deduplicated;
    }

    private static void AddChannel(
        ImportedBone bone,
        ImportedAnimationChannelType channelType,
        List<ImportedKeyframe> keyframes,
        List<ImportedAnimationChannel> channels)
    {
        channels.Add(new ImportedAnimationChannel(
            ImportedAnimationTarget.CreateBoneName(bone.Name),
            channelType,
            keyframes));
    }

    private static Quaternion CreateRotationOffset(Quaternion sample, ImportedBone bone)
    {
        var engineSample = NifTransformConverter.ConjugateSourceRotationToEngine(sample);
        return Quaternion.Concatenate(engineSample, Quaternion.Inverse(bone.BindRotation));
    }

    private static Vector3 CreateTranslationOffset(Vector3 sample, ImportedBone bone)
    {
        var engineSample = NifTransformConverter.ConvertSourceTranslationToEngine(sample);

        // The runtime composes the offset before the scaled rest linear
        // (row-vector), so undoing the rest applies the inverse rotation
        // first and the inverse rest scale second.
        return Vector3.Transform(
            engineSample - bone.BindTranslation,
            Quaternion.Inverse(bone.BindRotation)) / bone.BindScale;
    }

    private static bool HasXyzRotation(NifKeyframeData data)
    {
        return data.XyzRotationOrder is not null ||
            data.XRotationKeys is not null ||
            data.YRotationKeys is not null ||
            data.ZRotationKeys is not null;
    }

    private static float RotationAngle(Quaternion a, Quaternion b)
    {
        return 2f * MathF.Acos(MathF.Min(1f, MathF.Abs(Quaternion.Dot(a, b))));
    }

    private static NotSupportedException Unsupported(string message)
    {
        return new NotSupportedException($"NIF animation import does not support this clip: {message}.");
    }

    private static NotSupportedException VaryingAccumulationRoot(string targetName, string assetKey)
    {
        return new NotSupportedException(
            $"NIF animation '{assetKey}' accumulation root target '{targetName}' varies; root motion is not supported.");
    }
}
