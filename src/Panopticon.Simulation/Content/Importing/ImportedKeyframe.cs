#nullable enable

using System;
using System.Numerics;

namespace Panopticon.Content.Importing;

public enum ImportedKeyframeKind
{
    Float,
    Quaternion,
    Bool,
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

    private ImportedKeyframe(TimeSpan time, ImportedKeyframeKind kind, object value)
    {
        if (time < TimeSpan.Zero)
        {
            throw new ArgumentException("Keyframe time must not be negative.", nameof(time));
        }

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

    public static ImportedKeyframeKind GetExpectedKind(ImportedAnimationChannelType channelType)
    {
        return channelType switch
        {
            ImportedAnimationChannelType.TranslationX
            or ImportedAnimationChannelType.TranslationY
            or ImportedAnimationChannelType.TranslationZ
            or ImportedAnimationChannelType.Scale
            or ImportedAnimationChannelType.ScaleX
            or ImportedAnimationChannelType.ScaleY
            or ImportedAnimationChannelType.ScaleZ => ImportedKeyframeKind.Float,

            ImportedAnimationChannelType.Quaternion => ImportedKeyframeKind.Quaternion,

            ImportedAnimationChannelType.Visibility => ImportedKeyframeKind.Bool,

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
