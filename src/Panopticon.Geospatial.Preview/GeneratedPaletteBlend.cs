using System.Collections.Generic;
using Panopticon.Geospatial.Generation;

namespace Panopticon.Geospatial.Preview;

/// <summary>Palette-ordinal, mask, and border mapping shared by the preview and the package converter.</summary>
internal static class GeneratedPaletteBlend
{
    /// <summary>Tile values one standard CellSize 4 texture occupies in BlendTileData.</summary>
    internal const int TileValuesPerTexture = 64;

    internal sealed record PaletteTextureTable(string[] Textures, int[] OrdinalByPaletteIndex);

    /// <summary>Distinct canonical names in palette order form the texture table.</summary>
    internal static PaletteTextureTable FromCanonicalNames(string[] canonicalByIndex)
    {
        var textures = new List<string>(canonicalByIndex.Length);
        var ordinalByIndex = new int[canonicalByIndex.Length];

        for (var index = 0; index < canonicalByIndex.Length; index++)
        {
            var ordinal = textures.IndexOf(canonicalByIndex[index]);
            if (ordinal < 0)
            {
                ordinal = textures.Count;
                textures.Add(canonicalByIndex[index]);
            }

            ordinalByIndex[index] = ordinal;
        }

        return new PaletteTextureTable([.. textures], ordinalByIndex);
    }

    internal static void PaintTiles(
        ushort[,] tiles,
        bool[,] impassability,
        PaletteTextureTable textureTable,
        byte[,] materialIndices,
        TerrainMaskFlags[,] maskFlags,
        uint border,
        long coreWidth,
        long coreHeight,
        out long impassableCount,
        out long unbuildableCount)
    {
        impassableCount = 0L;
        unbuildableCount = 0L;

        for (var y = 0; y < coreHeight; y++)
        {
            for (var x = 0; x < coreWidth; x++)
            {
                var ordinal = textureTable.OrdinalByPaletteIndex[materialIndices[x, y]];
                tiles[x + border, y + border] = (ushort)(ordinal * TileValuesPerTexture);

                var flags = maskFlags[x, y];
                if ((flags & TerrainMaskFlags.Impassable) != 0)
                {
                    impassability[x + border, y + border] = true;
                    impassableCount++;
                }

                if ((flags & TerrainMaskFlags.Unbuildable) != 0)
                {
                    // The authoritative payload bit is retained unconverted: legacy
                    // Buildability polarity is unverified and never implies impassable.
                    unbuildableCount++;
                }
            }
        }

        // Border tile cells extend the nearest core edge like the border heights;
        // border impassability stays clear, the padding carries no payload data.
        ExtendBorderPadding(tiles, border, coreWidth, coreHeight);
    }

    internal static void ExtendBorderPadding<T>(T[,] stored, uint border, long coreWidth, long coreHeight)
    {
        for (var y = 0L; y < coreHeight + (2L * border); y++)
        {
            long coreY;
            if (y < border)
            {
                coreY = 0;
            }
            else if (y >= border + coreHeight)
            {
                coreY = coreHeight - 1;
            }
            else
            {
                coreY = y - border;
            }

            for (var x = 0L; x < coreWidth + (2L * border); x++)
            {
                if (x >= border && x < border + coreWidth && y >= border && y < border + coreHeight)
                {
                    continue;
                }

                long coreX;
                if (x < border)
                {
                    coreX = 0;
                }
                else if (x >= border + coreWidth)
                {
                    coreX = coreWidth - 1;
                }
                else
                {
                    coreX = x - border;
                }

                stored[x, y] = stored[coreX + border, coreY + border];
            }
        }
    }
}
