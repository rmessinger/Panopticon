using System.IO;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

[AddedIn(SageGame.Cnc3)]
public sealed class GlobalWaterSettings : Asset
{
    public const string AssetName = "GlobalWaterSettings";

    private float? _sourceReflectionPlaneZ;
    public bool ReflectionOn { get; private set; }
    public float ReflectionPlaneZ { get; private set; }

    internal static GlobalWaterSettings Parse(BinaryReader reader, MapParseContext context)
    {
        return ParseAsset(reader, context, version =>
        {
            var on = reader.ReadBooleanUInt32Checked();
            var rawZ = reader.ReadSingle();
            return new GlobalWaterSettings
            {
                ReflectionOn = on,
                _sourceReflectionPlaneZ = rawZ,
                ReflectionPlaneZ = (float)UnitConversionPolicy.ScaleLength(rawZ)
            };
        });
    }

    internal void WriteTo(BinaryWriter writer)
    {
        WriteAssetTo(writer, () =>
        {
            writer.WriteBooleanUInt32(ReflectionOn);
            writer.Write(_sourceReflectionPlaneZ ?? (float)UnitConversionPolicy.UnscaleLength(ReflectionPlaneZ));
        });
    }
}
