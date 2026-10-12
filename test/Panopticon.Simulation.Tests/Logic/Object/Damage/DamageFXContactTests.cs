using System;
using System.Numerics;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Logic.Object.Damage;

public class DamageFXContactTests
{
    private static readonly AxisAlignedBoundingBox Hull = new(new Vector3(-20, -5, 0), new Vector3(20, 5, 10));

    private static Vector3 Contact(Matrix4x4 localToWorld, Vector3 source)
    {
        Assert.True(DamageFXContact.TryGetAttackerFacingPoint(Hull, localToWorld, source, out var contact));
        return contact;
    }

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"expected {expected}, actual {actual}");
    }

    [Fact]
    public void AxisAlignedSourceHitsFacingFace()
    {
        AssertNear(new Vector3(20, 0, 5), Contact(Matrix4x4.Identity, new Vector3(70, 0, 5)));
        AssertNear(new Vector3(0, -5, 5), Contact(Matrix4x4.Identity, new Vector3(0, -55, 5)));
    }

    [Fact]
    public void YawedTargetUsesItsLocalFaces()
    {
        var world = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2))
            * Matrix4x4.CreateTranslation(60, 20, 0);

        // Local +Y faces world -X after a quarter turn, so a source at world -X hits the 5-unit side.
        AssertNear(new Vector3(55, 20, 4.5f), Contact(world, new Vector3(10, 20, 0)));
    }

    [Fact]
    public void NonUniformScaleWidensBounds()
    {
        var world = Matrix4x4.CreateScale(2, 3, 1);

        AssertNear(new Vector3(40, 0, 5), Contact(world, new Vector3(140, 0, 5)));
        AssertNear(new Vector3(0, 15, 5), Contact(world, new Vector3(0, 165, 5)));
    }

    [Fact]
    public void LowSourceHitsLowerOnTheFace()
    {
        // Ground-level source: the ray from the center (z 5) drops 5 over 50, exiting at z 4.5.
        AssertNear(new Vector3(0, 5, 4.5f), Contact(Matrix4x4.Identity, new Vector3(0, 50, 0)));
    }

    [Fact]
    public void SourceInsideBoundsReturnsCenter()
    {
        var world = Matrix4x4.CreateTranslation(100, 0, 0);

        AssertNear(new Vector3(100, 0, 5), Contact(world, new Vector3(110, 1, 2)));
        AssertNear(new Vector3(100, 0, 5), Contact(world, new Vector3(100, 0, 5)));
    }

    [Fact]
    public void SingularTransformFails()
    {
        Assert.False(DamageFXContact.TryGetAttackerFacingPoint(Hull, Matrix4x4.CreateScale(1, 0, 1), Vector3.Zero, out _));
    }

    private static readonly AxisAlignedBoundingBox Barrel = new(new Vector3(18, -1, 4), new Vector3(70, 1, 6));

    private static Vector3 SurfaceContact(Matrix4x4 localToWorld, Vector3 source)
    {
        Assert.True(DamageFXContact.TryGetAttackerFacingSurfacePoint([Hull, Barrel], localToWorld, source, out var contact));
        return contact;
    }

    [Fact]
    public void SurfaceContactPinsToHullFaceNotBarrelTip()
    {
        var contact = SurfaceContact(Matrix4x4.Identity, new Vector3(70, 0, 5));

        // The merged box would place the face at the barrel tip (x 70); the hull face is at x 20.
        AssertNear(new Vector3(20, 0, 5), contact);
    }

    [Fact]
    public void SurfaceContactFollowsYawAndScale()
    {
        var world = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI))
            * Matrix4x4.CreateScale(2, 1, 1)
            * Matrix4x4.CreateTranslation(0, 0, 0);

        // Half turn: local +X (hull front and barrel) faces world -X, doubled by scale.
        var contact = SurfaceContact(world, new Vector3(-70, 0, 5));

        AssertNear(new Vector3(-40, 0, 5), contact);
    }

    [Fact]
    public void SurfaceContactClampsObliqueRaysToTheFaceRectangle()
    {
        // Oblique from the front-right: dominant axis is Y, and the hit on that face plane falls
        // past the hull's X extent, so it clamps onto the face edge.
        AssertNear(new Vector3(20, 5, 5), SurfaceContact(Matrix4x4.Identity, new Vector3(70, 60, 5)));
    }

    [Fact]
    public void SurfaceContactPinsToSourcePointWhenInsideAPart()
    {
        var world = Matrix4x4.CreateTranslation(100, 0, 0);

        AssertNear(new Vector3(110, 0, 5), SurfaceContact(world, new Vector3(110, 0, 5)));
    }
}
