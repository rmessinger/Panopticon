using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenSage.Physics;

public sealed record PhysicsBodyDescription
{
    public required PhysicsShape Shape { get; init; }

    public float Mass { get; init; } = 1.0f;

    public Vector3 Position { get; init; }

    public Quaternion Rotation { get; init; } = Quaternion.Identity;

    public bool IsStatic { get; init; }

    public PhysicsCollisionFilter CollisionFilter { get; init; } = PhysicsCollisionFilter.Everything;
}

public abstract record PhysicsShape
{
    private PhysicsShape()
    {
    }

    public sealed record Sphere(float Radius) : PhysicsShape;

    public sealed record Box(Vector3 HalfExtents) : PhysicsShape;

    /// <summary>Height is the full height along the cylinder axis.</summary>
    public sealed record Cylinder(float Radius, float Height) : PhysicsShape;

    public sealed record ConvexHull(IReadOnlyList<Vector3> Points) : PhysicsShape;

    public sealed record TriangleList(IReadOnlyList<TerrainPhysicsVertex> Vertices, IReadOnlyList<int> Indices) : PhysicsShape;
}

public readonly record struct PhysicsCollisionFilter(int Group, int CollidesWith)
{
    public static PhysicsCollisionFilter Everything { get; } = new(-1, -1);
}
