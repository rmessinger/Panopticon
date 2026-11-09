using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

public sealed class SkyboxSettings : Asset
{
    public const string AssetName = "SkyboxSettings";

    private Vector3? _sourcePosition;
    public Vector3 Position { get; private set; }
    public float Scale { get; private set; }
    public float Rotation { get; private set; }
    public string TextureScheme { get; private set; }

    internal static SkyboxSettings Parse(BinaryReader reader, MapParseContext context)
    {
        return ParseAsset(reader, context, version =>
        {
            var rawPos = reader.ReadVector3();
            return new SkyboxSettings
            {
                _sourcePosition = rawPos,
                Position = rawPos * (float)UnitConversionPolicy.GeneralsFeetToMeters,
                Scale = reader.ReadSingle(),
                Rotation = reader.ReadSingle(),
                TextureScheme = reader.ReadUInt16PrefixedAsciiString()
            };
        });
    }

    internal void WriteTo(BinaryWriter writer)
    {
        WriteAssetTo(writer, () =>
        {
            writer.Write(_sourcePosition ?? (Position / (float)UnitConversionPolicy.GeneralsFeetToMeters));
            writer.Write(Scale);
            writer.Write(Rotation);
            writer.WriteUInt16PrefixedAsciiString(TextureScheme);
        });
    }
}
