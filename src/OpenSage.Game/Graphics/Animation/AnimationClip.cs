namespace OpenSage.Graphics.Animation;

public sealed class AnimationClip
{
    public AnimationClipType ClipType { get; }
    public AnimationInterpolation Interpolation { get; }
    public int Bone { get; }
    public Keyframe[] Keyframes { get; }

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
    }
}
