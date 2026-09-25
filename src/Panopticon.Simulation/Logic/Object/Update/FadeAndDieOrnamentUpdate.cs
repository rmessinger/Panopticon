using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.Bfme)]
public sealed class FadeAndDieOrnamentUpdateModuleData : UpdateModuleData
{
    internal static FadeAndDieOrnamentUpdateModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<FadeAndDieOrnamentUpdateModuleData> FieldParseTable = new IniParseTable<FadeAndDieOrnamentUpdateModuleData>
    {
        { "Envelope", (parser, x) => x.Envelope = Envelope.Parse(parser) },
    };

    public Envelope Envelope { get; private set; }
}
