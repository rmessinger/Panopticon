#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using FileFormatsNif = Panopticon.FileFormats.Nif;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// One morph controller bound to a target shape name: either an embedded
/// controller resolved through the shape's controller reference or a KF
/// sequence controlled-block target.
/// </summary>
internal sealed record NifMorphSource(
    string ShapeName,
    FileFormatsNif.NifMorphData MorphData);

/// <summary>
/// Converts NiMorphData into a C6 morph target set: morph 0 must equal the
/// base shape positions, morphs 1..N convert as relative position deltas
/// through the primitive's linear source-to-engine transform.
/// </summary>
internal static class NifMorphConverter
{
    /// <summary>
    /// Returns null when the morph data holds only the base shape (no
    /// morphable targets to import).
    /// </summary>
    internal static ImportedMorphTargetSet? Convert(
        string shapeName,
        string primitiveId,
        FileFormatsNif.NifMorphData morphData,
        FileFormatsNif.NifTriShapeData data,
        Matrix4x4 bake)
    {
        if (!morphData.RelativeTargets)
        {
            throw new NotSupportedException(
                $"NIF shape '{shapeName}' uses NiMorphData with RelativeTargets=0; " +
                "absolute morph targets are not supported.");
        }

        if (morphData.NumVertices != data.NumVertices)
        {
            throw new InvalidDataException(
                $"NIF shape '{shapeName}' carries {morphData.NumVertices} morph vectors " +
                $"for {data.NumVertices} vertices.");
        }

        if (morphData.NumMorphs == 0)
        {
            throw new InvalidDataException(
                $"NIF shape '{shapeName}' carries NiMorphData with no morphs.");
        }

        var targetCount = (int)morphData.NumMorphs - 1;
        if (targetCount == 0)
        {
            return null;
        }

        if (targetCount > ImportedMorphTargetSet.MaximumTargetCount)
        {
            throw new NotSupportedException(
                $"NIF shape '{shapeName}' carries {targetCount} morph targets after the base " +
                $"shape; the limit is {ImportedMorphTargetSet.MaximumTargetCount}.");
        }

        var baseVectors = morphData.Morphs[0].Vectors;
        for (var i = 0; i < data.NumVertices; i++)
        {
            if (baseVectors[i] != data.Vertices[i])
            {
                throw new InvalidDataException(
                    $"NIF shape '{shapeName}' morph 0 does not match the base shape " +
                    $"positions at vertex {i}.");
            }
        }

        // Deltas are directions in source space: convert through the bake's
        // linear part only, without its translation component.
        var linear = bake;
        linear.Translation = Vector3.Zero;

        var targetNames = new string[targetCount];
        var defaultWeights = new float[targetCount];
        var ranges = new ImportedMorphWeightRange[targetCount];
        var targets = new ImportedMorphTarget[targetCount];
        for (var m = 0; m < targetCount; m++)
        {
            var vectors = morphData.Morphs[m + 1].Vectors;
            var deltas = new Vector3[vectors.Count];
            for (var i = 0; i < vectors.Count; i++)
            {
                deltas[i] = Vector3.Transform(vectors[i], linear);
            }

            targetNames[m] = $"Target{m + 1}";
            defaultWeights[m] = 0.0f;
            ranges[m] = new ImportedMorphWeightRange(0.0f, 1.0f);
            targets[m] = new ImportedMorphTarget(deltas);
        }

        return new ImportedMorphTargetSet(primitiveId, targetNames, defaultWeights, ranges, targets);
    }

    /// <summary>
    /// Converts one KF morpher controller into a morph weight channel: morphs 1..N
    /// become Target1..TargetN weight vectors. Linear keys keep their source key
    /// times; quadratic keys resample on the C4c 30 Hz grid including all source
    /// key times.
    /// </summary>
    internal static void ConvertWeightChannel(
        FileFormatsNif.NifFile file,
        FileFormatsNif.NifGeomMorpherController controller,
        string targetName,
        NifPropertyTargetMap targets,
        string animationName,
        float duration,
        List<ImportedMorphAnimationChannel> channels)
    {
        NifPropertyAnimationConverter.ValidateTiming(controller, targetName, duration, animationName);
        NifPropertyAnimationConverter.ValidateFlags(
            controller.Flags, [0x2C], targetName, "morph");

        if (!targets.TryResolveShapeSubObject(targetName, out var primitiveId))
        {
            throw new InvalidDataException(
                $"NIF animation '{animationName}' morph target '{targetName}' does not match " +
                "any converted shape in the model.");
        }

        var data = (FileFormatsNif.NifMorphData)file.Blocks[controller.DataReference];
        var targetCount = (int)data.NumMorphs - 1;
        if (targetCount <= 0)
        {
            throw new InvalidDataException(
                $"NIF animation '{animationName}' morph target '{targetName}' carries no " +
                "morphable targets.");
        }

        if (targetCount > ImportedMorphTargetSet.MaximumTargetCount)
        {
            throw new NotSupportedException(
                $"NIF animation '{animationName}' morph target '{targetName}' carries " +
                $"{targetCount} morph targets after the base shape; the limit is " +
                $"{ImportedMorphTargetSet.MaximumTargetCount}.");
        }

        var groups = new FileFormatsNif.NifKeyGroup<FileFormatsNif.NifKey<float>>[targetCount];
        var targetNames = new string[targetCount];
        var anyQuadratic = false;
        for (var m = 0; m < targetCount; m++)
        {
            var name = $"Target{m + 1}";
            var group = data.Morphs[m + 1].WeightKeys;
            if (group is null || group.Keys.Count == 0)
            {
                throw new InvalidDataException(
                    $"NIF animation '{animationName}' morph target '{targetName}' target " +
                    $"'{name}' has no weight keys.");
            }

            foreach (var key in group.Keys)
            {
                if (!float.IsFinite(key.Value) || key.Value < 0f || key.Value > 1f)
                {
                    throw new InvalidDataException(
                        $"NIF animation '{animationName}' morph target '{targetName}' target " +
                        $"'{name}' has a weight key with value {key.Value.ToString(CultureInfo.InvariantCulture)}, outside [0, 1].");
                }

                if (!float.IsFinite(key.Time) || key.Time < -1e-4f || key.Time > duration + 1e-4f)
                {
                    throw new InvalidDataException(
                        $"NIF animation '{animationName}' morph target '{targetName}' target " +
                        $"'{name}' has a key at {key.Time.ToString(CultureInfo.InvariantCulture)} s, " +
                        $"outside the clip range 0..{duration.ToString(CultureInfo.InvariantCulture)}.");
                }
            }

            groups[m] = group;
            targetNames[m] = name;
            anyQuadratic |= group.Interpolation == FileFormatsNif.NifKeyType.Quadratic;
        }

        var times = new List<float>();
        foreach (var group in groups)
        {
            times.AddRange(group.Keys.Select(key => key.Time));
        }

        if (anyQuadratic)
        {
            for (var k = 0; k <= (int)MathF.Floor(duration * 30f); k++)
            {
                times.Add(k / 30f);
            }

            times.Add(duration);
        }

        times.Sort((a, b) => a.CompareTo(b));
        var sampleTimes = new List<float>();
        foreach (var time in times)
        {
            if (sampleTimes.Count == 0 || time - sampleTimes[^1] > 1e-6f)
            {
                sampleTimes.Add(time);
            }
        }

        var weightVectors = new float[sampleTimes.Count][];
        for (var i = 0; i < sampleTimes.Count; i++)
        {
            var vector = new float[targetCount];
            for (var m = 0; m < targetCount; m++)
            {
                vector[m] = NifKeyCurve.Evaluate(groups[m], sampleTimes[i]);
            }

            weightVectors[i] = vector;
        }

        channels.Add(new ImportedMorphAnimationChannel(
            primitiveId,
            targetNames,
            sampleTimes,
            weightVectors,
            ImportedAnimationInterpolation.Linear));
    }
}
