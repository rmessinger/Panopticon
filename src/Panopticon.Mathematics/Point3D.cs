using System;

namespace Panopticon.Mathematics;

public readonly record struct Point3D(float X, float Y, float Z)
{
    public Point3D(int x, int y, int z)
        : this((float)x, (float)y, (float)z)
    {
    }

    public Point2D ToPoint2D()
    {
        return new Point2D((int)MathF.Round(X), (int)MathF.Round(Y));
    }
}
