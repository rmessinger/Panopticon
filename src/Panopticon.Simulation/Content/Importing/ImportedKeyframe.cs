#nullable enable

using System;
using System.Numerics;
using Panopticon;

namespace Panopticon.Content.Importing;

public enum ImportedKeyframeKind
{
    Float,
    Quaternion,
    Bool,
    Vector3,
}

/// <summary>
/// One keyframe: time and a typed value. The kind is fixed at construction; reading the wrong
/// accessor throws.
/// </summary>
public sealed class ImportedKeyframe
{
    public TimeSpan Time { get; }

    public ImportedKeyframeKind Kind { get; }

    private readonly object _value;

    public float FloatValue => ValueAs<float>(ImportedKeyframeKind.Float);

    public Quaternion QuaternionValue => ValueAs<Quaternion>(ImportedKeyframeKind.Quaternion);

    public bool BoolValue => ValueAs<bool>(ImportedKeyframeKind.Bool);

    public Vector3 Vector3Value => ValueAs<Vector3>(ImportedKeyframeKind.Vector3);

    private ImportedKeyframe(TimeSpan time, ImportedKeyframeKind kind, object value)
    {
        Ensure.AtLeast(time, TimeSpan.Zero, "Keyframe time must not be negative.");

        Time = time;
        Kind = kind;
        _value = value;
    }

    public static ImportedKeyframe FromFloat(TimeSpan time, float value)
    {
        return new ImportedKeyframe(time, ImportedKeyframeKind.Float, value);
    }

    public static ImportedKeyframe FromQuaternion(TimeSpan time, in Quaternion value)
    {
        return new ImportedKeyframe(time, ImportedKeyframeKind.Quaternion, value);
    }

    public static ImportedKeyframe FromBool(TimeSpan time, bool value)
    {
        return new ImportedKeyframe(time, ImportedKeyframeKind.Bool, value);
    }

    public static ImportedKeyframe FromVector3(TimeSpan time, in Vector3 value)
    {
        return new ImportedKeyframe(time, ImportedKeyframeKind.Vector3, value);
    }

    public static ImportedKeyframeKind GetExpectedKind(ImportedAnimationChannelType channelType)
    {
        return channelType switch
        {
            ImportedAnimationChannelType.TranslationX
            or ImportedAnimationChannelType.TranslationY
            or ImportedAnimationChannelType.TranslationZ => ImportedKeyframeKind.Float,

            ImportedAnimationChannelType.Quaternion => ImportedKeyframeKind.Quaternion,

            ImportedAnimationChannelType.Scale => ImportedKeyframeKind.Float,

            ImportedAnimationChannelType.Visibility => ImportedKeyframeKind.Bool,

            ImportedAnimationChannelType.TextureTranslateU
            or ImportedAnimationChannelType.TextureTranslateV
            or ImportedAnimationChannelType.TextureScaleU
            or ImportedAnimationChannelType.TextureScaleV
            or ImportedAnimationChannelType.TextureRotate
            or ImportedAnimationChannelType.MaterialAlpha => ImportedKeyframeKind.Float,

            ImportedAnimationChannelType.MaterialAmbientColor
            or ImportedAnimationChannelType.MaterialDiffuseColor
            or ImportedAnimationChannelType.MaterialSpecularColor
            or ImportedAnimationChannelType.MaterialEmissiveColor => ImportedKeyframeKind.Vector3,

            _ => throw new ArgumentOutOfRangeException(nameof(channelType)),
        };
    }

    private T ValueAs<T>(ImportedKeyframeKind expectedKind)
    {
        if (Kind != expectedKind)
        {
            throw new InvalidOperationException(
                $"Keyframe at {Time} holds a {Kind} value, not {expectedKind}.");
        }

        return (T)_value;
    }
}
