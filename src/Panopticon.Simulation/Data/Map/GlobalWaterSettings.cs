using System.IO;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

[AddedIn(SageGame.Cnc3)]
public sealed class GlobalWaterSettings : Asset
{
    public const string AssetName = "GlobalWaterSettings";

    public bool ReflectionOn { get; private set; }
    public float ReflectionPlaneZ { get; private set; }

    internal static GlobalWaterSettings Parse(BinaryReader reader, MapParseContext context)
    {
        return ParseAsset(reader, context, version =>
        {
            return new GlobalWaterSettings
            {
                ReflectionOn = reader.ReadBooleanUInt32Checked(),
                ReflectionPlaneZ = reader.ReadSingle()
            };
        });
    }

    internal void WriteTo(BinaryWriter writer)
    {
        WriteAssetTo(writer, () =>
        {
            writer.WriteBooleanUInt32(ReflectionOn);
            writer.Write(ReflectionPlaneZ);
        });
    }
}
