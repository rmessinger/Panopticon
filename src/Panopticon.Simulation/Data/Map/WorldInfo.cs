using System.IO;
using Panopticon.Data.Ini;

namespace Panopticon.Data.Map;

public sealed class WorldInfo : Asset
{
    public const string AssetName = "WorldInfo";

    public AssetPropertyCollection Properties { get; private set; }

    /// <summary>WI-A5-28: a writable empty collection for new maps.</summary>
    internal static WorldInfo Create() => new()
    {
        Properties = new AssetPropertyCollection(),
    };

    internal static WorldInfo Parse(BinaryReader reader, MapParseContext context)
    {
        return ParseAsset(reader, context, version =>
        {
            return new WorldInfo
            {
                Properties = AssetPropertyCollection.Parse(reader, context)
            };
        });
    }

    internal void WriteTo(BinaryWriter writer, AssetNameCollection assetNames)
    {
        WriteAssetTo(writer, () =>
        {
            Properties.WriteTo(writer, assetNames);
        });
    }
}

public enum MapWeatherType : uint
{
    [IniEnum("NORMAL")]
    Normal,

    [IniEnum("SNOWY")]
    Snowy
}

public enum MapCompressionType : uint
{
    None,
    RefPack
}
