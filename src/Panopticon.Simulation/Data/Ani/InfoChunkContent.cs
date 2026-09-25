using System.IO;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;

namespace Panopticon.Data.Ani;

public sealed class InfoChunkContent : RiffChunkContent
{
    public string Value { get; private set; }

    internal static InfoChunkContent Parse(BinaryReader reader)
    {
        return new InfoChunkContent
        {
            Value = reader.ReadNullTerminatedString()
        };
    }
}
