using Panopticon.Data.Ini;
using Panopticon.Mathematics;

namespace Panopticon.Logic.Object;

[AddedIn(SageGame.CncGeneralsZeroHour)]
public sealed class GrantStealthBehaviorModuleData : BehaviorModuleData
{
    internal static GrantStealthBehaviorModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<GrantStealthBehaviorModuleData> FieldParseTable = new IniParseTable<GrantStealthBehaviorModuleData>
    {
        { "StartRadius", (parser, x) => x.StartRadius = parser.ParseDistanceFloat() },
        { "FinalRadius", (parser, x) => x.FinalRadius = parser.ParseDistanceFloat() },
        { "RadiusGrowRate", (parser, x) => x.RadiusGrowRate = parser.ParseDistanceFloat() },
        { "RadiusParticleSystemName", (parser, x) => x.RadiusParticleSystemName = parser.ParseAssetReference() },
        { "KindOf", (parser, x) => x.KindOf = parser.ParseEnumBitArray<ObjectKinds>() },
    };

    public float StartRadius { get; private set; }
    public float FinalRadius { get; private set; }
    public float RadiusGrowRate { get; private set; }
    public string RadiusParticleSystemName { get; private set; }
    public BitArray<ObjectKinds> KindOf { get; private set; }
}
