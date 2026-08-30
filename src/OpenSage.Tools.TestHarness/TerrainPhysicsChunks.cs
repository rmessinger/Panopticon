using System;
using System.Collections.Generic;
using OpenSage.Physics;
using OpenSage.Terrain;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Feeds a HeightMap into an <see cref="IPhysicsWorld"/> as one static chunk per
/// 16x16-cell region (matching the 17-vertex TerrainPatch grid), in world coordinates.
/// </summary>
internal static class TerrainPhysicsChunks
{
    public const int CellsPerChunk = 16;

    public static void Feed(HeightMap heightMap, IPhysicsWorld world)
    {
        var chunkCountX = (heightMap.Width - 1 + CellsPerChunk - 1) / CellsPerChunk;
        var chunkCountY = (heightMap.Height - 1 + CellsPerChunk - 1) / CellsPerChunk;

        for (var chunkY = 0; chunkY < chunkCountY; chunkY++)
        {
            for (var chunkX = 0; chunkX < chunkCountX; chunkX++)
            {
                FeedChunk(heightMap, world, chunkX, chunkY);
            }
        }
    }

    private static void FeedChunk(HeightMap heightMap, IPhysicsWorld world, int chunkX, int chunkY)
    {
        var verticesPerSide = CellsPerChunk + 1;
        var vertices = new TerrainPhysicsVertex[verticesPerSide * verticesPerSide];
        for (var ly = 0; ly < verticesPerSide; ly++)
        {
            for (var lx = 0; lx < verticesPerSide; lx++)
            {
                // Partial edge chunks clamp to the heightmap border; the duplicated
                // edge vertices keep the chunk grid rectangular.
                var gx = Math.Min(chunkX * CellsPerChunk + lx, heightMap.Width - 1);
                var gy = Math.Min(chunkY * CellsPerChunk + ly, heightMap.Height - 1);
                var position = heightMap.GetPosition(gx, gy);
                vertices[ly * verticesPerSide + lx] = new TerrainPhysicsVertex(position);
            }
        }

        var indices = new List<int>(CellsPerChunk * CellsPerChunk * 6);
        for (var y = 0; y < CellsPerChunk; y++)
        {
            for (var x = 0; x < CellsPerChunk; x++)
            {
                var cellX = chunkX * CellsPerChunk + x;
                var cellY = chunkY * CellsPerChunk + y;
                if (cellX >= heightMap.Width - 1 || cellY >= heightMap.Height - 1)
                {
                    continue;
                }

                var yThis = y * verticesPerSide;
                var yNext = (y + 1) * verticesPerSide;
                indices.Add(yThis + x);
                indices.Add(yThis + x + 1);
                indices.Add(yNext + x);
                indices.Add(yNext + x);
                indices.Add(yThis + x + 1);
                indices.Add(yNext + x + 1);
            }
        }

        world.SetTerrainChunk(chunkX, chunkY, vertices, indices.ToArray());
    }
}
