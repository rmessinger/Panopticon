#nullable enable

using System;
using System.Numerics;
using Panopticon.Mathematics;

namespace Panopticon.Client;

/// <summary>
/// Render-only tyre pose for a four-wheel vehicle on a generated-bridge corridor: the
/// rigid rotation carries the body's current tyre plane onto the support plane under
/// the tyres, the lift keeps every tyre at or above its support, the corridor weight
/// blends both back to the existing pose. The body's current tyre plane is the rotation
/// reference so the engine's own pitch and roll are not counted twice; logic position,
/// movement and collision are untouched. The fit is deliberately unguarded: measured on
/// the corridor's lateral embankment edges, dropping ledge tyres or capping the tilt
/// produced per-frame steps several times larger than the plain fit.
/// </summary>
internal static class BridgeWheelPose
{
    /// <summary>
    /// Solves the render-only adjustment for one vehicle: row-vector matrix with
    /// Vector3.Transform(p, adjustment) = (p - origin) * R + origin + (0, 0, lift).
    /// Returns false (identity) when the pose does not apply: zero weight or a
    /// degenerate plane fit.
    /// </summary>
    public static bool TrySolve(ReadOnlySpan<Vector3> contacts, ReadOnlySpan<Vector3> supports, Vector3 origin, float weight, out Matrix4x4 adjustment)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(contacts.Length, 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(supports.Length, 4);
        ArgumentOutOfRangeException.ThrowIfNegative(weight);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(weight, 1);

        if (contacts.Length != supports.Length)
        {
            throw new ArgumentException("Contacts and supports must describe the same tyres.", nameof(supports));
        }

        adjustment = Matrix4x4.Identity;

        if (weight <= 0f)
        {
            return false;
        }

        if (!WheelPlaneFit.TryGetNormal(contacts, out var bodyNormal))
        {
            return false;
        }

        if (!WheelPlaneFit.TryGetNormal(supports, out var supportNormal))
        {
            return false;
        }

        var rotation = QuaternionUtility.CreateRotation(bodyNormal, supportNormal);
        var rotationMatrix = Matrix4x4.CreateFromQuaternion(rotation);
        var lift = float.NegativeInfinity;

        for (var i = 0; i < supports.Length; i++)
        {
            var rotatedHeight = Vector3.Transform(contacts[i] - origin, rotationMatrix).Z + origin.Z;
            lift = MathF.Max(lift, supports[i].Z - rotatedHeight);
        }

        if (weight < 1f)
        {
            rotation = Quaternion.Slerp(Quaternion.Identity, rotation, weight);
            lift *= weight;
        }

        rotationMatrix = Matrix4x4.CreateFromQuaternion(rotation);
        var translation = origin - Vector3.Transform(origin, rotationMatrix) + new Vector3(0f, 0f, lift);
        adjustment = rotationMatrix * Matrix4x4.CreateTranslation(translation);

        return true;
    }
}
