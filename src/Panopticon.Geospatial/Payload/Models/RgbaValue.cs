namespace Panopticon.Geospatial.Payload.Models;

/// <summary>
/// CPU-only RGBA sample value, one byte per channel. Returned by payload imagery
/// sampling; carries no renderer, ImageSharp, or texture type.
/// </summary>
public readonly record struct RgbaValue(byte R, byte G, byte B, byte A);
