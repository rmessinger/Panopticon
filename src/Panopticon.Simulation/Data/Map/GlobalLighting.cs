using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Data.Ini;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;
using Panopticon.Mathematics;

namespace Panopticon.Data.Map;

public sealed class GlobalLighting : Asset
{
    public const string AssetName = "GlobalLighting";

    private static readonly TimeOfDay[] TimeOfDayValues = Enum.GetValues(typeof(TimeOfDay)).Cast<TimeOfDay>().ToArray();

    public TimeOfDay Time { get; private set; }

    public Dictionary<TimeOfDay, GlobalLightingConfiguration> LightingConfigurations { get; private set; }

    public MapColorArgb ShadowColor { get; private set; }

    public byte[] Unknown { get; private set; }

    [AddedIn(SageGame.Cnc4)]
    public Vector3? Unknown2 { get; private set; }

    [AddedIn(SageGame.Cnc4)]
    public MapColorArgb? Unknown3 { get; private set; }

    public ColorRgbF? NoCloudFactor { get; private set; }

    /// <summary>
    /// WI-A5-28: a writable default for new maps. Version 6 keeps the chunk minimal: no
    /// Unknown bytes (7 to 11) and no NoCloudFactor (8+), and each configuration carries
    /// only terrain and object lights (infantry lights arrive at version 7). Lights stay
    /// zero-valued; lit generated maps use <see cref="CreateForGeneratedMap"/> instead.
    /// Per-time-of-day theming is WI-A5-35 scope.
    /// </summary>
    internal static GlobalLighting Create() => new()
    {
        Version = 6,
        Time = TimeOfDay.Morning,
        LightingConfigurations = new Dictionary<TimeOfDay, GlobalLightingConfiguration>
        {
            [TimeOfDay.Morning] = GlobalLightingConfiguration.Create(),
            [TimeOfDay.Afternoon] = GlobalLightingConfiguration.Create(),
            [TimeOfDay.Evening] = GlobalLightingConfiguration.Create(),
            [TimeOfDay.Night] = GlobalLightingConfiguration.Create(),
        },
    };

    /// <summary>
    /// Neutral daylight for generated maps. Downward light directions match Lighting.h;
    /// ambient fill keeps back-facing slopes visible. Per-time-of-day theming is A5-35 scope.
    /// </summary>
    internal static GlobalLighting CreateForGeneratedMap()
    {
        var sun = GlobalLight.Create(
            new Vector3(0.38f, 0.38f, 0.38f),
            new Vector3(0.52f, 0.51f, 0.49f),
            Vector3.Normalize(new Vector3(0.50f, 0.35f, -0.79f)));
        var fill = GlobalLight.Create(
            new Vector3(0.04f, 0.04f, 0.04f),
            new Vector3(0.08f, 0.08f, 0.08f),
            Vector3.Normalize(new Vector3(-0.55f, -0.40f, -0.73f)));

        return new GlobalLighting
        {
            Version = 6,
            Time = TimeOfDay.Morning,
            LightingConfigurations = new Dictionary<TimeOfDay, GlobalLightingConfiguration>
            {
                [TimeOfDay.Morning] = GlobalLightingConfiguration.CreateLit(sun, fill),
                [TimeOfDay.Afternoon] = GlobalLightingConfiguration.CreateLit(sun, fill),
                [TimeOfDay.Evening] = GlobalLightingConfiguration.CreateLit(sun, fill),
                [TimeOfDay.Night] = GlobalLightingConfiguration.CreateLit(sun, fill),
            },
        };
    }

    internal static GlobalLighting Parse(BinaryReader reader, MapParseContext context)
    {
        return ParseAsset(reader, context, version =>
        {
            var time = reader.ReadUInt32AsEnum<TimeOfDay>();

            var lightingConfigurations = new Dictionary<TimeOfDay, GlobalLightingConfiguration>();

            for (var i = 0; i < TimeOfDayValues.Length; i++)
            {
                lightingConfigurations[TimeOfDayValues[i]] = GlobalLightingConfiguration.Parse(reader, version);
            }

            var shadowColor = MapColorArgb.Parse(reader);

            // TODO: BFME. Overbright? Bloom?
            byte[] unknown = null;
            if (version >= 7 && version < 11)
            {
                unknown = reader.ReadBytes(version >= 9 ? 4 : 44);
            }

            Vector3? unknown2 = null;
            MapColorArgb? unknown3 = null;
            if (version >= 12)
            {
                unknown2 = reader.ReadVector3();
                unknown3 = MapColorArgb.Parse(reader);
            }

            ColorRgbF? noCloudFactor = null;
            if (version >= 8)
            {
                noCloudFactor = reader.ReadColorRgbF();
            }

            return new GlobalLighting
            {
                Time = time,
                LightingConfigurations = lightingConfigurations,
                ShadowColor = shadowColor,
                Unknown = unknown,
                Unknown2 = unknown2,
                Unknown3 = unknown3,
                NoCloudFactor = noCloudFactor
            };
        });
    }

    internal void WriteTo(BinaryWriter writer)
    {
        WriteAssetTo(writer, () =>
        {
            writer.Write((uint)Time);

            for (var i = 0; i < TimeOfDayValues.Length; i++)
            {
                LightingConfigurations[TimeOfDayValues[i]].WriteTo(writer, Version);
            }

            ShadowColor.WriteTo(writer);

            if (Version >= 7 && Version < 11)
            {
                writer.Write(Unknown);
            }

            if (Version >= 12)
            {
                writer.Write(Unknown2.Value);
                Unknown3.Value.WriteTo(writer);
            }

            if (Version >= 8)
            {
                writer.Write(NoCloudFactor.Value);
            }
        });
    }
}

public enum TimeOfDay : uint
{
    [IniEnum("MORNING")]
    Morning = 1,

    [IniEnum("AFTERNOON")]
    Afternoon,

    [IniEnum("EVENING")]
    Evening,

    [IniEnum("NIGHT")]
    Night
}
