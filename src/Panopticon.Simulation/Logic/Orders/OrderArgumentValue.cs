using System.Numerics;
using System.Runtime.InteropServices;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Logic.Orders;

[StructLayout(LayoutKind.Explicit)]
public struct OrderArgumentValue
{
    [FieldOffset(0)]
    public int Integer;

    [FieldOffset(0)]
    public float Float;

    [FieldOffset(0)]
    public bool Boolean;

    [FieldOffset(0)]
    public ObjectId ObjectId;

    [FieldOffset(0)]
    public Vector3 Position;

    [FieldOffset(0)]
    public Point2D ScreenPosition;

    [FieldOffset(0)]
    public Rectangle ScreenRectangle;
}
