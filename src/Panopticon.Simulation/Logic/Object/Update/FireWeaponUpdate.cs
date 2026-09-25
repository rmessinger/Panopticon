using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Data.Ini;

namespace Panopticon.Logic.Object;

public sealed class FireWeaponUpdate : UpdateModule
{
    private readonly FireWeaponUpdateModuleData _moduleData;

    private byte _unknownByte;
    private string _weaponTemplate;

    private uint _unknownUInt1;
    private uint _unknownUInt2;

    private LogicFrame _unknownFrame1;
    private LogicFrame _unknownFrame2;
    private LogicFrame _unknownFrame3;
    private LogicFrame _unknownFrame4;
    private LogicFrame _unknownFrame5;

    private uint _unknownUInt3;
    private uint _unknownUInt4;
    private uint _unknownUInt5;

    public FireWeaponUpdate(GameObject gameObject, IGameEngine gameEngine, FireWeaponUpdateModuleData moduleData)
        : base(gameObject, gameEngine)
    {
        _moduleData = moduleData;
    }

    public override UpdateSleepTime Update()
    {
        var template = _moduleData?.Weapon?.Value;
        if (template == null)
        {
            return UpdateSleepTime.Forever;
        }

        FireAreaWeapon(GameObject.Translation, template);

        // Re-fire on the weapon's shot cooldown. A zero/unset cooldown fires every frame,
        // which is correct for continuous area weapons (radiation fields, scorch marks).
        var cooldown = template.CoolDownDelayBetweenShots.Min;
        var frames = cooldown.Value > 0 ? cooldown : LogicFrameSpan.One;
        return UpdateSleepTime.Frames(frames);
    }

    private void FireAreaWeapon(Vector3 origin, WeaponTemplate template)
    {
        foreach (var nugget in template.Nuggets.OfType<DamageNugget>())
        {
            ApplyRadiusDamage(nugget, origin);
        }
    }

    private void ApplyRadiusDamage(DamageNugget nugget, Vector3 origin)
    {
        var radius = nugget.Radius;
        if (radius <= 0f)
        {
            return;
        }

        var radiusSq = radius * radius;
        foreach (var candidate in GameEngine.GameLogic.Objects)
        {
            if (candidate == null || ReferenceEquals(candidate, GameObject))
            {
                continue;
            }

            if (candidate.IsEffectivelyDead || candidate.BodyModule == null)
            {
                continue;
            }

            if (Vector3.DistanceSquared(candidate.Translation, origin) > radiusSq)
            {
                continue;
            }

            candidate.AttemptDamage(new DamageInfoInput(GameObject)
            {
                DamageType = nugget.DamageType,
                DeathType = nugget.DeathType,
                Amount = nugget.Damage,
            });
        }
    }

    internal override void Load(StatePersister reader)
    {
        var version = reader.PersistVersion(2);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();

        reader.PersistByte(ref _unknownByte);
        reader.PersistAsciiString(ref _weaponTemplate);

        reader.SkipUnknownBytes(4);

        reader.PersistUInt32(ref _unknownUInt1);
        if (_unknownUInt1 != 2 && _unknownUInt1 != 3) // was 3 for NukeRadiationFieldWeapon
        {
            throw new InvalidStateException();
        }

        reader.PersistUInt32(ref _unknownUInt2); // something large, was 1 for NukeRadiationFieldWeapon

        reader.PersistLogicFrame(ref _unknownFrame1);
        reader.SkipUnknownBytes(4);
        reader.PersistLogicFrame(ref _unknownFrame2);
        reader.PersistLogicFrame(ref _unknownFrame3);
        reader.PersistLogicFrame(ref _unknownFrame4);

        reader.SkipUnknownBytes(8);

        reader.PersistUInt32(ref _unknownUInt3);

        reader.PersistUInt32(ref _unknownUInt4);
        if (_unknownUInt4 != 1)
        {
            throw new InvalidStateException();
        }

        reader.PersistUInt32(ref _unknownUInt5);
        if (_unknownUInt5 != 1)
        {
            throw new InvalidStateException();
        }

        reader.SkipUnknownBytes(4);

        if (version >= 2)
        {
            reader.PersistLogicFrame(ref _unknownFrame5);
        }
    }
}

public sealed class FireWeaponUpdateModuleData : UpdateModuleData
{
    internal static FireWeaponUpdateModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private static readonly IniParseTable<FireWeaponUpdateModuleData> FieldParseTable = new IniParseTable<FireWeaponUpdateModuleData>
    {
        { "Weapon", (parser, x) => x.Weapon = parser.ParseWeaponTemplateReference() },
        { "ExclusiveWeaponDelay", (parser, x) => x.ExclusiveWeaponDelay = parser.ParseTimeMillisecondsToLogicFrames() },
        { "InitialDelay", (parser, x) => x.InitialDelay = parser.ParseTimeMillisecondsToLogicFrames() },
        { "ChargingModeTrigger", (parser, x) => x.ChargingModeTrigger = parser.ParseBoolean() },
        { "AliveOnly", (parser, x) => x.AliveOnly = parser.ParseBoolean() },
        { "HeroModeTrigger", (parser, x) => x.HeroModeTrigger = parser.ParseBoolean() },
        { "FireWeaponNugget", (parser, x) => x.FireWeaponNugget = WeaponNugget.Parse(parser) }
    };

    public LazyAssetReference<WeaponTemplate> Weapon { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public LogicFrameSpan ExclusiveWeaponDelay { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public LogicFrameSpan InitialDelay { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool ChargingModeTrigger { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool AliveOnly { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool HeroModeTrigger { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public WeaponNugget FireWeaponNugget { get; private set; }

    internal override FireWeaponUpdate CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        return new FireWeaponUpdate(gameObject, gameEngine, this);
    }
}

[AddedIn(SageGame.Bfme2)]
public sealed class WeaponNugget
{
    internal static WeaponNugget Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);
    private static readonly IniParseTable<WeaponNugget> FieldParseTable = new IniParseTable<WeaponNugget>
    {
        { "WeaponName", (parser, x) => x.WeaponName = parser.ParseAssetReference() },
        { "FireDelay", (parser, x) => x.FireDelay = parser.ParseInteger() },
        { "OneShot", (parser, x) => x.OneShot = parser.ParseBoolean() },
        { "Offset", (parser, x) => x.Offset = parser.ParseVector3() }
    };
    public string WeaponName { get; private set; }
    public int FireDelay { get; private set; }
    public bool OneShot { get; private set; }
    public Vector3 Offset { get; private set; }
}
