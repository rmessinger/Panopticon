using System.Collections.Generic;
using System.IO;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

public sealed class BlendTileData : Asset
{
    public const string AssetName = "BlendTileData";

    /// <summary>Default texture: a Terrain.ini block name, not an arbitrary texture file name.</summary>
    public const string DefaultTextureName = "GrassType1";

    public uint NumTiles { get; private set; }

    public ushort[,] Tiles { get; private set; }
    public uint[,] Blends { get; private set; }
    public uint[,] ThreeWayBlends { get; private set; }
    public uint[,] CliffTextures { get; private set; }

    public bool[,] Impassability { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool[,] ImpassabilityToPlayers { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool[,] PassageWidths { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool[,] Taintability { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool[,] ExtraPassability { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public TileFlammability[,] Flammability { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public bool[,] Visibility { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool[,] ImpassabilityToAirUnits { get; private set; }

    [AddedIn(SageGame.Cnc3)]
    public bool[,] Buildability { get; private set; }

    [AddedIn(SageGame.Cnc3)]
    public bool[,] TiberiumGrowability { get; private set; }

    [AddedIn(SageGame.Ra3)]
    public byte[,] DynamicShrubberyDensity { get; private set; }

    public uint TextureCellCount { get; private set; }

    public BlendTileTexture[] Textures { get; private set; }

    public uint MagicValue1 { get; private set; }
    public uint MagicValue2 { get; private set; }

    public BlendDescription[] BlendDescriptions { get; private set; }

    /// <summary>
    /// When there aren't any cliff blends, some maps have 0 and some have 1.
    /// We need to keep the parsed value around so we can roundtrip correctly.
    /// </summary>
    public uint ParsedCliffTextureMappingsCount { get; private set; }

    public CliffTextureMapping[] CliffTextureMappings { get; private set; }

    private List<BlendTileTextureIndex> _textureIndices;

    public static BlendTileData Create(uint width, uint height) => CreateUniform(width, height, DefaultTextureName);

    /// <summary>
    /// Zero-filled tile data sized to a height map (WI-A5-27, WI-A5-28): tile, blend, and cliff
    /// arrays plus the version-gated passability arrays are allocated so editor edits and
    /// serialization always have a target. WI-A5-28 stamps Version 8: version 7 writes the
    /// impassability bit rows unpadded while the reader clips them to byte-aligned widths, so
    /// only versions above 7 round-trip exactly; 8 also keeps the Impassability array active
    /// (written when Version &gt; 6) without pulling in the heavier version-gated chunks.
    /// WI-A5-35 follow-through, found by the WI-A5-02 play leg: one terrain texture entry is
    /// seeded because Terrain.CreateTileDataTexture indexes TextureIndices by the per-tile
    /// texture id and an empty list makes every tile access out of range. textureName must be
    /// a Terrain.ini block name in the installed game.
    /// </summary>
    public static BlendTileData CreateUniform(uint width, uint height, string textureName = DefaultTextureName)
    {
        Ensure.NotNullOrEmpty(textureName, "Terrain texture name must be a Terrain.ini block name.");

        return new BlendTileData
        {
            Version = 8,
            NumTiles = width * height,
            Tiles = new ushort[width, height],
            Blends = new uint[width, height],
            ThreeWayBlends = new uint[width, height],
            CliffTextures = new uint[width, height],
            Impassability = new bool[width, height],
            ImpassabilityToPlayers = new bool[width, height],
            PassageWidths = new bool[width, height],
            Taintability = new bool[width, height],
            ExtraPassability = new bool[width, height],
            Flammability = new TileFlammability[width, height],
            Visibility = new bool[width, height],
            ImpassabilityToAirUnits = new bool[width, height],
            Buildability = new bool[width, height],
            TiberiumGrowability = new bool[width, height],
            DynamicShrubberyDensity = new byte[width, height],
            TextureCellCount = 16,
            Textures = [BlendTileTexture.Create(textureName, 0, 16, 4)],
            BlendDescriptions = [],
            CliffTextureMappings = [],
        };
    }

    /// <summary>
    /// Derived data.
    /// </summary>
    public IReadOnlyList<BlendTileTextureIndex> TextureIndices
    {
        get
        {
            if (_textureIndices == null)
            {
                _textureIndices = CalculateTextureIndices();
            }

            return _textureIndices;
        }
    }

    internal static BlendTileData Parse(BinaryReader reader, MapParseContext context, HeightMapData heightMapData)
    {
        return ParseAsset(reader, context, version =>
        {
            if (version < 6)
            {
                throw new InvalidDataException();
            }

            if (heightMapData == null)
            {
                throw new InvalidDataException("Expected HeightMapData asset before BlendTileData asset.");
            }

            var width = heightMapData.Width;
            var height = heightMapData.Height;

            var result = new BlendTileData();

            result.NumTiles = reader.ReadUInt32();
            if (result.NumTiles != width * height)
            {
                throw new InvalidDataException();
            }

            result.Tiles = reader.ReadUInt16Array2D(width, height);

            var blendBitSize = GetBlendBitSize(version);
            result.Blends = reader.ReadUIntArray2D(width, height, blendBitSize);
            result.ThreeWayBlends = reader.ReadUIntArray2D(width, height, blendBitSize);
            result.CliffTextures = reader.ReadUIntArray2D(width, height, blendBitSize);

            if (version > 6)
            {
                var passabilityWidth = heightMapData.Width;
                if (version == 7)
                {
                    // C&C Generals clips partial bytes from each row of passability data,
                    passabilityWidth = ((passabilityWidth + 1) / 8) * 8;
                }

                // If terrain is passable, there's a 0 in the data file.
                result.Impassability = reader.ReadSingleBitBooleanArray2D(passabilityWidth, heightMapData.Height);
            }

            if (version >= 10)
            {
                result.ImpassabilityToPlayers = reader.ReadSingleBitBooleanArray2D(heightMapData.Width, heightMapData.Height);
            }

            if (version >= 11)
            {
                result.PassageWidths = reader.ReadSingleBitBooleanArray2D(heightMapData.Width, heightMapData.Height);
            }

            if (version >= 14 && version < 25)
            {
                result.Taintability = reader.ReadSingleBitBooleanArray2D(heightMapData.Width, heightMapData.Height);
            }

            if (version >= 15)
            {
                result.ExtraPassability = reader.ReadSingleBitBooleanArray2D(heightMapData.Width, heightMapData.Height);
            }

            if (version >= 16 && version < 25)
            {
                result.Flammability = reader.ReadByteArray2DAsEnum<TileFlammability>(heightMapData.Width, heightMapData.Height);
            }

            if (version >= 17)
            {
                result.Visibility = reader.ReadSingleBitBooleanArray2D(heightMapData.Width, heightMapData.Height);
            }

            if (version >= 24)
            {
                // TODO: Are these in the right order?
                result.Buildability = reader.ReadSingleBitBooleanArray2D(heightMapData.Width, heightMapData.Height);
                result.ImpassabilityToAirUnits = reader.ReadSingleBitBooleanArray2D(heightMapData.Width, heightMapData.Height);
                result.TiberiumGrowability = reader.ReadSingleBitBooleanArray2D(heightMapData.Width, heightMapData.Height);
            }

            if (version >= 25)
            {
                result.DynamicShrubberyDensity = reader.ReadByteArray2D(heightMapData.Width, heightMapData.Height);
            }

            result.TextureCellCount = reader.ReadUInt32();

            var blendsCount = reader.ReadUInt32();
            if (blendsCount > 0)
            {
                // Usually minimum value is 1, but some files (perhaps Generals, not Zero Hour?) have 0.
                blendsCount--;
            }

            result.ParsedCliffTextureMappingsCount = reader.ReadUInt32();
            var cliffBlendsCount = result.ParsedCliffTextureMappingsCount;
            if (cliffBlendsCount > 0)
            {
                // Usually minimum value is 1, but some files (perhaps Generals, not Zero Hour?) have 0.
                cliffBlendsCount--;
            }

            var textureCount = reader.ReadUInt32();
            result.Textures = new BlendTileTexture[textureCount];
            for (var i = 0; i < textureCount; i++)
            {
                result.Textures[i] = BlendTileTexture.Parse(reader);
            }

            // Can be a variety of values, don't know what it means.
            result.MagicValue1 = reader.ReadUInt32();

            result.MagicValue2 = reader.ReadUInt32();
            if (result.MagicValue2 != 0)
            {
                throw new InvalidDataException();
            }

            result.BlendDescriptions = new BlendDescription[blendsCount];
            for (var i = 0; i < blendsCount; i++)
            {
                result.BlendDescriptions[i] = BlendDescription.Parse(reader, version);
            }

            result.CliffTextureMappings = new CliffTextureMapping[cliffBlendsCount];
            for (var i = 0; i < cliffBlendsCount; i++)
            {
                result.CliffTextureMappings[i] = CliffTextureMapping.Parse(reader);
            }

            return result;
        });
    }

    private static int GetTextureIndex(ushort tileValue, BlendTileTexture[] textures)
    {
        var actualCellIndex = 0u;
        for (var i = 0; i < textures.Length; i++)
        {
            var texture = textures[i];

            var actualCellCount = (texture.CellSize * 2) * (texture.CellSize * 2);
            if (tileValue < actualCellIndex + actualCellCount)
                return i;

            actualCellIndex += actualCellCount;
        }

        throw new InvalidDataException();
    }

    private List<BlendTileTextureIndex> CalculateTextureIndices()
    {
        // Calculate texture index + offset within texture.
        var textureIndices = new List<BlendTileTextureIndex>();
        var actualCellIndex = 0u;
        for (var i = 0; i < Textures.Length; i++)
        {
            var texture = Textures[i];

            var actualCellCount = (texture.CellSize * 2) * (texture.CellSize * 2);
            for (var j = 0; j < actualCellCount; j++)
            {
                textureIndices.Add(new BlendTileTextureIndex
                {
                    TextureIndex = i,
                    Offset = j
                });
            }

            actualCellIndex += actualCellCount;
        }

        return textureIndices;
    }

    private static uint GetBlendBitSize(ushort version)
    {
        return version >= 14 && version < 24
            ? 32u
            : 16u;
    }

    internal void WriteTo(BinaryWriter writer)
    {
        WriteAssetTo(writer, () =>
        {
            writer.Write(NumTiles);

            writer.WriteUInt16Array2D(Tiles);

            var blendBitSize = GetBlendBitSize(Version);
            writer.WriteUIntArray2D(Blends, blendBitSize);
            writer.WriteUIntArray2D(ThreeWayBlends, blendBitSize);
            writer.WriteUIntArray2D(CliffTextures, blendBitSize);

            if (Version > 6)
            {
                writer.WriteSingleBitBooleanArray2D(Impassability);
            }

            if (Version >= 10)
            {
                writer.WriteSingleBitBooleanArray2D(ImpassabilityToPlayers);
            }

            if (Version >= 11)
            {
                writer.WriteSingleBitBooleanArray2D(PassageWidths);
            }

            if (Version >= 14 && Version < 25)
            {
                writer.WriteSingleBitBooleanArray2D(Taintability);
            }

            if (Version >= 15)
            {
                writer.WriteSingleBitBooleanArray2D(ExtraPassability);
            }

            if (Version >= 16 && Version < 25)
            {
                writer.WriteByteArray2DAsEnum(Flammability);
            }

            if (Version >= 17)
            {
                writer.WriteSingleBitBooleanArray2D(Visibility, padValue: 0xFF);
            }

            if (Version >= 24)
            {
                // TODO: Are these in the right order?
                writer.WriteSingleBitBooleanArray2D(Buildability);
                writer.WriteSingleBitBooleanArray2D(ImpassabilityToAirUnits);
                writer.WriteSingleBitBooleanArray2D(TiberiumGrowability);
            }

            if (Version >= 25)
            {
                writer.WriteByteArray2D(DynamicShrubberyDensity);
            }

            writer.Write(TextureCellCount);

            writer.Write((uint)BlendDescriptions.Length + 1);

            writer.Write(ParsedCliffTextureMappingsCount);

            writer.Write((uint)Textures.Length);

            foreach (var texture in Textures)
            {
                texture.WriteTo(writer);
            }

            writer.Write(MagicValue1);
            writer.Write(MagicValue2);

            foreach (var blendDescription in BlendDescriptions)
            {
                blendDescription.WriteTo(writer);
            }

            foreach (var cliffTextureMapping in CliffTextureMappings)
            {
                cliffTextureMapping.WriteTo(writer);
            }
        });
    }
}

[AddedIn(SageGame.Bfme2)]
public enum TileFlammability
{
    FireResistant = 0,
    Grass = 1,
    HighlyFlammable = 2,
    Undefined = 3
}
