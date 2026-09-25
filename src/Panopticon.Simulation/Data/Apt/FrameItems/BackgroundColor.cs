using System.IO;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;
using Panopticon.Mathematics;

namespace Panopticon.Data.Apt.FrameItems;

public sealed class BackgroundColor : FrameItem
{
    public ColorRgba Color { get; private set; }

    public static BackgroundColor Parse(BinaryReader reader)
    {
        var backgroundColor = new BackgroundColor();
        backgroundColor.Color = reader.ReadColorRgba();
        return backgroundColor;
    }
}
