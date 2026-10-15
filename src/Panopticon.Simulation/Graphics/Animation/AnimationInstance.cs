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
    private readonly int[] _morphKeyframeIndices;
    private readonly ModelBoneInstance[] _boneInstances;
    private readonly BoundModelAnimation _animation;
    private readonly ModelPropertyAnimationState _propertyState;
    private readonly ModelMeshInstance[] _meshInstances;

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
    /// <param name="propertyState">Receives sampled sub-object visibility and material-slot values; null for bone-only animations</param>
    public AnimationInstance(ModelBoneInstance[] modelBoneInstances, BoundModelAnimation animation,
        AnimationMode mode, AnimationFlags flags, GameObject gameObject, IRandom random)
        : this(modelBoneInstances, animation, mode, flags, gameObject, random, propertyState: null)
    {
    }

    internal AnimationInstance(ModelBoneInstance[] modelBoneInstances, BoundModelAnimation animation,
        AnimationMode mode, AnimationFlags flags, GameObject gameObject, IRandom random,
        ModelPropertyAnimationState propertyState, ModelMeshInstance[] meshInstances = null)
    {
        if (animation.MorphTracks.Length > 0 &&
            (meshInstances is null || meshInstances.Length == 0))
        {
            throw new ArgumentException(
                "Animations with morph tracks require the model's mesh instances.",
                nameof(meshInstances));
        }

        _animation = animation;
        _mode = mode;
        _flags = flags;
        _boneInstances = modelBoneInstances;
        _keyframeIndices = new int[animation.Clips.Length];
        _meshInstances = meshInstances ?? Array.Empty<ModelMeshInstance>();
        _morphKeyframeIndices = new int[animation.MorphTracks.Length];
        _gameObject = gameObject;
        _random = random;
        _propertyState = propertyState;
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

            bone.Visible = true;
        }

        _propertyState?.Reset();

        Array.Clear(_morphKeyframeIndices, 0, _morphKeyframeIndices.Length);
        if (_flags.HasFlag(AnimationFlags.StartFrameLast) || Reverse && !_flags.HasFlag(AnimationFlags.StartFrameFirst))
        {
            for (var i = 0; i < _morphKeyframeIndices.Length; i++)
            {
                _morphKeyframeIndices[i] = _animation.MorphTracks[i].Keyframes.Length - 1;
            }
        }

        foreach (var track in _animation.MorphTracks)
        {
            _meshInstances[track.SubObjectIndex].ResetMorphWeightsToDefault();
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

            if (clip.TargetKind == AnimationClipTarget.Bone && clip.Bone >= _boneInstances.Length)
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

        for (var i = 0; i < _animation.MorphTracks.Length; i++)
        {
            var track = _animation.MorphTracks[i];
            MorphWeightKeyframe? next = null;

            var previous = track.Keyframes[_morphKeyframeIndices[i]];

            if (Reverse)
            {
                for (var j = _morphKeyframeIndices[i]; j >= 0; j--)
                {
                    var keyframe = track.Keyframes[j];

                    if (keyframe.Time < _currentTimeValue)
                    {
                        next = keyframe;
                        break;
                    }

                    previous = keyframe;
                    _morphKeyframeIndices[i] = j;
                }
            }
            else
            {
                for (var j = _morphKeyframeIndices[i]; j < track.Keyframes.Length; j++)
                {
                    var keyframe = track.Keyframes[j];

                    if (keyframe.Time > _currentTimeValue)
                    {
                        next = keyframe;
                        break;
                    }

                    previous = keyframe;
                    _morphKeyframeIndices[i] = j;
                }
            }

            EvaluateMorph(
                track,
                previous,
                next ?? previous,
                _currentTimeValue);
        }
    }

    private void EvaluateMorph(
        MorphWeightTrack track,
        in MorphWeightKeyframe previous,
        in MorphWeightKeyframe next,
        TimeSpan currentTime)
    {
        var mesh = _meshInstances[track.SubObjectIndex];

        float[] vector;
        if (track.Interpolation == AnimationInterpolation.Step || next.Time == previous.Time)
        {
            vector = previous.Weights;
        }
        else
        {
            var span = (next.Time - previous.Time).TotalSeconds;
            var amount = span <= 0 ? 0 : (currentTime - previous.Time).TotalSeconds / span;
            if (amount < 0)
            {
                amount = 0;
            }

            if (amount > 1)
            {
                amount = 1;
            }

            vector = new float[previous.Weights.Length];
            for (var j = 0; j < vector.Length; j++)
            {
                vector[j] = MathUtility.Lerp(previous.Weights[j], next.Weights[j], (float)amount);
            }
        }

        var weights = new float[mesh.MorphWeightCount];
        mesh.CopyMorphDefaultWeights(weights);
        for (var t = 0; t < track.TargetSlots.Length; t++)
        {
            weights[track.TargetSlots[t]] = vector[t];
        }

        mesh.SetMorphWeights(weights);
    }

    private void Evaluate(
        AnimationClip clip,
        in Keyframe previous,
        in Keyframe next,
        TimeSpan currentTime)
    {
        if (clip.TargetKind != AnimationClipTarget.Bone)
        {
            EvaluateProperty(clip, previous, next, currentTime);
            return;
        }

        var animatedOffset = _boneInstances[clip.Bone].AnimatedOffset;

        if (clip.Interpolation == AnimationInterpolation.Step)
        {
            // The held value is the keyframe at or before the current time. In forward
            // playback that is `previous`; the reverse walk names it `next`, because it
            // stores the last key below the current time.
            var held = previous.Time <= currentTime ? previous : next;

            switch (clip.ClipType)
            {
                case AnimationClipType.TranslationX:
                    animatedOffset.Translation = animatedOffset.Translation.WithX(held.Value.FloatValue);
                    break;

                case AnimationClipType.TranslationY:
                    animatedOffset.Translation = animatedOffset.Translation.WithY(held.Value.FloatValue);
                    break;

                case AnimationClipType.TranslationZ:
                    animatedOffset.Translation = animatedOffset.Translation.WithZ(held.Value.FloatValue);
                    break;

                case AnimationClipType.Quaternion:
                    _boneInstances[clip.Bone].AnimatedOffset.Rotation = held.Value.Quaternion;
                    break;

                case AnimationClipType.Scale:
                    _boneInstances[clip.Bone].AnimatedOffset.Scale = held.Value.FloatValue;
                    break;

                case AnimationClipType.Visibility:
                    _boneInstances[clip.Bone].Visible = previous.Value.BoolValue;
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
                animatedOffset.Translation = animatedOffset.Translation.WithX(MathUtility.Lerp(
                    previous.Value.FloatValue,
                    next.Value.FloatValue,
                    amount));
                break;

            case AnimationClipType.TranslationY:
                animatedOffset.Translation = animatedOffset.Translation.WithY(MathUtility.Lerp(
                    previous.Value.FloatValue,
                    next.Value.FloatValue,
                    amount));
                break;

            case AnimationClipType.TranslationZ:
                animatedOffset.Translation = animatedOffset.Translation.WithZ(MathUtility.Lerp(
                    previous.Value.FloatValue,
                    next.Value.FloatValue,
                    amount));
                break;

            case AnimationClipType.Quaternion:
                _boneInstances[clip.Bone].AnimatedOffset.Rotation = Quaternion.Slerp(
                    previous.Value.Quaternion,
                    next.Value.Quaternion,
                    amount);
                break;

            case AnimationClipType.Scale:
                _boneInstances[clip.Bone].AnimatedOffset.Scale = MathUtility.Lerp(
                    previous.Value.FloatValue,
                    next.Value.FloatValue,
                    amount);
                break;

            case AnimationClipType.Visibility:
                _boneInstances[clip.Bone].Visible = previous.Value.BoolValue;
                break;

            default:
                throw new InvalidOperationException();
        }
    }

    private void EvaluateProperty(
        AnimationClip clip,
        in Keyframe previous,
        in Keyframe next,
        TimeSpan currentTime)
    {
        if (_propertyState is null)
        {
            throw new InvalidOperationException(
                $"Animation '{_animation.Name}' evaluates a {clip.TargetKind} clip without property animation state.");
        }

        if (clip.TargetKind == AnimationClipTarget.SubObject)
        {
            // Visibility is step-keyed by construction; hold the key at or before the current time.
            var held = previous.Time <= currentTime ? previous : next;
            _propertyState.SetSubObjectVisible(clip.SubObjectIndex, held.Value.BoolValue);
            return;
        }

        if (clip.Interpolation == AnimationInterpolation.Step)
        {
            var held = previous.Time <= currentTime ? previous : next;
            WriteSlotValue(clip, held.Value);
            return;
        }

        var progress = (float)(currentTime - previous.Time).TotalMilliseconds;
        var duration = (float)(next.Time - previous.Time).TotalMilliseconds;

        var amount = (duration != 0)
            ? progress / duration
            : 0;

        switch (clip.ClipType)
        {
            case AnimationClipType.MaterialAmbientColor:
            case AnimationClipType.MaterialDiffuseColor:
            case AnimationClipType.MaterialSpecularColor:
            case AnimationClipType.MaterialEmissiveColor:
                WriteSlotValue(clip, new KeyframeValue
                {
                    Vector3Value = Vector3.Lerp(previous.Value.Vector3Value, next.Value.Vector3Value, amount),
                });
                break;

            default:
                WriteSlotValue(clip, new KeyframeValue
                {
                    FloatValue = MathUtility.Lerp(previous.Value.FloatValue, next.Value.FloatValue, amount),
                });
                break;
        }
    }

    private void WriteSlotValue(AnimationClip clip, KeyframeValue value)
    {
        ref var slot = ref _propertyState!.GetSlot(clip.SubObjectIndex, clip.MaterialIndex);

        switch (clip.ClipType)
        {
            case AnimationClipType.MaterialAmbientColor:
                slot.Ambient = value.Vector3Value;
                slot.OverrideFlags |= (uint)ModelMaterialOverrideFlags.Ambient;
                break;

            case AnimationClipType.MaterialDiffuseColor:
                slot.Diffuse = value.Vector3Value;
                slot.OverrideFlags |= (uint)ModelMaterialOverrideFlags.Diffuse;
                break;

            case AnimationClipType.MaterialSpecularColor:
                slot.Specular = value.Vector3Value;
                slot.OverrideFlags |= (uint)ModelMaterialOverrideFlags.Specular;
                break;

            case AnimationClipType.MaterialEmissiveColor:
                slot.Emissive = value.Vector3Value;
                slot.OverrideFlags |= (uint)ModelMaterialOverrideFlags.Emissive;
                break;

            case AnimationClipType.MaterialAlpha:
                slot.Alpha = value.FloatValue;
                slot.OverrideFlags |= (uint)ModelMaterialOverrideFlags.Alpha;
                break;

            case AnimationClipType.TextureTranslateU:
                slot.UvTranslateU = value.FloatValue;
                slot.OverrideFlags |= (uint)ModelMaterialOverrideFlags.UvTranslate;
                break;

            case AnimationClipType.TextureTranslateV:
                slot.UvTranslateV = value.FloatValue;
                slot.OverrideFlags |= (uint)ModelMaterialOverrideFlags.UvTranslate;
                break;

            case AnimationClipType.TextureScaleU:
                slot.UvScaleU = value.FloatValue;
                slot.OverrideFlags |= (uint)ModelMaterialOverrideFlags.UvScale;
                break;

            case AnimationClipType.TextureScaleV:
                slot.UvScaleV = value.FloatValue;
                slot.OverrideFlags |= (uint)ModelMaterialOverrideFlags.UvScale;
                break;

            case AnimationClipType.TextureRotate:
                slot.UvRotation = value.FloatValue;
                slot.OverrideFlags |= (uint)ModelMaterialOverrideFlags.UvRotate;
                break;

            default:
                throw new InvalidOperationException();
        }
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
