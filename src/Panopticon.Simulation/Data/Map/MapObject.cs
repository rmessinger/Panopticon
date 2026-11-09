using System.Diagnostics;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

[DebuggerDisplay("{TypeName} ({Position})")]
public sealed class MapObject : Asset
{
    public const string AssetName = "Object";

    private Vector3? _sourcePosition;
    private Vector3 _convertedPosition;

    public Vector3 Position { get; set; }

    internal Vector3 LegacyPosition => _sourcePosition is { } source && Position == _convertedPosition
        ? source
        : Position / (float)UnitConversionPolicy.GeneralsFeetToMeters;

    /// <summary>
    /// Angle of the object in radians.
    /// </summary>
    public float Angle { get; private set; }

    public RoadType RoadType { get; private set; }

    public string TypeName { get; private set; }

    public AssetPropertyCollection Properties { get; private set; }

    public MapObject(Vector3 position, float angle, RoadType roadType, string typeName)
    {
        Position = position;
        Angle = angle;
        RoadType = roadType;
        TypeName = typeName;

        // WI-A5-28: WriteTo always serializes Properties; a placed object without properties
        // must still save. Engine-valid property values are WI-A5-35 scope.
        Properties = new AssetPropertyCollection();
    }

    internal static MapObject Parse(BinaryReader reader, MapParseContext context)
    {
        return ParseAsset(reader, context, version =>
        {
            var rawPos = reader.ReadVector3();
            var convertedPos = rawPos * (float)UnitConversionPolicy.GeneralsFeetToMeters;
            return new MapObject(
                convertedPos,
                reader.ReadSingle(),
                reader.ReadUInt32AsEnumFlags<RoadType>(),
                reader.ReadUInt16PrefixedAsciiString())
            {
                _sourcePosition = rawPos,
                _convertedPosition = convertedPos,
                Properties = AssetPropertyCollection.Parse(reader, context)
            };
        });
    }

    internal void WriteTo(BinaryWriter writer, AssetNameCollection assetNames)
    {
        WriteAssetTo(writer, () =>
        {
            writer.Write(LegacyPosition);
            writer.Write(Angle);
            writer.Write((uint)RoadType);
            writer.WriteUInt16PrefixedAsciiString(TypeName);
            Properties.WriteTo(writer, assetNames);
        });
    }
}

public enum ObjectWeather : uint
{
    UseMapWeather,
    UseNormalModel,
    UseSnowModel
}

public enum ObjectTime : uint
{
    UseMapTime,
    UseDayModel,
    UseNightModel
}

public enum ObjectAggressiveness : int
{
    Sleep = -2,
    Passive,
    Normal,
    Alert,
    Aggressive,
}

public enum ObjectVeterancy : uint
{
    Normal,
    Veteran,
    Elite,
    Heroic
}

public enum ObjectAmbientSoundPriority : uint
{
    Lowest,
    Low,
    Normal,
    High,
    Critical
}
