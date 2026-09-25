using Panopticon.Audio;
using Panopticon.Content;
using Panopticon.Data.Ini;

namespace Panopticon.FX;

public sealed class SoundFXNugget : FXNugget
{
    internal static SoundFXNugget Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<SoundFXNugget> FieldParseTable = FXNuggetFieldParseTable.Concat(new IniParseTable<SoundFXNugget>
    {
        { "Name", (parser, x) => x.Value = parser.ParseAudioEventReference() }
    });

    public LazyAssetReference<BaseAudioEventInfo> Value { get; private set; }

    internal override void Execute(FXListExecutionContext context)
    {
        context.GameEngine.AudioSystem.PlayAudioEvent(Value?.Value);
    }
}
