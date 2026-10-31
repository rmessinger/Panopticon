#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.FileFormats.Granny;

namespace Panopticon.Content.Importing.Granny;

/// <summary>
/// Imports animations from uncompressed Granny GR2 clips (importer id GR2). Every
/// transform track becomes up to five channels on a bone-name target: translation
/// X/Y/Z, rotation, and uniform scale. Sample values pass through the clip's
/// ArtToolInfo axis system as full local transforms, matching the skeleton import.
/// Track names normalize spaces to underscores, the naming the skeletons use.
/// </summary>
internal sealed class GrannyAnimationImporter : IAnimationImporter
{
    private const int MaxSamplesPerTrack = 10_000;

    public string Id => "GR2";

    public GrannyImportDiagnostics Diagnostics { get; } = new();

    private static GrannyAxisSystem IdentityAxes { get; } = new(
        1f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    public ImportedAnimation? ImportAnimation(string name, AssetImportContext context)
    {
        // Granny clips store absolute local transforms, but the runtime animation contract
        // composes channels as offsets on the bind pose; converting requires the target
        // skeleton's bind, so the skeleton-less overload cannot produce honest channels.
        throw new InvalidDataException(
            $"Granny animation '{name}': import requires the target model's skeleton " +
            "(use the ImportAnimation overload that passes it) to convert absolute clip locals " +
            "into bind-relative offsets.");
    }

    public ImportedAnimation? ImportAnimation(string name, AssetImportContext context, ImportedSkeleton targetSkeleton)
    {
        ArgumentNullException.ThrowIfNull(targetSkeleton);

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var entry = context.FileSystem.GetFile(name);
        if (entry is null)
        {
            return null;
        }

        GrannyObject root;
        using (var stream = entry.Open())
        {
            var buffered = new MemoryStream();
            stream.CopyTo(buffered);
            buffered.Position = 0;
            try
            {
                root = GrannyFile.Read(buffered, name).Root;
            }
            catch (GrannyCompressedSectionException ex)
            {
                throw new InvalidDataException(
                    $"Granny animation '{name}' cannot be imported: {ex.Message}", ex);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"Granny animation '{name}' cannot be imported: {ex.Message}", ex);
            }
            catch (NotSupportedException ex)
            {
                throw new InvalidDataException($"Granny animation '{name}' cannot be imported: {ex.Message}", ex);
            }
        }

        var animations = root.GetObjects("Animations");
        if (animations.Count == 0)
        {
            throw new InvalidDataException($"Granny animation '{name}': the file has no Animations object.");
        }

        var animation = animations[0];
        var durationSeconds = animation.GetSingle("Duration")
            ?? throw new InvalidDataException($"Granny animation '{name}': the animation has no Duration.");
        var timeStepSeconds = animation.GetSingle("TimeStep")
            ?? throw new InvalidDataException($"Granny animation '{name}': the animation has no TimeStep.");
        if (timeStepSeconds <= 0f || durationSeconds < 0f)
        {
            throw new InvalidDataException(
                $"Granny animation '{name}': invalid timing (duration {durationSeconds}, time step {timeStepSeconds}).");
        }

        var axes = root.GetObject("ArtToolInfo") is { } artToolInfo
            ? GrannyAxisSystem.FromArtToolInfo(artToolInfo)
            : IdentityAxes;

        var channels = new List<ImportedAnimationChannel>();
        var bindLocals = BuildBindLocals(targetSkeleton);
        var unitScale = ComputeUnitScale(animation, name, axes, bindLocals);
        foreach (var trackGroup in animation.GetObjects("TrackGroups"))
        {
            ImportTrackGroup(trackGroup, name, axes, durationSeconds, timeStepSeconds, bindLocals, unitScale, channels);
        }

        return new ImportedAnimation(name, TimeSpan.FromSeconds(durationSeconds), channels);
    }

    /// <summary>
    /// Engine-space bind locals by normalized bone name. Clip tracks carry absolute local
    /// transforms authored against the same rig; the runtime composes animation channels as
    /// offsets on the bind pose (AnimatedOffset * bind), so channels must carry
    /// animatedLocal * bind⁻¹.
    /// </summary>
    private static Dictionary<string, Matrix4x4> BuildBindLocals(ImportedSkeleton skeleton)
    {
        var binds = new Dictionary<string, Matrix4x4>(skeleton.Bones.Count, StringComparer.Ordinal);
        foreach (var bone in skeleton.Bones)
        {
            var local = Matrix4x4.CreateScale(bone.BindScale)
                * Matrix4x4.CreateFromQuaternion(bone.BindRotation)
                * Matrix4x4.CreateTranslation(bone.BindTranslation);
            binds[bone.Name.Replace(' ', '_')] = local;
        }

        return binds;
    }

    /// <summary>
    /// Clip files and model files in the AoE3 corpus disagree about their unit scale: the
    /// musketeer model declares 1.5625 units per meter while shared library clips such as
    /// march_rifleman declare 0.6152 (the same unit expressed 2.54x smaller). Converting each
    /// file through its own declared scale would stretch clip translations 2.54x relative to
    /// the model's binds. The correction factor is therefore derived from the files
    /// themselves: the median ratio of matched bones' bind translation length to the clip's
    /// first-sample translation length, both in engine space.
    /// </summary>
    private float ComputeUnitScale(
        GrannyObject animation, string sourceKey, GrannyAxisSystem axes, Dictionary<string, Matrix4x4> bindLocals)
    {
        var ratios = new List<float>();
        foreach (var trackGroup in animation.GetObjects("TrackGroups"))
        {
            foreach (var track in trackGroup.GetObjects("TransformTracks"))
            {
                var trackName = track.GetString("Name");
                var position = GrannyCurve.FromWrapper(
                    track.GetObject("PositionCurve"), sourceKey, trackName ?? "?", "PositionCurve");
                if (trackName is null || position is null)
                {
                    continue;
                }

                if (!bindLocals.TryGetValue(trackName.Replace(' ', '_'), out var bindLocal))
                {
                    continue;
                }

                var bindLength = bindLocal.Translation.Length();
                var clipLength = axes.ToEngineMatrix(
                    Matrix4x4.CreateTranslation(position.SamplePosition(0f))).Translation.Length();
                if (bindLength < 1e-4f || clipLength < 1e-4f)
                {
                    continue;
                }

                ratios.Add(bindLength / clipLength);
            }
        }

        // The mismatch is file-global (units per meter declared per file), so a single
        // matched bind/clip pair is sufficient evidence; more pairs only harden the median
        // against an unusual first pose.
        if (ratios.Count == 0)
        {
            return 1f;
        }

        ratios.Sort();
        var median = ratios[ratios.Count / 2];
        if (median is < 0.05f or > 20f)
        {
            Diagnostics.Warn(
                $"Granny animation '{sourceKey}': derived unit scale {median:0.###} is outside a sane range; " +
                "keeping the declared scale.");
            return 1f;
        }

        if (MathF.Abs(median - 1f) > 0.01f)
        {
            Diagnostics.Info(
                $"Granny animation '{sourceKey}': clip unit scale corrected by {median:0.###} " +
                $"({ratios.Count} matched bones) to match the target skeleton.");
        }

        return median;
    }

    private void ImportTrackGroup(
        GrannyObject trackGroup,
        string sourceKey,
        GrannyAxisSystem axes,
        float durationSeconds,
        float timeStepSeconds,
        Dictionary<string, Matrix4x4> bindLocals,
        float unitScale,
        List<ImportedAnimationChannel> channels)
    {
        foreach (var track in trackGroup.GetObjects("TransformTracks"))
        {
            var trackName = track.GetString("Name");
            if (trackName is null)
            {
                Diagnostics.Warn($"Granny animation '{sourceKey}': skipping an unnamed transform track.");
                continue;
            }

            var position = GrannyCurve.FromWrapper(track.GetObject("PositionCurve"), sourceKey, trackName, "PositionCurve");
            var orientation = GrannyCurve.FromWrapper(
                track.GetObject("OrientationCurve"), sourceKey, trackName, "OrientationCurve");
            var scaleShear = GrannyCurve.FromWrapper(
                track.GetObject("ScaleShearCurve"), sourceKey, trackName, "ScaleShearCurve");
            if (position is null && orientation is null && scaleShear is null)
            {
                continue;
            }

            var times = SampleTimes(durationSeconds, timeStepSeconds, sourceKey, trackName);
            ImportTrack(trackName, position, orientation, scaleShear, axes, bindLocals, unitScale, times, channels);
        }
    }

    private static float[] SampleTimes(float durationSeconds, float timeStepSeconds, string sourceKey, string trackName)
    {
        var sampleCount = (int)MathF.Ceiling(durationSeconds / timeStepSeconds) + 1;
        if (sampleCount > MaxSamplesPerTrack)
        {
            throw new InvalidDataException(
                $"Granny animation '{sourceKey}': track '{trackName}' would sample {sampleCount} times " +
                $"(duration {durationSeconds}, time step {timeStepSeconds}); refusing the import.");
        }

        var times = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            times[i] = MathF.Min(i * timeStepSeconds, durationSeconds);
        }

        return times;
    }

    private void ImportTrack(
        string trackName,
        GrannyCurve? position,
        GrannyCurve? orientation,
        GrannyCurve? scaleShear,
        GrannyAxisSystem axes,
        Dictionary<string, Matrix4x4> bindLocals,
        float unitScale,
        float[] times,
        List<ImportedAnimationChannel> channels)
    {
        // Skeleton bone names use underscores where some clip track names keep the Maya
        // space separator ("Bip01 Head" vs "Bip01_Head").
        var normalizedTrackName = trackName.Replace(' ', '_');
        var target = ImportedAnimationTarget.CreateBoneName(normalizedTrackName);

        var translations = new List<ImportedKeyframe>(times.Length);
        var rotations = new List<ImportedKeyframe>(times.Length);
        var scales = new List<ImportedKeyframe>(times.Length);
        var hasNonUniformScale = false;

        // The runtime composes channels as offsets on the bind local; without a bind for
        // this track (bone absent from the target skeleton) channels would mis-compose,
        // so tracks without a bind carry the identity offset and are reported.
        var hasBind = bindLocals.TryGetValue(normalizedTrackName, out var bindLocal);
        if (!hasBind)
        {
            Diagnostics.Warn(
                $"Granny track '{trackName}' has no bone in the target skeleton; it binds to nothing.");
        }

        foreach (var time in times)
        {
            var fileLocal = BuildFileLocalTransform(position, orientation, scaleShear, time);
            var engineLocal = axes.ToEngineMatrix(fileLocal);

            // Clip files may declare a different unit scale than the model (see ComputeUnitScale);
            // translations are re-expressed in the target skeleton's scale before offset composition.
            engineLocal.Translation *= unitScale;

            var channelLocal = engineLocal;
            if (hasBind && Matrix4x4.Invert(bindLocal, out var inverseBind))
            {
                channelLocal = engineLocal * inverseBind;
            }

            Matrix4x4.Decompose(channelLocal, out var scale, out var rotation, out var translation);

            if (position is not null)
            {
                translations.Add(ImportedKeyframe.FromVector3(TimeSpan.FromSeconds(time), translation));
            }

            if (orientation is not null)
            {
                rotations.Add(ImportedKeyframe.FromQuaternion(
                    TimeSpan.FromSeconds(time), Quaternion.Normalize(rotation)));
            }

            if (scaleShear is not null)
            {
                hasNonUniformScale |= MathF.Abs(scale.X - scale.Y) > 1e-3f || MathF.Abs(scale.Y - scale.Z) > 1e-3f;
                var uniform = MathF.Cbrt(scale.X * scale.Y * scale.Z);
                scales.Add(ImportedKeyframe.FromFloat(TimeSpan.FromSeconds(time), uniform));
            }
        }

        if (hasNonUniformScale)
        {
            Diagnostics.Warn(
                $"Granny track '{trackName}' animates non-uniform scale-shear; it is reduced to a uniform scale.");
        }

        AddChannel(target, ImportedAnimationChannelType.TranslationX, translations, v => v.X, channels);
        AddChannel(target, ImportedAnimationChannelType.TranslationY, translations, v => v.Y, channels);
        AddChannel(target, ImportedAnimationChannelType.TranslationZ, translations, v => v.Z, channels);

        if (rotations.Count > 0)
        {
            channels.Add(new ImportedAnimationChannel(
                target, ImportedAnimationChannelType.Quaternion, rotations, ImportedAnimationInterpolation.Linear));
        }

        // Scale channels only matter when the clip actually moves away from the bind-pose
        // identity scale; constant-identity scale tracks add nothing.
        if (scales.Count > 0 && scales.Any(k => MathF.Abs(k.FloatValue - 1f) > 1e-4f))
        {
            channels.Add(new ImportedAnimationChannel(
                target, ImportedAnimationChannelType.Scale, scales, ImportedAnimationInterpolation.Linear));
        }
    }

    private static void AddChannel(
        ImportedAnimationTarget target,
        ImportedAnimationChannelType type,
        List<ImportedKeyframe> vectorKeyframes,
        Func<Vector3, float> component,
        List<ImportedAnimationChannel> channels)
    {
        if (vectorKeyframes.Count == 0)
        {
            return;
        }

        var keyframes = new List<ImportedKeyframe>(vectorKeyframes.Count);
        foreach (var keyframe in vectorKeyframes)
        {
            keyframes.Add(ImportedKeyframe.FromFloat(keyframe.Time, component(keyframe.Vector3Value)));
        }

        channels.Add(new ImportedAnimationChannel(
            target, type, keyframes, ImportedAnimationInterpolation.Linear));
    }

    private static Matrix4x4 BuildFileLocalTransform(
        GrannyCurve? position,
        GrannyCurve? orientation,
        GrannyCurve? scaleShear,
        float time)
    {
        var scaleMatrix = Matrix4x4.Identity;
        if (scaleShear is not null)
        {
            var values = scaleShear.SampleScaleShear(time);
            scaleMatrix = new Matrix4x4(
                values[0], values[1], values[2], 0f,
                values[3], values[4], values[5], 0f,
                values[6], values[7], values[8], 0f,
                0f, 0f, 0f, 1f);
        }

        var rotation = orientation is not null ? orientation.SampleQuaternion(time) : Quaternion.Identity;
        var translation = position is not null ? position.SamplePosition(time) : Vector3.Zero;
        return scaleMatrix * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
    }
}
