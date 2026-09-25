using System.Collections.Generic;
using System.IO;
using Panopticon.Data.Apt.FrameItems;
using Panopticon.FileFormats;

namespace Panopticon.Data.Apt.Characters;

public sealed class Sprite : Playable
{
    public static Sprite Parse(BinaryReader reader)
    {
        return new Sprite
        {
            Frames = reader.ReadListAtOffset(() => Frame.Parse(reader))
        };
    }

    public static Sprite Create(AptFile container)
    {
        return new Sprite
        {
            Container = container,
            Frames = new List<Frame> { Frame.Create(new List<FrameItem>()) }
        };
    }
}
