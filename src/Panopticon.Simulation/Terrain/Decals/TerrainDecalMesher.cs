using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Graphics.Shaders;

namespace Panopticon.Terrain.Decals;

internal static class TerrainDecalMesher
{
    internal static TerrainDecalMesh Build(HeightMap heightMap, IReadOnlyList<TerrainDecal> decals, Func<int, int, bool> flipped = null)
    {
        var vertices = new List<RoadShaderResources.RoadVertex>();
        var indices = new List<uint>();
        var origin = heightMap.GetPosition(0, 0);
        // Newest first matches the original alpha-overlay ordering.
        for (var decalIndex = decals.Count - 1; decalIndex >= 0; decalIndex--)
        {
            var decal = decals[decalIndex];
            var radius = decal.Radius;
            var minX = Math.Max(0, (int)MathF.Floor((decal.Position.X - radius - origin.X) / HeightMap.HorizontalScale));
            var minY = Math.Max(0, (int)MathF.Floor((decal.Position.Y - radius - origin.Y) / HeightMap.HorizontalScale));
            var maxX = Math.Min(heightMap.Width - 1, (int)MathF.Ceiling((decal.Position.X + radius - origin.X) / HeightMap.HorizontalScale));
            var maxY = Math.Min(heightMap.Height - 1, (int)MathF.Ceiling((decal.Position.Y + radius - origin.Y) / HeightMap.HorizontalScale));
            if (minX >= maxX || minY >= maxY)
            {
                continue;
            }

            var start = (uint)vertices.Count;
            var width = (uint)(maxX - minX + 1);
            var cell = new Vector2(decal.Cell % decal.Template.CellsPerRow, decal.Cell / decal.Template.CellsPerRow);
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var position = heightMap.GetPosition(x, y);
                    var uv = (cell * decal.Template.CellSpacing + new Vector2(0.5f)
                        + new Vector2(position.X - decal.Position.X, position.Y - decal.Position.Y) / (2 * radius))
                        / (decal.Template.CellsPerRow + 1);
                    position.Z += heightMap.VerticalScale / 10f;
                    vertices.Add(new RoadShaderResources.RoadVertex
                    {
                        Position = position,
                        Normal = heightMap.Normals[x, y],
                        UV = uv,
                    });
                }
            }

            for (var y = minY; y < maxY; y++)
            {
                for (var x = minX; x < maxX; x++)
                {
                    var index = start + (uint)(y - minY) * width + (uint)(x - minX);
                    // Panopticon's terrain currently uses the flipped split for every cell.
                    if (flipped?.Invoke(x, y) ?? true)
                    {
                        indices.AddRange([index + 1, index + width, index, index + 1, index + width + 1, index + width]);
                    }
                    else
                    {
                        indices.AddRange([index, index + width + 1, index + width, index, index + 1, index + width + 1]);
                    }
                }
            }
        }

        return new TerrainDecalMesh(vertices.ToArray(), indices.ToArray());
    }
}

internal sealed record TerrainDecalMesh(RoadShaderResources.RoadVertex[] Vertices, uint[] Indices);
