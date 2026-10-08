using System;

namespace Veldrid.ImageSharp;

/// <summary>
/// Exact piecewise sRGB transfer functions, matching the GLSL versions in
/// PbrMetallicRoughness.frag. Colour filtering under the linear colour space
/// (WI-G6-02) goes through these on the CPU; alpha never does.
/// </summary>
public static class SrgbTransfer
{
    public static float ToLinear(float srgb)
    {
        return srgb <= 0.04045f
            ? srgb / 12.92f
            : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
    }

    public static float ToSrgb(float linear)
    {
        return linear <= 0.0031308f
            ? linear * 12.92f
            : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
    }

    public static byte ToSrgbByte(float linear)
    {
        var clamped = Math.Clamp(linear, 0f, 1f);
        return (byte)MathF.Round(Math.Clamp(ToSrgb(clamped), 0f, 1f) * 255f);
    }

    /// <summary>Decodes an RGB triple; used at authored-colour upload boundaries.</summary>
    public static System.Numerics.Vector3 ToLinear(System.Numerics.Vector3 srgb)
    {
        return new System.Numerics.Vector3(ToLinear(srgb.X), ToLinear(srgb.Y), ToLinear(srgb.Z));
    }

    /// <summary>Decodes RGB and keeps alpha untouched; alpha is never a colour.</summary>
    public static System.Numerics.Vector4 ToLinearRgb(System.Numerics.Vector4 srgb)
    {
        return new System.Numerics.Vector4(ToLinear(srgb.X), ToLinear(srgb.Y), ToLinear(srgb.Z), srgb.W);
    }
}
