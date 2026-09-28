#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
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
}
