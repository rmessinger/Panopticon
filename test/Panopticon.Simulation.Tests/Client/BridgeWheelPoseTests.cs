#nullable enable

using System;
using System.Numerics;
using Panopticon.Client;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Client;

/// <summary>
/// Pure coverage for the unguarded render-only tyre pose solver: rotation onto the
/// support plane (ramps, steep and ledge supports, non-coplanar fits), the pivot and
/// lift bookkeeping of the adjustment matrix, blending and argument validation.
/// </summary>
public sealed class BridgeWheelPoseTests
{
    private const double Tolerance = 1e-5;

    private static readonly Vector3[] Contacts =
[
        new(1.7f, 7.15f, 0f),
        new(1.7f, -7.15f, 0f),
        new(-14.5f, 7.15f, 0f),
        new(-14.5f, -7.15f, 0f),
    ];

    private static Vector3[] SupportsWith(Func<Vector3, float> height)
    {
        var supports = new Vector3[Contacts.Length];

        for (var i = 0; i < Contacts.Length; i++)
        {
            supports[i] = new Vector3(Contacts[i].X, Contacts[i].Y, height(Contacts[i]));
        }

        return supports;
    }

    private static double RotationAngle(Matrix4x4 adjustment)
    {
        return Math.Acos(Math.Clamp((adjustment.M11 + adjustment.M22 + adjustment.M33 - 1f) / 2f, -1f, 1f));
    }

    [Fact]
    public void FlatSupport_IsIdentity()
    {
        var ok = BridgeWheelPose.TrySolve(Contacts, SupportsWith(_ => 0f), Vector3.Zero, 1f, out var adjustment);

        Assert.True(ok);
        Assert.Equal(Matrix4x4.Identity, adjustment);
    }

    [Fact]
    public void RampX_RotatesOntoSupportPlane()
    {
        var supports = SupportsWith(c => 0.1f * c.X);
        var ok = BridgeWheelPose.TrySolve(Contacts, supports, Vector3.Zero, 1f, out var adjustment);

        Assert.True(ok);
        Assert.Equal(Math.Atan(0.1), RotationAngle(adjustment), 1e-4);

        for (var i = 0; i < Contacts.Length; i++)
        {
            var posed = Vector3.Transform(Contacts[i], adjustment);

            // The rigid tilt carries the tyre plane onto the support plane, so the posed
            // contact sits at or above the plane at its own (shifted) XY and at or above
            // its support sample; the worst-gap tyres touch exactly.
            Assert.True(posed.Z >= (0.1f * posed.X) - 1e-4);
            Assert.True(posed.Z >= supports[i].Z - 1e-4);
        }

        Assert.Equal((double)(0.1f * Contacts[0].X), (double)Vector3.Transform(Contacts[0], adjustment).Z, 1e-5);
        Assert.Equal((double)(0.1f * Contacts[1].X), (double)Vector3.Transform(Contacts[1], adjustment).Z, 1e-5);
    }

    [Fact]
    public void RampY_RotatesOntoSupportPlane()
    {
        var supports = SupportsWith(c => 0.1f * c.Y);
        var ok = BridgeWheelPose.TrySolve(Contacts, supports, Vector3.Zero, 1f, out var adjustment);

        Assert.True(ok);
        Assert.Equal(Math.Atan(0.1), RotationAngle(adjustment), 1e-4);

        for (var i = 0; i < Contacts.Length; i++)
        {
            var posed = Vector3.Transform(Contacts[i], adjustment);

            Assert.True(posed.Z >= (0.1f * posed.Y) - 1e-4);
            Assert.True(posed.Z >= supports[i].Z - 1e-4);
        }

        Assert.Equal((double)(0.1f * Contacts[0].Y), (double)Vector3.Transform(Contacts[0], adjustment).Z, 1e-5);
        Assert.Equal((double)(0.1f * Contacts[2].Y), (double)Vector3.Transform(Contacts[2], adjustment).Z, 1e-5);
    }

    [Fact]
    public void SteepSupport_IsFollowed()
    {
        var origin = new Vector3((Contacts[0].X + Contacts[2].X) / 2f, 0f, 0f);
        var supports = SupportsWith(c => MathF.Tan(MathF.PI / 6f) * c.X);
        var ok = BridgeWheelPose.TrySolve(Contacts, supports, origin, 1f, out var adjustment);

        Assert.True(ok);
        Assert.Equal(Math.Atan(0.57735), RotationAngle(adjustment), 1e-4);

        // The worst-gap tyres (the +X pair, lifted by the negative lift) land exactly on
        // their support samples; no posed contact goes below its sample.
        Assert.Equal((double)(MathF.Tan(MathF.PI / 6f) * Contacts[0].X), (double)Vector3.Transform(Contacts[0], adjustment).Z, 1e-4);
        Assert.Equal((double)(MathF.Tan(MathF.PI / 6f) * Contacts[1].X), (double)Vector3.Transform(Contacts[1], adjustment).Z, 1e-4);

        for (var i = 0; i < Contacts.Length; i++)
        {
            Assert.True(Vector3.Transform(Contacts[i], adjustment).Z >= supports[i].Z - 1e-4);
        }
    }

    [Fact]
    public void LedgeSupport_IsFollowed()
    {
        var supports = SupportsWith(_ => 0f);
        supports[2] = new Vector3(supports[2].X, supports[2].Y, 20f);
        var ok = BridgeWheelPose.TrySolve(Contacts, supports, Vector3.Zero, 1f, out var adjustment);

        Assert.True(ok);

        var angle = RotationAngle(adjustment);
        Assert.True(double.IsFinite(angle) && angle > 0.5);

        var touching = 0;

        for (var i = 0; i < Contacts.Length; i++)
        {
            var gap = Vector3.Transform(Contacts[i], adjustment).Z - supports[i].Z;
            Assert.True(gap >= -1e-4);

            if (Math.Abs(gap) <= 1e-4)
            {
                touching++;
            }
        }

        Assert.True(touching >= 1);
    }

    [Fact]
    public void FlatSupportAtHalfUnit_TranslatesByHalfUnit()
    {
        var ok = BridgeWheelPose.TrySolve(Contacts, SupportsWith(_ => 0.5f), Vector3.Zero, 1f, out var adjustment);

        Assert.True(ok);
        Assert.Equal(0.5, (double)adjustment.Translation.Z, Tolerance);
        Assert.Equal(0.5, (double)Vector3.Transform(Contacts[3], adjustment).Z, Tolerance);
    }

    [Fact]
    public void OffsetOrigin_PivotStaysAtOrigin()
    {
        var origin = new Vector3(5f, -3f, 100f);
        var supports = SupportsWith(c => 0.1f * c.X);
        var ok = BridgeWheelPose.TrySolve(Contacts, supports, origin, 1f, out var adjustment);

        Assert.True(ok);

        // Recompute the lift from the documented geometry and verify the pivot identity:
        // Transform(origin, adjustment) = origin + (0, 0, lift).
        var supportNormal = Vector3.Normalize(new Vector3(-0.1f, 0f, 1f));
        var rotation = Matrix4x4.CreateFromQuaternion(QuaternionUtility.CreateRotation(Vector3.UnitZ, supportNormal));
        var lift = float.NegativeInfinity;

        for (var i = 0; i < Contacts.Length; i++)
        {
            var rotatedHeight = Vector3.Transform(Contacts[i] - origin, rotation).Z + origin.Z;
            lift = MathF.Max(lift, supports[i].Z - rotatedHeight);
        }

        var posedOrigin = Vector3.Transform(origin, adjustment);

        Assert.Equal((double)origin.X, (double)posedOrigin.X, 1e-4);
        Assert.Equal((double)origin.Y, (double)posedOrigin.Y, 1e-4);
        Assert.Equal((double)(origin.Z + lift), (double)posedOrigin.Z, 1e-4);
    }

    [Fact]
    public void HalfWeight_BlendsRotationAndLift()
    {
        var supports = SupportsWith(c => 0.1f * c.X);
        Assert.True(BridgeWheelPose.TrySolve(Contacts, supports, Vector3.Zero, 1f, out var full));
        Assert.True(BridgeWheelPose.TrySolve(Contacts, supports, Vector3.Zero, 0.5f, out var half));

        Assert.Equal(RotationAngle(full) / 2, RotationAngle(half), 1e-4);
        Assert.Equal((double)(full.Translation.Z / 2), (double)half.Translation.Z, Tolerance);
    }

    [Fact]
    public void ZeroWeight_ReturnsFalseWithIdentity()
    {
        var ok = BridgeWheelPose.TrySolve(Contacts, SupportsWith(_ => 0f), Vector3.Zero, 0f, out var adjustment);

        Assert.False(ok);
        Assert.Equal(Matrix4x4.Identity, adjustment);
    }

    [Fact]
    public void NonCoplanarSupports_FollowTheMeasuredPlane()
    {
        var heights = new float[] { 0f, 0.1f, 0.05f, 0.2f };
        var supports = new Vector3[Contacts.Length];

        for (var i = 0; i < Contacts.Length; i++)
        {
            supports[i] = new Vector3(Contacts[i].X, Contacts[i].Y, heights[i]);
        }

        var ok = BridgeWheelPose.TrySolve(Contacts, supports, Vector3.Zero, 1f, out var adjustment);

        Assert.True(ok);

        var tiltDegrees = RotationAngle(adjustment) * (180.0 / Math.PI);
        Assert.True(tiltDegrees > 0 && tiltDegrees < 10);

        for (var i = 0; i < Contacts.Length; i++)
        {
            Assert.True(Vector3.Transform(Contacts[i], adjustment).Z >= supports[i].Z - 1e-4);
        }
    }

    [Fact]
    public void CollinearContacts_ReturnsFalse()
    {
        Vector3[] collinear = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(2f, 0f, 0f), new(3f, 0f, 0f)];
        var ok = BridgeWheelPose.TrySolve(collinear, SupportsWith(_ => 0f), Vector3.Zero, 1f, out var adjustment);

        Assert.False(ok);
        Assert.Equal(Matrix4x4.Identity, adjustment);
    }

    [Fact]
    public void NegativeSupport_LiftsDown()
    {
        var ok = BridgeWheelPose.TrySolve(Contacts, SupportsWith(_ => -0.25f), Vector3.Zero, 1f, out var adjustment);

        Assert.True(ok);
        Assert.Equal(-0.25, (double)adjustment.Translation.Z, Tolerance);
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        var supports = SupportsWith(_ => 0f);

        Assert.Throws<ArgumentOutOfRangeException>(() => BridgeWheelPose.TrySolve(Contacts[..3], supports, Vector3.Zero, 1f, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => BridgeWheelPose.TrySolve(Contacts, supports[..3], Vector3.Zero, 1f, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => BridgeWheelPose.TrySolve(Contacts, supports, Vector3.Zero, -0.5f, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => BridgeWheelPose.TrySolve(Contacts, supports, Vector3.Zero, 1.5f, out _));
        Assert.Throws<ArgumentException>(() => BridgeWheelPose.TrySolve(Contacts, [.. supports, Vector3.Zero], Vector3.Zero, 1f, out _));
    }
}
