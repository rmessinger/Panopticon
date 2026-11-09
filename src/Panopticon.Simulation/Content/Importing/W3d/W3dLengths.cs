#nullable enable

using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;

namespace Panopticon.Content.Importing.W3d;

/// <summary>
/// Converts W3D lengths from legacy game units to meters at import by the Generals
/// compatibility scale. W3D files declare no unit, so this scale is compatibility, not measurement (AQ-5).
/// </summary>
internal static class W3dLengths
{
    private const float MetersPerLegacyUnit = (float)UnitConversionPolicy.GeneralsFeetToMeters;

    public static float ToMeters(float legacyLength) => legacyLength * MetersPerLegacyUnit;

    public static Vector3 ToMeters(in Vector3 legacyPosition) => legacyPosition * MetersPerLegacyUnit;
}
