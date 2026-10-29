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
        foreach (var trackGroup in animation.GetObjects("TrackGroups"))
        {
            ImportTrackGroup(trackGroup, name, axes, durationSeconds, timeStepSeconds, channels);
        }

        return new ImportedAnimation(name, TimeSpan.FromSeconds(durationSeconds), channels);
    }

    private void ImportTrackGroup(
        GrannyObject trackGroup,
        string sourceKey,
        GrannyAxisSystem axes,
        float durationSeconds,
        float timeStepSeconds,
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
            ImportTrack(trackName, position, orientation, scaleShear, axes, times, channels);
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
        float[] times,
        List<ImportedAnimationChannel> channels)
    {
        // Skeleton bone names use underscores where some clip track names keep the Maya
        // space separator ("Bip01 Head" vs "Bip01_Head").
        var target = ImportedAnimationTarget.CreateBoneName(trackName.Replace(' ', '_'));

        var translations = new List<ImportedKeyframe>(times.Length);
        var rotations = new List<ImportedKeyframe>(times.Length);
        var scales = new List<ImportedKeyframe>(times.Length);
        var hasNonUniformScale = false;

        foreach (var time in times)
        {
            var fileLocal = BuildFileLocalTransform(position, orientation, scaleShear, time);
            var engineLocal = axes.ToEngineMatrix(fileLocal);
            Matrix4x4.Decompose(engineLocal, out var scale, out var rotation, out var translation);

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
