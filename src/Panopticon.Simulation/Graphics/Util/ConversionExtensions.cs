using System.Numerics;
using Panopticon.Mathematics;

namespace Panopticon.Graphics.Util;

public static class ConversionExtensions
{
    public static Vector3 ToVector3(this in ColorRgb value)
    {
        return new Vector3(value.R / 255.0f, value.G / 255.0f, value.B / 255.0f);
    }
}
