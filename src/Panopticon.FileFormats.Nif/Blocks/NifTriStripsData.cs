using System.Collections.Generic;
using System.IO;

namespace Panopticon.FileFormats.Nif;

/// <summary>
/// NiTriStripsData at 10.1.0.0: triangle-based geometry data plus strip
/// lengths and a flat point list. Points index the vertex array; each
/// strip of length n contributes n-2 triangles with alternating winding.
/// </summary>
public sealed record NifTriStripsData : NifTriBasedGeometryData
{
    public IReadOnlyList<ushort> StripLengths { get; }
    public IReadOnlyList<ushort> Points { get; }

    private NifTriStripsData(NifBinaryReader reader)
        : base(reader)
    {
        var numStrips = reader.ReadUInt16("NumStrips");
        var stripLengths = new ushort[numStrips];
        var pointCount = 0;
        for (var i = 0; i < numStrips; i++)
        {
            stripLengths[i] = reader.ReadUInt16($"StripLengths[{i}]");
            pointCount += stripLengths[i];
        }

        StripLengths = stripLengths;

        var hasPoints = reader.ReadByte("HasPoints");
        if (hasPoints != 1)
        {
            throw new InvalidDataException(
                $"NIF NiTriStripsData declares HasPoints={hasPoints}; only 1 is valid.");
        }

        var points = new ushort[pointCount];
        for (var i = 0; i < pointCount; i++)
        {
            points[i] = reader.ReadUInt16($"Points[{i}]");
            if (points[i] >= NumVertices)
            {
                throw new InvalidDataException(
                    $"NIF NiTriStripsData point {i} is vertex {points[i]}, but the block has " +
                    $"only {NumVertices} vertices.");
            }
        }

        Points = points;

        var stripTriangleCount = pointCount - (2 * numStrips);
        if (stripTriangleCount != NumTriangles)
        {
            throw new InvalidDataException(
                $"NIF NiTriStripsData strips encode {stripTriangleCount} triangles, but the " +
                $"block declares {NumTriangles}.");
        }
    }

    internal static NifTriStripsData Read(NifBinaryReader reader)
    {
        return new NifTriStripsData(reader);
    }
}
