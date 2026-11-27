#nullable enable

using System;
using System.Numerics;

namespace Panopticon.Terrain;

/// <summary>
/// Planar frame of one generated bridge route. Route-local coordinates are X lateral
/// (positive to the right of the along axis), Y along the route (0 at Start, Length at
/// End) and Z up.
/// </summary>
public readonly record struct BridgeRoute(Vector2 Start, Vector2 End)
{
    public double Length => Vector2.Distance(Start, End);

    public bool IsValid => Length > 0;

    /// <summary>
    /// Unit XY from Start to End, or zero for a default or degenerate route so a
    /// Length 0 route carries zero axes instead of a NaN normalize.
    /// </summary>
    public Vector2 Along => Length > 0 ? (End - Start) / (float)Length : Vector2.Zero;

    public Vector2 Lateral => new(Along.Y, -Along.X);

    public (double S, double N) Project(float x, float y)
    {
        return Project(new Vector2(x, y));
    }

    public (double S, double N) Project(in Vector2 point)
    {
        var dx = (double)point.X - Start.X;
        var dy = (double)point.Y - Start.Y;
        var along = Along;
        var lateral = Lateral;

        return (dx * along.X + dy * along.Y, dx * lateral.X + dy * lateral.Y);
    }

    public Vector3 ToWorld(double s, double n, double z)
    {
        var along = Along;
        var lateral = Lateral;

        return new Vector3(
            (float)(Start.X + s * along.X + n * lateral.X),
            (float)(Start.Y + s * along.Y + n * lateral.Y),
            (float)z);
    }

    /// <summary>
    /// Row-vector matrix (the engine's Vector3.Transform(v, m) convention) mapping world
    /// points into route-local coordinates. The rotation angle atan2(Along.X, Along.Y)
    /// sends Along to +Y and Lateral to +X.
    /// </summary>
    public Matrix4x4 WorldToLocal =>
        Matrix4x4.CreateTranslation(-Start.X, -Start.Y, 0) *
        Matrix4x4.CreateRotationZ((float)Math.Atan2(Along.X, Along.Y));

    public Matrix4x4 LocalToWorld
    {
        get
        {
            Matrix4x4.Invert(WorldToLocal, out var inverse);
            return inverse;
        }
    }
}
