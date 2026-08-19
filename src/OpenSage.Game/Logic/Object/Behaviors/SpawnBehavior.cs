#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Content;
using OpenSage.Data.Ini;

namespace OpenSage.Logic.Object;

internal sealed class SpawnBehavior : UpdateModule
{
    SpawnBehaviorModuleData _moduleData;

    private List<GameObject> _spawnedUnits;
    private bool _initial;
    private LogicFrame _nextReplaceFrame;
    private IProductionExit? _productionExit;
    private OpenContainModule? _openContain;

    private bool _unknownBool1;
    private string? _templateName;
    private int _unknownInt1;
    private int _unknownInt2;
    private readonly List<uint> _unknownIntList = new();
    private readonly List<ObjectId> _unknownObjectList = new();
    private ushort _unknownInt3;
    private int _unknownInt4;

    protected override UpdateOrder UpdateOrder => UpdateOrder.Order2;

    internal SpawnBehavior(GameObject gameObject, IGameEngine gameEngine, SpawnBehaviorModuleData moduleData) : base(gameObject, gameEngine)
    {
        _moduleData = moduleData;

        _spawnedUnits = new List<GameObject>();
        _initial = true;
    }

    // Ring radius for ExitByBudding spawns: members bud off scattered around the spawner
    // instead of marching out of a production door.
    private const float BuddingRadius = 35f;

    private void SpawnUnit()
    {
        var spawnedObject = GameEngine.GameLogic.CreateObject(PickSpawnTemplate()?.Value, GameObject.Owner);
        _spawnedUnits.Add(spawnedObject);

        spawnedObject.CreatedByObjectID = GameObject.Id;
        var slavedUpdate = spawnedObject.FindBehavior<SlavedUpdateModule>();
        slavedUpdate?.SetMaster(GameObject);
        spawnedObject.FindBehavior<MobMemberSlavedUpdate>()?.SetMaster(GameObject);

        if (_moduleData.ExitByBudding)
        {
            var angle = GameEngine.GameLogic.Random.NextSingle(0, MathF.PI * 2);
            var offset = new Vector3(
                MathF.Cos(angle) * BuddingRadius,
                MathF.Sin(angle) * BuddingRadius,
                0f);
            spawnedObject.SetTranslation(GameObject.Translation + offset);
            return;
        }

        if (!TryTransformViaProductionExit(spawnedObject) &&
            !TryTransformViaOpenContainer(spawnedObject))
        {
            throw new Exception("Unable to set spawn point for spawned unit");
        }
    }

    private LazyAssetReference<ObjectDefinition>? PickSpawnTemplate()
    {
        // Multiple SpawnTemplateName lines define a pool the spawner draws from randomly
        // (e.g. the Angry Mob's pistol/rock/molotov mix). The draw uses the deterministic
        // logic RNG so replays and the validation ground stay reproducible.
        var templates = _moduleData.SpawnTemplates;
        if (templates.Count == 0)
        {
            return null;
        }

        return templates.Count == 1
            ? templates[0]
            : templates[GameEngine.GameLogic.Random.Next(0, templates.Count - 1)];
    }

    private int AliveSpawnedCount()
    {
        _spawnedUnits.RemoveAll(u => u.IsEffectivelyDead);
        return _spawnedUnits.Count;
    }

    private bool TryTransformViaProductionExit(GameObject spawnedObject)
    {
        _productionExit ??= GameObject.FindBehavior<IProductionExit>();

        if (_productionExit == null)
        {
            return false;
        }

        if (_productionExit != null)
        {
            spawnedObject.SetTranslation(GameObject.ToWorldspace(_productionExit.GetUnitCreatePoint()));

            var rallyPoint = _productionExit.GetNaturalRallyPoint();
            if (rallyPoint.HasValue)
            {
                spawnedObject.AIUpdate?.AddTargetPoint(GameObject.ToWorldspace(rallyPoint.Value));
            }
        }

        var productionUpdate = GameObject.FindBehavior<ProductionUpdate>();
        if (productionUpdate != null)
        {
            ProductionUpdate.HandleHarvesterUnitCreation(GameObject, spawnedObject);
        }

        return true;
    }

    // GLATunnelNetwork has no production exit behavior - it's explicitly commented out, saying "... we don't appear to need it for the spawns because they use OpenContain instead"
    private bool TryTransformViaOpenContainer(GameObject spawnedObject)
    {
        _openContain ??= GameObject.FindBehavior<OpenContainModule>();

        if (_openContain == null)
        {
            return false;
        }

        // spawn at container output
        var (exitStart, exitEnd) = _openContain.DefaultExitPath;

        if (exitStart.HasValue && exitEnd.HasValue)
        {
            spawnedObject.SetTranslation(GameObject.ToWorldspace(exitStart.Value));
            spawnedObject.AIUpdate.AddTargetPoint(GameObject.ToWorldspace(exitEnd.Value));
        }
        else
        {
            spawnedObject.SetTranslation(GameObject.Translation);
        }

        // move to rally point
        var rallyPoint = GameObject.RallyPoint;
        if (rallyPoint.HasValue)
        {
            spawnedObject.AIUpdate?.AddTargetPoint(rallyPoint.Value);
        }

        return true;
    }

    public void SpawnInitial()
    {
        // InitialBurst units appear immediately (the Angry Mob's first five members); when no
        // burst is configured, the full SpawnNumber appears as before.
        var count = _moduleData.InitialBurst > 0 ? _moduleData.InitialBurst : _moduleData.SpawnNumber;
        for (var i = 0; i < count && AliveSpawnedCount() < _moduleData.SpawnNumber; i++)
        {
            SpawnUnit();
        }
        _nextReplaceFrame = GameEngine.GameLogic.CurrentFrame + ReplaceDelayFrames();
    }

    private LogicFrameSpan ReplaceDelayFrames()
    {
        // SpawnReplaceDelay is authored in milliseconds.
        return LogicFrameSpan.FromMilliseconds(_moduleData.SpawnReplaceDelay, GameEngine.SageGame);
    }

    public override UpdateSleepTime Update()
    {
        if (_initial && !GameObject.IsBeingConstructed())
        {
            SpawnInitial();
            _initial = false;
        }

        // Maintenance: top the spawn count back up toward SpawnNumber (covering both the burst
        // trickle-up and replacing killed members) on the SpawnReplaceDelay cadence. OneShot
        // spawners stop once SpawnNumber has been reached even if members die later.
        if (!_initial
            && !_moduleData.OneShot
            && AliveSpawnedCount() < _moduleData.SpawnNumber
            && GameEngine.GameLogic.CurrentFrame >= _nextReplaceFrame)
        {
            SpawnUnit();
            _nextReplaceFrame = GameEngine.GameLogic.CurrentFrame + ReplaceDelayFrames();
        }

        return UpdateSleepTime.None;
    }

    internal override void Load(StatePersister reader)
    {
        reader.PersistVersion(2);

        reader.BeginObject("Base");
        base.Load(reader);
        reader.EndObject();

        reader.PersistBoolean(ref _unknownBool1);
        reader.PersistAsciiString(ref _templateName);
        reader.PersistInt32(ref _unknownInt1);
        reader.PersistInt32(ref _unknownInt2);

        reader.SkipUnknownBytes(4);

        var unknownBool2 = true;
        reader.PersistBoolean(ref unknownBool2);
        if (!unknownBool2)
        {
            throw new InvalidStateException();
        }

        reader.PersistList(
            _unknownIntList,
            static (StatePersister persister, ref uint item) =>
            {
                persister.PersistUInt32Value(ref item);
            });

        var unknownBool3 = true;
        reader.PersistBoolean(ref unknownBool3);
        if (!unknownBool3)
        {
            throw new InvalidStateException();
        }

        reader.PersistList(
            _unknownObjectList,
            static (StatePersister persister, ref ObjectId item) =>
            {
                persister.PersistObjectIdValue(ref item);
            });

        reader.PersistUInt16(ref _unknownInt3);
        if (_unknownInt3 != 0 && _unknownInt3 != 1)
        {
            throw new InvalidStateException();
        }

        reader.PersistInt32(ref _unknownInt4);
        if (_unknownInt4 != _unknownObjectList.Count && _unknownInt4 != -1)
        {
            throw new InvalidStateException();
        }

        reader.SkipUnknownBytes(4);
    }
}

public sealed class SpawnBehaviorModuleData : UpgradeModuleData
{
    internal static SpawnBehaviorModuleData Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

    private new static readonly IniParseTable<SpawnBehaviorModuleData> FieldParseTable = UpgradeModuleData.FieldParseTable
        .Concat(new IniParseTable<SpawnBehaviorModuleData>
        {
            { "SpawnNumber", (parser, x) => x.SpawnNumber = parser.ParseInteger() },
            { "SpawnReplaceDelay", (parser, x) => x.SpawnReplaceDelay = parser.ParseLong() },
            { "SpawnTemplateName", (parser, x) => x.SpawnTemplates.Add(parser.ParseObjectReference()!) },
            { "OneShot", (parser, x) => x.OneShot = parser.ParseBoolean() },
            { "CanReclaimOrphans", (parser, x) => x.CanReclaimOrphans = parser.ParseBoolean() },
            { "SpawnedRequireSpawner", (parser, x) => x.SpawnedRequireSpawner = parser.ParseBoolean() },
            { "ExitByBudding", (parser, x) => x.ExitByBudding = parser.ParseBoolean() },
            { "InitialBurst", (parser, x) => x.InitialBurst = parser.ParseInteger() },
            { "AggregateHealth", (parser, x) => x.AggregateHealth = parser.ParseBoolean() },
            { "SlavesHaveFreeWill", (parser, x) => x.SlavesHaveFreeWill = parser.ParseBoolean() },
            { "RespectCommandLimit", (parser, x) => x.RespectCommandLimit = parser.ParseBoolean() },
            { "KillSpawnsBasedOnModelConditionState", (parser, x) => x.KillSpawnsBasedOnModelConditionState = parser.ParseBoolean() },
            { "ShareUpgrades", (parser, x) => x.ShareUpgrades = parser.ParseBoolean() },
            { "FadeInTime", (parser, x) => x.FadeInTime = parser.ParseInteger() },
            { "SpawnInsideBuilding", (parser, x) => x.SpawnInsideBuilding = parser.ParseBoolean() },
        });

    public int SpawnNumber { get; private set; }
    public long SpawnReplaceDelay { get; private set; }

    /// <summary>
    /// The spawn template pool: one entry per SpawnTemplateName line. Spawners with a single
    /// entry behave as before; multiple entries (the Angry Mob's member mix) draw randomly.
    /// </summary>
    public List<LazyAssetReference<ObjectDefinition>> SpawnTemplates { get; } = new();

    /// <summary>Single-template convenience over <see cref="SpawnTemplates"/>.</summary>
    public LazyAssetReference<ObjectDefinition>? SpawnTemplate => SpawnTemplates.FirstOrDefault();
    public bool OneShot { get; private set; }
    public bool CanReclaimOrphans { get; private set; }
    public bool SpawnedRequireSpawner { get; private set; }
    public bool ExitByBudding { get; private set; }
    public int InitialBurst { get; private set; }
    public bool AggregateHealth { get; private set; }

    [AddedIn(SageGame.CncGeneralsZeroHour)]
    public bool SlavesHaveFreeWill { get; private set; }

    [AddedIn(SageGame.Bfme)]
    public bool RespectCommandLimit { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public bool KillSpawnsBasedOnModelConditionState { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public bool ShareUpgrades { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public int FadeInTime { get; private set; }

    [AddedIn(SageGame.Bfme2)]
    public bool SpawnInsideBuilding { get; private set; }

    internal override BehaviorModule CreateModule(GameObject gameObject, IGameEngine gameEngine)
    {
        return new SpawnBehavior(gameObject, gameEngine, this);
    }
}
