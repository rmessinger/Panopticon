using System.IO;
using System.Numerics;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;

namespace Panopticon.Data.Map;

public sealed class GlobalLight
{
    public Vector3 Ambient { get; private set; }
    public Vector3 Color { get; private set; }
    public Vector3 Direction { get; private set; }

    internal static GlobalLight Create(Vector3 ambient, Vector3 color, Vector3 direction) => new()
    {
        Ambient = ambient,
        Color = color,
        Direction = direction
    };

    internal static GlobalLight Parse(BinaryReader reader)
    {
        return new GlobalLight
        {
            Ambient = reader.ReadVector3(),
            Color = reader.ReadVector3(),
            Direction = reader.ReadVector3()
        };
    }

    internal void WriteTo(BinaryWriter writer)
    {
        writer.Write(Ambient);
        writer.Write(Color);
        writer.Write(Direction);
    }
}
