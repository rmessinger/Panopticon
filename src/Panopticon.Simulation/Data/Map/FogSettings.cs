using System.IO;
using Panopticon.Data.Utilities.Extensions;

namespace Panopticon.Data.Map;

[AddedIn(SageGame.Cnc3)]
public sealed class FogSettings : Asset
{
    public const string AssetName = "FogSettings";

    public byte[] Unknown { get; private set; }

    internal static FogSettings Parse(BinaryReader reader, MapParseContext context)
    {
        return ParseAsset(reader, context, version =>
        {
            var result = new FogSettings();

            // TODO
            result.Unknown = reader.ReadBytes(24);

            return result;
        });
    }

    internal void WriteTo(BinaryWriter writer)
    {
        WriteAssetTo(writer, () =>
        {
            writer.Write(Unknown);
        });
    }
}
