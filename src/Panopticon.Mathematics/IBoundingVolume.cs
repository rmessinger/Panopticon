namespace Panopticon.Mathematics;

public interface IBoundingVolume
{
    bool Intersects(RectangleF bounds);
}
