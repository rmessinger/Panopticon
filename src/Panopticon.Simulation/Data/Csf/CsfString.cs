using System.IO;
using Panopticon.Data.Utilities.Extensions;
using Panopticon.FileFormats;

namespace Panopticon.Data.Csf;

public sealed class CsfString
{
    public string Value { get; private set; }
    public string ExtraValue { get; private set; }

    internal static CsfString Parse(BinaryReader reader)
    {
        var fourCc = reader.ReadFourCc(bigEndian: true);
        if (fourCc != "STR " && fourCc != "STRW")
        {
            throw new InvalidDataException();
        }

        var value = reader.ReadUInt32PrefixedNegatedUnicodeString();

        string extraValue = null;
        if (fourCc == "STRW")
        {
            extraValue = reader.ReadUInt32PrefixedAsciiString();
        }

        return new CsfString
        {
            Value = value,
            ExtraValue = extraValue
        };
    }
}
