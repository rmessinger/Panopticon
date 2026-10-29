#nullable enable

using System;
using System.IO;
using System.Numerics;
using Panopticon.FileFormats.Granny;

namespace Panopticon.Content.Importing.Granny;

/// <summary>
/// Engine-space basis change from a Granny file's ArtToolInfo: units-per-meter scale, origin
/// subtraction, the right/up/back basis, and the Granny Y-up art space to Panopticon Z-up model
/// space rotation. Applied to positions, directions, and matrices so imported geometry is
/// upright and metric.
/// </summary>
internal sealed class GrannyAxisSystem
{
    private readonly Matrix4x4 _toEngine;

    private readonly Matrix4x4 _fromEngine;

    public GrannyAxisSystem(float unitsPerMeter, in Vector3 origin, in Vector3 right, in Vector3 up, in Vector3 back)
    {
        if (!float.IsFinite(unitsPerMeter) || unitsPerMeter <= 0.0f)
        {
            throw new InvalidDataException($"ArtToolInfo UnitsPerMeter must be positive and finite, found {unitsPerMeter}.");
        }

        var basis = new Matrix4x4(
            right.X, right.Y, right.Z, 0.0f,
            up.X, up.Y, up.Z, 0.0f,
            back.X, back.Y, back.Z, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);

        // Rows are the file axes expressed in engine space (row-vector convention: file +X
        // maps onto the Right vector); a mirrored basis (negative determinant, e.g. AoE3's
        // Right = (-1, 0, 0)) flips triangle winding.
        var determinant = basis.GetDeterminant();
        if (MathF.Abs(MathF.Abs(determinant) - 1.0f) > 1e-3f)
        {
            throw new InvalidDataException(
                $"ArtToolInfo right/up/back basis is not orthonormal (determinant {determinant}).");
        }

        Mirrors = determinant < 0.0f;

        var metersPerUnit = 1.0f / unitsPerMeter;

        // Granny art space is Y-up (Maya); Panopticon model space is Z-up with +X forward
        // (GltfTransformConverter documents the same conversion for glTF). The 3-cycle
        // (x, y, z) -> (z, x, y) maps art up onto engine up and has determinant +1, so it
        // does not change Mirrors.
        var yUpToZUp = new Matrix4x4(
            0.0f, 1.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 1.0f, 0.0f,
            1.0f, 0.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);
        _toEngine = Matrix4x4.CreateScale(metersPerUnit) * basis * yUpToZUp;
        _toEngine.Translation = Vector3.TransformNormal(-origin, _toEngine);
        _ = Matrix4x4.Invert(_toEngine, out _fromEngine);
    }

    /// <summary>True when the basis mirrors handedness, requiring triangle-winding reversal.</summary>
    public bool Mirrors { get; }

    public Vector3 ToEnginePosition(in Vector3 position)
    {
        return Vector3.Transform(position, _toEngine);
    }

    public Vector3 ToEngineDirection(in Vector3 direction)
    {
        return Vector3.TransformNormal(direction, _toEngine);
    }

    /// <summary>
    /// Re-expresses a file-space transform (e.g. a bind or inverse-bind matrix) in engine space
    /// by conjugation. System.Numerics is row-vector (v' = v * M), so the conjugate is
    /// M^-1 * m * M, not M * m * M^-1.
    /// </summary>
    public Matrix4x4 ToEngineMatrix(in Matrix4x4 matrix)
    {
        return _fromEngine * matrix * _toEngine;
    }

    // AoE3 DE files all report UnitsPerMeter 1.5625, but the art is authored at ~1.15
    // units per meter. Anchors (file units): the standing musketeer spans 2.073 units
    // (1.80 m with tricorn at 1.15, an implausible 1.33 m at 1.5625) and the hussar
    // horse spans 2.620 units hoofs-to-ears, 2.757 nose-to-tail, 0.731 body width
    // (2.28 / 2.40 / 0.64 m light-cavalry values at 1.15; no horse is 1.76 m
    // nose-to-tail). Evidence recorded in the C10 deep dive.
    private const float Aoe3MisreportedUnitsPerMeter = 1.5625f;

    private const float Aoe3UnitsPerMeter = 1.15f;

    public static GrannyAxisSystem FromArtToolInfo(GrannyObject artToolInfo)
    {
        ArgumentNullException.ThrowIfNull(artToolInfo);

        var fileUnitsPerMeter = artToolInfo.GetSingle("UnitsPerMeter") ?? 1.0f;
        return new GrannyAxisSystem(
            fileUnitsPerMeter == Aoe3MisreportedUnitsPerMeter ? Aoe3UnitsPerMeter : fileUnitsPerMeter,
            ReadVector3(artToolInfo, "Origin", Vector3.Zero),
            ReadVector3(artToolInfo, "RightVector", Vector3.UnitX),
            ReadVector3(artToolInfo, "UpVector", Vector3.UnitY),
            ReadVector3(artToolInfo, "BackVector", Vector3.UnitZ));
    }

    private static Vector3 ReadVector3(GrannyObject obj, string fieldName, in Vector3 fallback)
    {
        return obj.GetArray(fieldName) is float[] array && array.Length == 3
            ? new Vector3(array[0], array[1], array[2])
            : fallback;
    }
}
