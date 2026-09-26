using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Utilities;

namespace Panopticon.Graphics.Animation;

public sealed class AnimationInstance
{
    private readonly GameObject _gameObject;
    private readonly int[] _keyframeIndices;
    private readonly ModelBoneInstance[] _boneInstances;
    private readonly BoundModelAnimation _animation;

    private TimeSpan _currentTimeValue;

    private bool _playing;
    private readonly AnimationMode _mode;
    private readonly AnimationFlags _flags;
    private readonly IRandom _random;

    private float _speedFactor;

    private bool Looping => _mode == AnimationMode.Loop || _mode == AnimationMode.LoopBackwards;
    private bool Reverse => _mode == AnimationMode.OnceBackwards || _mode == AnimationMode.LoopBackwards;
    private bool Manual => _mode == AnimationMode.Manual;

    public TimeSpan Duration => _animation.Duration;

    /// <summary>
    /// Construct a new <see cref="AnimationInstance"/>
    /// </summary>
    /// <param name="modelBoneInstances">The bone instances to use, which will be modified as the animation plays</param>
    /// <param name="animation">The bound animation to apply to the bones</param>
    /// <param name="mode">The animation mode</param>
    /// <param name="flags">Additional animation flags</param>
    /// <param name="gameObject"></param>
    /// <param name="random">Random number generator used when combined with <see cref="AnimationFlags.RandomStart"/></param>
    public AnimationInstance(ModelBoneInstance[] modelBoneInstances, BoundModelAnimation animation,
        AnimationMode mode, AnimationFlags flags, GameObject gameObject, IRandom random)
    {
        _animation = animation;
        _mode = mode;
        _flags = flags;
        _boneInstances = modelBoneInstances;
        _keyframeIndices = new int[animation.Clips.Length];
        _gameObject = gameObject;
        _random = random;
    }

    public void SetSpeedFactor(float speedFactor)
    {
        _speedFactor = speedFactor;
    }

    public void Play(float speedFactor = 1.0f)
    {
        _speedFactor = speedFactor;
        if (_playing)
        {
            return;
        }

        ResetTimeStamps();
        ResetBoneTransforms();

        _playing = true;
    }

    public bool IsPlaying => _playing;

    public void Stop()
    {
        if (!_playing)
        {
            return;
        }

        // TODO: Reset to original transforms and visibilities?

        _playing = false;
    }

    private void ResetTimeStamps()
    {
        if (_flags == AnimationFlags.None ||
            _flags.HasFlag(AnimationFlags.StartFrameFirst) ||
            _flags.HasFlag(AnimationFlags.StartFrameLast))
        {
            if (Reverse || _flags.HasFlag(AnimationFlags.StartFrameLast))
            {
                _currentTimeValue = _animation.Clips.Max(c => c.Keyframes.LastOrDefault().Time);
            }
            else
            {
                _currentTimeValue = TimeSpan.Zero;
            }
        }
        else if (_flags.HasFlag(AnimationFlags.RandomStart))
        {
            _currentTimeValue = TimeSpan.FromMilliseconds(_random.Next(0, (int)_animation.Duration.TotalMilliseconds));
        }
        else
        {
            //TODO: implement other flags
            //throw new NotImplementedException();
        }
    }

    private void ResetBoneTransforms()
    {
        Array.Clear(_keyframeIndices, 0, _keyframeIndices.Length);

        if (_flags.HasFlag(AnimationFlags.StartFrameLast) || Reverse && !_flags.HasFlag(AnimationFlags.StartFrameFirst))
        {
            for (var i = 0; i < _keyframeIndices.Length; i++)
            {
                _keyframeIndices[i] = _animation.Clips[i].Keyframes.Length - 1;
            }
        }

        foreach (var bone in _boneInstances)
        {
            bone.AnimatedOffset.Translation = Vector3.Zero;
            bone.AnimatedOffset.Rotation = Quaternion.Identity;
            bone.AnimatedOffset.Scale = 1.0f;
            bone.ClearAbsoluteLocal();

            bone.Visible = true;
        }
    }

    internal void Update(in TimeInterval gameTime)
    {
        if (!_playing)
        {
            return;
        }

        UpdateBoneTransforms(gameTime);
    }

    private void UpdateBoneTransforms(in TimeInterval gameTime)
    {
        //TODO: implement ping pong
        var time = _currentTimeValue;
        var deltaTime = gameTime.DeltaTime * _speedFactor;

        if (Manual)
        {
            // manual can also be used to "hold" an animation, and may have nothing to do with build progress
            if (_flags.HasFlag(AnimationFlags.AdjustHeightByConstructionPercent))
            {
                time = _animation.Duration * _gameObject.BuildProgress * _speedFactor;
            }
        }
        else
        {
            if (Reverse)
            {
                time -= deltaTime;
            }
            else
            {
                time += deltaTime;
            }
        }

        var reachedEnd = Reverse
            ? time < TimeSpan.Zero
            : time >= _animation.Duration;

        if (reachedEnd)
        {
            // If we reached the end, loop back to the start or stay at the last frame
            if (Looping)
            {
                if (Reverse)
                {
                    while (time < TimeSpan.Zero)
                    {
                        time += _animation.Duration;
                    }

                    // If we've just moved forwards, reset the keyframe indices.
                    if (time > _currentTimeValue)
                    {
                        ResetBoneTransforms();
                    }
                }
                else
                {
                    while (time >= _animation.Duration)
                    {
                        time -= _animation.Duration;
                    }

                    // If we've just moved backwards, reset the keyframe indices.
                    if (time < _currentTimeValue)
                    {
                        ResetBoneTransforms();
                    }
                }
            }
            else
            {
                _playing = false;
                if (Reverse)
                {
                    time = TimeSpan.Zero;
                }
                else
                {
                    time = _animation.Duration;
                }
            }
        }

        _currentTimeValue = time;

        for (var i = 0; i < _animation.Clips.Length; i++)
        {
            Keyframe? next = null;

            var clip = _animation.Clips[i];

            if (clip.Bone >= _boneInstances.Length)
            {
                continue;
            }

            // In case we're beyond the animation bounds,
            // default to the end.
            var previous = clip.Keyframes[_keyframeIndices[i]];
            if (previous.Time < _currentTimeValue && Reverse)
            {
                _currentTimeValue = previous.Time - deltaTime;
            }

            if (Reverse)
            {
                for (var j = _keyframeIndices[i]; j >= 0; j--)
                {
                    var keyframe = clip.Keyframes[j];

                    if (keyframe.Time < _currentTimeValue)
                    {
                        next = keyframe;
                        break;
                    }

                    previous = keyframe;
                    _keyframeIndices[i] = j;
                }
            }
            else
            {
                for (var j = _keyframeIndices[i]; j < clip.Keyframes.Length; j++)
                {
                    var keyframe = clip.Keyframes[j];

                    if (keyframe.Time > _currentTimeValue)
                    {
                        next = keyframe;
                        break;
                    }

                    previous = keyframe;
                    _keyframeIndices[i] = j;
                }
            }

            Evaluate(
                clip,
                previous,
                next ?? previous,
                _currentTimeValue);
        }
    }

    private void Evaluate(
        AnimationClip clip,
        in Keyframe previous,
        in Keyframe next,
        TimeSpan currentTime)
    {
        var bone = _boneInstances[clip.Bone];
        var animatedOffset = bone.AnimatedOffset;
        var isAbsolute = _animation.Composition == ModelAnimationComposition.AbsoluteLocal;

        if (clip.Interpolation == AnimationInterpolation.Step)
        {
            // The held value is the keyframe at or before the current time. In forward
            // playback that is `previous`; the reverse walk names it `next`, because it
            // stores the last key below the current time.
            var held = previous.Time <= currentTime ? previous : next;

            switch (clip.ClipType)
            {
                case AnimationClipType.TranslationX:
                    SetTranslationX(bone, animatedOffset, held.Value.FloatValue, isAbsolute);
                    break;

                case AnimationClipType.TranslationY:
                    SetTranslationY(bone, animatedOffset, held.Value.FloatValue, isAbsolute);
                    break;

                case AnimationClipType.TranslationZ:
                    SetTranslationZ(bone, animatedOffset, held.Value.FloatValue, isAbsolute);
                    break;

                case AnimationClipType.Quaternion:
                    SetRotation(bone, animatedOffset, held.Value.Quaternion, isAbsolute);
                    break;

                case AnimationClipType.Visibility:
                    bone.Visible = previous.Value.BoolValue;
                    break;

                case AnimationClipType.Scale:
                    SetUniformScale(bone, animatedOffset, held.Value.FloatValue, isAbsolute);
                    break;

                case AnimationClipType.ScaleX:
                    SetScaleX(bone, held.Value.FloatValue, isAbsolute);
                    break;

                case AnimationClipType.ScaleY:
                    SetScaleY(bone, held.Value.FloatValue, isAbsolute);
                    break;

                case AnimationClipType.ScaleZ:
                    SetScaleZ(bone, held.Value.FloatValue, isAbsolute);
                    break;

                default:
                    throw new InvalidOperationException();
            }

            return;
        }

        var progress = (float)(currentTime - previous.Time).TotalMilliseconds;
        var duration = (float)(next.Time - previous.Time).TotalMilliseconds;

        var amount = (duration != 0)
            ? progress / duration
            : 0;

        switch (clip.ClipType)
        {
            case AnimationClipType.TranslationX:
                SetTranslationX(bone, animatedOffset, MathUtility.Lerp(
                    previous.Value.FloatValue,
                    next.Value.FloatValue,
                    amount), isAbsolute);
                break;

            case AnimationClipType.TranslationY:
                SetTranslationY(bone, animatedOffset, MathUtility.Lerp(
                    previous.Value.FloatValue,
                    next.Value.FloatValue,
                    amount), isAbsolute);
                break;

            case AnimationClipType.TranslationZ:
                SetTranslationZ(bone, animatedOffset, MathUtility.Lerp(
                    previous.Value.FloatValue,
                    next.Value.FloatValue,
                    amount), isAbsolute);
                break;

            case AnimationClipType.Quaternion:
                SetRotation(bone, animatedOffset, Quaternion.Slerp(
                    previous.Value.Quaternion,
                    next.Value.Quaternion,
                    amount), isAbsolute);
                break;

            case AnimationClipType.Visibility:
                bone.Visible = previous.Value.BoolValue;
                break;

            case AnimationClipType.Scale:
                SetUniformScale(bone, animatedOffset, MathUtility.Lerp(
                    previous.Value.FloatValue,
                    next.Value.FloatValue,
                    amount), isAbsolute);
                break;

            case AnimationClipType.ScaleX:
                SetScaleX(bone, MathUtility.Lerp(
                    previous.Value.FloatValue,
                    next.Value.FloatValue,
                    amount), isAbsolute);
                break;

            case AnimationClipType.ScaleY:
                SetScaleY(bone, MathUtility.Lerp(
                    previous.Value.FloatValue,
                    next.Value.FloatValue,
                    amount), isAbsolute);
                break;

            case AnimationClipType.ScaleZ:
                SetScaleZ(bone, MathUtility.Lerp(
                    previous.Value.FloatValue,
                    next.Value.FloatValue,
                    amount), isAbsolute);
                break;

            default:
                throw new InvalidOperationException();
        }
    }

    private static void SetTranslationX(ModelBoneInstance bone, Transform animatedOffset, float value, bool isAbsolute)
    {
        if (isAbsolute)
        {
            bone.AbsoluteLocalTranslation = bone.AbsoluteLocalTranslation.WithX(value);
        }
        else
        {
            animatedOffset.Translation = animatedOffset.Translation.WithX(value);
        }
    }

    private static void SetTranslationY(ModelBoneInstance bone, Transform animatedOffset, float value, bool isAbsolute)
    {
        if (isAbsolute)
        {
            bone.AbsoluteLocalTranslation = bone.AbsoluteLocalTranslation.WithY(value);
        }
        else
        {
            animatedOffset.Translation = animatedOffset.Translation.WithY(value);
        }
    }

    private static void SetTranslationZ(ModelBoneInstance bone, Transform animatedOffset, float value, bool isAbsolute)
    {
        if (isAbsolute)
        {
            bone.AbsoluteLocalTranslation = bone.AbsoluteLocalTranslation.WithZ(value);
        }
        else
        {
            animatedOffset.Translation = animatedOffset.Translation.WithZ(value);
        }
    }

    private static void SetRotation(ModelBoneInstance bone, Transform animatedOffset, Quaternion value, bool isAbsolute)
    {
        if (isAbsolute)
        {
            bone.AbsoluteLocalRotation = value;
        }
        else
        {
            animatedOffset.Rotation = value;
        }
    }

    private static void SetUniformScale(ModelBoneInstance bone, Transform animatedOffset, float value, bool isAbsolute)
    {
        if (isAbsolute)
        {
            bone.AbsoluteLocalScale = new Vector3(value);
        }
        else
        {
            animatedOffset.Scale = value;
        }
    }

    private static void SetScaleX(ModelBoneInstance bone, float value, bool isAbsolute)
    {
        if (!isAbsolute)
        {
            throw new InvalidOperationException("Per-axis scale clips require absolute-local animation composition.");
        }

        bone.AbsoluteLocalScale = bone.AbsoluteLocalScale.WithX(value);
    }

    private static void SetScaleY(ModelBoneInstance bone, float value, bool isAbsolute)
    {
        if (!isAbsolute)
        {
            throw new InvalidOperationException("Per-axis scale clips require absolute-local animation composition.");
        }

        bone.AbsoluteLocalScale = bone.AbsoluteLocalScale.WithY(value);
    }

    private static void SetScaleZ(ModelBoneInstance bone, float value, bool isAbsolute)
    {
        if (!isAbsolute)
        {
            throw new InvalidOperationException("Per-axis scale clips require absolute-local animation composition.");
        }

        bone.AbsoluteLocalScale = bone.AbsoluteLocalScale.WithZ(value);
    }

    internal void DrawInspector()
    {
        if (ImGui.TreeNodeEx("Animation", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.LabelText("Name", _animation.Name);
            ImGui.LabelText("Playing", _playing.ToString());

            ImGui.TreePop();
        }
    }
}
