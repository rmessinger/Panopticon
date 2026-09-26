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
/// Converts C4e property controllers (visibility, texture transform,
/// material color, material alpha) from a controller sequence into
/// imported animation channels, resolving targets through the model's
/// property target map. Visibility targets fall back to verbatim
/// sub-object names when the model has no matching bone or shape, so
/// no channel is silently dropped. Morph controllers stay C6 work.
/// </summary>
internal static class NifPropertyAnimationConverter
{
    private const float TimeTolerance = 1e-4f;

    private static readonly ushort[] VisFlags = [0x2C, 0x25, 0x21];
    private static readonly ushort[] AlphaFlags = [0x28];
    private static readonly ushort[] TexTransformFlags = [0x2C, 0x28];
    private static readonly ushort[] MaterialColorFlags = [0x2C, 0x28];

    public static void Convert(
        NifFile file,
        NifTimeController controller,
        string targetName,
        ImportedSkeleton skeleton,
        NifPropertyTargetMap targets,
        string animationName,
        float duration,
        List<ImportedAnimationChannel> channels)
    {
        switch (controller)
        {
            case NifVisController vis:
                ValidateTiming(vis, targetName, duration, animationName);
                ValidateFlags(vis.Flags, VisFlags, targetName, "visibility");
                ConvertVis(file, vis, targetName, skeleton, targets, animationName, duration, channels);
                break;
            case NifTextureTransformController texTransform:
                ValidateTiming(texTransform, targetName, duration, animationName);
                ValidateFlags(texTransform.Flags, TexTransformFlags, targetName, "texture transform");
                ConvertTextureTransform(file, texTransform, targetName, targets, animationName, duration, channels);
                break;
            case NifMaterialColorController materialColor:
                ValidateTiming(materialColor, targetName, duration, animationName);
                ValidateFlags(materialColor.Flags, MaterialColorFlags, targetName, "material color");
                ConvertMaterialColor(file, materialColor, targetName, targets, animationName, duration, channels);
                break;
            case NifAlphaController alpha:
                ValidateTiming(alpha, targetName, duration, animationName);
                ValidateFlags(alpha.Flags, AlphaFlags, targetName, "alpha");
                ConvertAlpha(file, alpha, targetName, targets, animationName, duration, channels);
                break;
            case NifGeomMorpherController:
                throw Unsupported(
                    $"target '{targetName}' uses a NiGeomMorpherController; morph animation is C6 work");
            default:
                throw Unsupported(
                    $"target '{targetName}' uses controller family '{controller.GetType().Name}'");
        }
    }

    private static void ConvertVis(
        NifFile file,
        NifVisController controller,
        string targetName,
        ImportedSkeleton skeleton,
        NifPropertyTargetMap targets,
        string animationName,
        float duration,
        List<ImportedAnimationChannel> channels)
    {
        List<(float Time, bool Visible)> keys;
        if (file.Blocks[controller.DataReference] is NifVisData visData)
        {
            keys = visData.Keys.Select(key => (key.Time, key.Visible != 0)).ToList();
        }
        else
        {
            var boolData = (NifBoolData)file.Blocks[controller.DataReference];
            keys = boolData.Keys.Keys.Select(key => (key.Time, key.Value != 0)).ToList();
        }

        if (keys.Count == 0)
        {
            throw new InvalidDataException(
                $"NIF animation '{animationName}' visibility target '{targetName}' has no keys.");
        }

        ValidateTimes(keys.Select(key => key.Time), targetName, duration, animationName, "visibility");

        var target = ResolveVisibilityTarget(targetName, skeleton, targets);
        var keyframes = keys.Select(
            key => ImportedKeyframe.FromBool(TimeSpan.FromSeconds(key.Time), key.Visible)).ToList();
        channels.Add(new ImportedAnimationChannel(
            target,
            ImportedAnimationChannelType.Visibility,
            keyframes,
            ImportedAnimationInterpolation.Step));
    }

    private static ImportedAnimationTarget ResolveVisibilityTarget(
        string targetName,
        ImportedSkeleton skeleton,
        NifPropertyTargetMap targets)
    {
        for (var i = 0; i < skeleton.Bones.Count; i++)
        {
            if (string.Equals(skeleton.Bones[i].Name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return ImportedAnimationTarget.CreateBoneName(skeleton.Bones[i].Name);
            }
        }

        if (targets.TryResolveShapeSubObject(targetName, out var subObjectFullName))
        {
            return ImportedAnimationTarget.CreateSubObjectName(subObjectFullName);
        }

        return ImportedAnimationTarget.CreateSubObjectName(targetName);
    }

    private static void ConvertTextureTransform(
        NifFile file,
        NifTextureTransformController controller,
        string targetName,
        NifPropertyTargetMap targets,
        string animationName,
        float duration,
        List<ImportedAnimationChannel> channels)
    {
        if (controller.ShaderMap)
        {
            throw Unsupported($"texture transform target '{targetName}' uses a shader map");
        }

        if (controller.TextureSlot != 0)
        {
            throw Unsupported(
                $"texture transform target '{targetName}' addresses texture slot {controller.TextureSlot}; only slot 0 is supported");
        }

        var channelType = controller.Operation switch
        {
            NifTextureTransformOp.TranslateU => ImportedAnimationChannelType.TextureTranslateU,
            NifTextureTransformOp.TranslateV => ImportedAnimationChannelType.TextureTranslateV,
            _ => throw Unsupported(
                $"texture transform target '{targetName}' uses operation {controller.Operation}; only TT_TRANSLATE_U and TT_TRANSLATE_V are supported"),
        };

        var data = (NifFloatData)file.Blocks[controller.DataReference];
        var keys = ReadFloatKeys(data, targetName, "texture transform", animationName);
        var slots = ResolveSlots(targetName, "NiTexturingProperty", targets);
        AddSlotChannels(slots, channelType, keys, channels);
    }

    private static void ConvertMaterialColor(
        NifFile file,
        NifMaterialColorController controller,
        string targetName,
        NifPropertyTargetMap targets,
        string animationName,
        float duration,
        List<ImportedAnimationChannel> channels)
    {
        var channelType = controller.TargetColor switch
        {
            NifMaterialColorTarget.Ambient => ImportedAnimationChannelType.MaterialAmbientColor,
            NifMaterialColorTarget.Diffuse => ImportedAnimationChannelType.MaterialDiffuseColor,
            NifMaterialColorTarget.Specular => ImportedAnimationChannelType.MaterialSpecularColor,
            NifMaterialColorTarget.Emissive => ImportedAnimationChannelType.MaterialEmissiveColor,
            _ => throw Unsupported(
                $"material color target '{targetName}' addresses color {controller.TargetColor}; only ambient, diffuse, specular and emissive are supported"),
        };

        var data = (NifPosData)file.Blocks[controller.DataReference];
        if (data.Keys is null || data.Keys.Keys.Count == 0)
        {
            throw new InvalidDataException(
                $"NIF animation '{animationName}' material color target '{targetName}' has no keys.");
        }

        ValidateTimes(
            data.Keys.Keys.Select(key => key.Time),
            targetName,
            duration,
            animationName,
            "material color");
        var keys = data.Keys.Keys.Select(
            key => (key.Time, key.Value)).ToList();
        var slots = ResolveSlots(targetName, "NiMaterialProperty", targets);
        AddVector3SlotChannels(slots, channelType, keys, channels);
    }

    private static void ConvertAlpha(
        NifFile file,
        NifAlphaController controller,
        string targetName,
        NifPropertyTargetMap targets,
        string animationName,
        float duration,
        List<ImportedAnimationChannel> channels)
    {
        var keys = ReadFloatKeys((NifFloatData)file.Blocks[controller.DataReference], targetName, "alpha", animationName);
        foreach (var (_, value) in keys)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
            {
                throw new InvalidDataException(
                    $"NIF animation '{animationName}' alpha target '{targetName}' has a key with value {value.ToString(CultureInfo.InvariantCulture)}, outside [0, 1].");
            }
        }

        var slots = ResolveSlots(targetName, "NiMaterialProperty", targets);
        AddSlotChannels(slots, ImportedAnimationChannelType.MaterialAlpha, keys, channels);
    }

    private static List<(float Time, float Value)> ReadFloatKeys(
        NifFloatData data,
        string targetName,
        string family,
        string animationName)
    {
        if (data.Keys is null || data.Keys.Keys.Count == 0)
        {
            throw new InvalidDataException(
                $"NIF animation '{animationName}' {family} target '{targetName}' has no keys.");
        }

        return data.Keys.Keys.Select(key => (key.Time, key.Value)).ToList();
    }

    private static void ValidateTimes(
        IEnumerable<float> times,
        string targetName,
        float duration,
        string animationName,
        string family)
    {
        var previous = float.NegativeInfinity;
        foreach (var time in times)
        {
            if (!float.IsFinite(time) ||
                time < -TimeTolerance ||
                time > duration + TimeTolerance ||
                time < previous)
            {
                throw new InvalidDataException(
                    $"NIF animation '{animationName}' {family} target '{targetName}' has a key at {time.ToString(CultureInfo.InvariantCulture)} s, outside the clip range 0..{duration.ToString(CultureInfo.InvariantCulture)} or out of order.");
            }

            previous = time;
        }
    }

    private static IReadOnlyList<NifMaterialSlotTarget> ResolveSlots(
        string targetName,
        string propertyTypeName,
        NifPropertyTargetMap targets)
    {
        if (!NifPropertyTargetMap.TryParsePropertyTarget(targetName, out var shapeName, out var parsedTypeName))
        {
            throw new InvalidDataException(
                $"NIF animation target '{targetName}' is not a property target; expected '<shape>\\nPROP\\n{propertyTypeName}'.");
        }

        return targets.ResolvePropertySlots(shapeName, parsedTypeName);
    }

    private static void AddSlotChannels(
        IReadOnlyList<NifMaterialSlotTarget> slots,
        ImportedAnimationChannelType channelType,
        List<(float Time, float Value)> keys,
        List<ImportedAnimationChannel> channels)
    {
        foreach (var slot in slots)
        {
            var keyframes = keys.Select(
                key => ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(key.Time), key.Value)).ToList();
            channels.Add(new ImportedAnimationChannel(
                ImportedAnimationTarget.CreateMaterialSlot(slot.SubObjectFullName, slot.MaterialIndex),
                channelType,
                keyframes));
        }
    }

    private static void AddVector3SlotChannels(
        IReadOnlyList<NifMaterialSlotTarget> slots,
        ImportedAnimationChannelType channelType,
        List<(float Time, Vector3 Value)> keys,
        List<ImportedAnimationChannel> channels)
    {
        foreach (var slot in slots)
        {
            var keyframes = keys.Select(
                key => ImportedKeyframe.FromVector3(TimeSpan.FromSeconds(key.Time), key.Value)).ToList();
            channels.Add(new ImportedAnimationChannel(
                ImportedAnimationTarget.CreateMaterialSlot(slot.SubObjectFullName, slot.MaterialIndex),
                channelType,
                keyframes));
        }
    }

    private static void ValidateTiming(
        NifTimeController controller,
        string targetName,
        float duration,
        string assetKey)
    {
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

    private static void ValidateFlags(
        ushort flags,
        ushort[] allowed,
        string targetName,
        string family)
    {
        foreach (var candidate in allowed)
        {
            if (flags == candidate)
            {
                return;
            }
        }

        var list = string.Join(
            ", ",
            allowed.Select(flag => "0x" + flag.ToString("X2", CultureInfo.InvariantCulture)));
        throw Unsupported(
            $"target '{targetName}' has {family} controller flags 0x{flags.ToString("X4", CultureInfo.InvariantCulture)} instead of one of {list}");
    }

    private static NotSupportedException Unsupported(string message)
    {
        return new NotSupportedException($"NIF animation import does not support this clip: {message}.");
    }
}
