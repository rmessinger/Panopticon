using System.IO;

namespace Panopticon.Data.Map;

public sealed class GlobalLightingConfiguration
{
    public GlobalLight TerrainSun { get; private set; }
    public GlobalLight TerrainAccent1 { get; private set; }
    public GlobalLight TerrainAccent2 { get; private set; }

    public GlobalLight ObjectSun { get; private set; }
    public GlobalLight ObjectAccent1 { get; private set; }
    public GlobalLight ObjectAccent2 { get; private set; }

    public GlobalLight InfantrySun { get; private set; }
    public GlobalLight InfantryAccent1 { get; private set; }
    public GlobalLight InfantryAccent2 { get; private set; }

    /// <summary>
    /// WI-A5-28: an all-default configuration for new maps. Every light is populated so the
    /// chunk round-trips at any asset version (each version reads a different subset).
    /// </summary>
    internal static GlobalLightingConfiguration Create() => new()
    {
        TerrainSun = new GlobalLight(),
        TerrainAccent1 = new GlobalLight(),
        TerrainAccent2 = new GlobalLight(),
        ObjectSun = new GlobalLight(),
        ObjectAccent1 = new GlobalLight(),
        ObjectAccent2 = new GlobalLight(),
        InfantrySun = new GlobalLight(),
        InfantryAccent1 = new GlobalLight(),
        InfantryAccent2 = new GlobalLight(),
    };

    /// <summary>
    /// Shared terrain/object lighting. Version 6 omits infantry lights.
    /// </summary>
    internal static GlobalLightingConfiguration CreateLit(GlobalLight sun, GlobalLight fill) => new()
    {
        TerrainSun = sun,
        TerrainAccent1 = fill,
        TerrainAccent2 = new GlobalLight(),
        ObjectSun = sun,
        ObjectAccent1 = fill,
        ObjectAccent2 = new GlobalLight(),
        InfantrySun = new GlobalLight(),
        InfantryAccent1 = new GlobalLight(),
        InfantryAccent2 = new GlobalLight(),
    };

    internal static GlobalLightingConfiguration Parse(BinaryReader reader, uint version)
    {
        var result = new GlobalLightingConfiguration();

        result.TerrainSun = GlobalLight.Parse(reader);

        if (version < 10)
        {
            result.ObjectSun = GlobalLight.Parse(reader);

            if (version >= 7)
            {
                result.InfantrySun = GlobalLight.Parse(reader);
            }
        }

        result.TerrainAccent1 = GlobalLight.Parse(reader);

        if (version < 10)
        {
            result.ObjectAccent1 = GlobalLight.Parse(reader);

            if (version >= 7)
            {
                result.InfantryAccent1 = GlobalLight.Parse(reader);
            }
        }

        result.TerrainAccent2 = GlobalLight.Parse(reader);

        if (version < 10)
        {
            result.ObjectAccent2 = GlobalLight.Parse(reader);

            if (version >= 7)
            {
                result.InfantryAccent2 = GlobalLight.Parse(reader);
            }
        }

        return result;
    }

    internal void WriteTo(BinaryWriter writer, uint version)
    {
        TerrainSun.WriteTo(writer);

        if (version < 10)
        {
            ObjectSun.WriteTo(writer);

            if (version >= 7)
            {
                InfantrySun.WriteTo(writer);
            }
        }

        TerrainAccent1.WriteTo(writer);

        if (version < 10)
        {
            ObjectAccent1.WriteTo(writer);

            if (version >= 7)
            {
                InfantryAccent1.WriteTo(writer);
            }
        }

        TerrainAccent2.WriteTo(writer);

        if (version < 10)
        {
            ObjectAccent2.WriteTo(writer);

            if (version >= 7)
            {
                InfantryAccent2.WriteTo(writer);
            }
        }
    }
}
