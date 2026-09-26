namespace Panopticon.Graphics.Animation;

/// <summary>What a clip's integer target indices address: a bone, a sub-object, or a material slot.</summary>
public enum AnimationClipTarget
{
    Bone,
    SubObject,
    MaterialSlot,
}

public sealed class AnimationClip
{
    public AnimationClipType ClipType { get; }
    public AnimationInterpolation Interpolation { get; }
    public int Bone { get; }
    public Keyframe[] Keyframes { get; }

    public AnimationClipTarget TargetKind { get; }

    /// <summary>Sub-object index within the owning model; -1 for bone clips.</summary>
    public int SubObjectIndex { get; }

    /// <summary>Material index within the sub-object's mesh; -1 unless the target is a material slot.</summary>
    public int MaterialIndex { get; }

    internal AnimationClip(
        AnimationClipType clipType,
        int bone,
        Keyframe[] keyframes,
        AnimationInterpolation interpolation)
    {
        ClipType = clipType;
        Bone = bone;
        Keyframes = keyframes;
        Interpolation = interpolation;
        TargetKind = AnimationClipTarget.Bone;
        SubObjectIndex = -1;
        MaterialIndex = -1;
    }

    internal AnimationClip(
        AnimationClipType clipType,
        AnimationClipTarget targetKind,
        int subObjectIndex,
        int materialIndex,
        Keyframe[] keyframes,
        AnimationInterpolation interpolation)
    {
        ClipType = clipType;
        TargetKind = targetKind;
        SubObjectIndex = subObjectIndex;
        MaterialIndex = materialIndex;
        Keyframes = keyframes;
        Interpolation = interpolation;
        Bone = -1;
    }
}
