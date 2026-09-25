using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.CncGeneralsZeroHour)]
public sealed class InternetHackContainModuleData : TransportContainModuleData
{
    internal static new InternetHackContainModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static new readonly IniParseTable<InternetHackContainModuleData> FieldParseTable = TransportContainModuleData.FieldParseTable
        .Concat(new IniParseTable<InternetHackContainModuleData>());
}
