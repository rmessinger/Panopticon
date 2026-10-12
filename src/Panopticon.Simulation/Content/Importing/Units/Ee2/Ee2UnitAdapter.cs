#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Panopticon.IO;
using Panopticon.Logic.Object;

namespace Panopticon.Content.Importing.Units.Ee2;

/// <summary>
/// Card WI-H2a-16 (extended by WI-M2-01): emits shared interchange for EE2 units. <c>UnitType</c>
/// chains become unit records with their abilities (<c>Move</c>/<c>VehicleMove</c>/<c>AircraftMove</c>,
/// <c>LOS</c>, <c>Attack</c>) as locomotor, vision, and weapon records; the tech-tree CSV row named
/// by an explicit row key becomes a child unit carrying that row's stats and visual, composed with
/// the epoch <c>UpgradeAbilities</c> overrides the row references; <c>UnitModel</c> chains become
/// visual-state records. Distances stay in EE2 tiles (<see cref="QuantityUnit.SourceDistance"/>)
/// for the conversion policy to scale.
/// </summary>
public sealed class Ee2UnitAdapter : IUnitSourceAdapter
{
    private const string AiTag = "ModuleTag_AI";
    private const string PhysicsTag = "ModuleTag_Physics";
    private const string DeathTag = "ModuleTag_Death";

    /// <summary>EE2 abilities override by name, so its modules replace only by tag, never by Generals module kind.</summary>
    private const ModuleInheritanceMode ByTag = ModuleInheritanceMode.AlwaysInherit;

    /// <summary>DDF state names bound to the four engine states.</summary>
    private static readonly (string Engine, string Ddf)[] StateNames = [("Idle", "Idle"), ("Move", "Walk"), ("Attack", "Attack1"), ("Death", "Death")];

    /// <summary>The three EE2 movement ability families, all mapped to one locomotor per unit chain.</summary>
    private static readonly string[] MovementFamilies = ["Move", "VehicleMove", "AircraftMove"];

    /// <summary>Engine state surfaces per movement family; vehicles stay off rubble-climb surfaces.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> FamilySurfaces = new Dictionary<string, string[]>
    {
        ["Move"] = ["GROUND", "RUBBLE"],
        ["VehicleMove"] = ["GROUND"],
        ["AircraftMove"] = ["AIR"],
    };

    /// <summary>db/Simulation/dbcombat.csv defender rows that count as air targets (normalized keys).</summary>
    private static readonly string[] AirCombatRows =
        ["CloseAirSupport", "AirSuperiority", "Bomber", "AtomicBomber", "Helicopter", "AirNonCombat"];

    /// <summary>Attack members consumed into typed weapon fields; everything else stays visible as unclassified.</summary>
    private static readonly HashSet<string> AttackKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "damage", "range", "reloadTime", "applyDamageTime", "missileName", "ordnance", "areaDamageRadius", "minRangeMobile",
    };

    private static readonly HashSet<string> MovementKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "speed", "angSpeed", "CruisingHeight", "canHover",
    };

    private static readonly HashSet<string> NonGameplayKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "DisplayName", "DisplayNameScenEdit", "VerboseTooltip", "icon", "sounds", "fullMapOverlay", "visualName",
    };

    private static readonly HashSet<string> NonGameplayColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "UPGRADE", "UNIT", "TYPE", "EPOCH", "DISPLAYNAME", "VISUAL", "UI VISUAL", "ICON", "MMCOLOR", "FULLMAP", "TOOLTIP",
        "SNDSEL", "SNDMOVE", "SNDATTACK", "SNDDEATH", "UPGRADEREFS",
    };

    private static readonly string[] CostColumns = ["FOOD", "WOOD", "STONE", "GOLD", "TIN", "IRON", "SALTPETER", "OIL", "URANIUM"];

    private readonly Ee2SourceFiles _files;
    private readonly Ee2ImportPolicy _policy;
    private readonly Dictionary<string, DdfRecord> _unitTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DdfRecord> _unitModels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<DdfBlock>> _upgradeAbilities = new(StringComparer.Ordinal);
    private readonly List<ImportDiagnostic> _diagnostics = [];
    private readonly List<ProjectileRecord> _missileRecords = [];
    private readonly HashSet<string> _emittedMissiles = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DdfRecord>? _missiles;
    private Dictionary<string, string>? _graphicsIndex;

    public Ee2UnitAdapter(Ee2SourceFiles files, Ee2ImportPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(policy);
        _files = files;
        _policy = policy;
    }

    public ContentSource Source => ContentSource.Ee2;

    /// <param name="rootNames">EE2 <c>UnitType</c> names; the policy epoch selects the published row.</param>
    public UnitContentSet Read(IReadOnlyCollection<string> rootNames)
    {
        ArgumentNullException.ThrowIfNull(rootNames);

        return ReadRows(rootNames.Select(rootName => $"{rootName}UpgradeEpoch{_policy.Epoch}").ToArray());
    }

    /// <summary>
    /// Card WI-M2-01: publishes exact tech-tree row keys. The row's <c>UNIT</c> cell names the
    /// <c>UnitType</c> (row keys do not always derive from it: epoch 15 tanks publish through
    /// <c>LightMounted1UpgradeEpoch15</c>), and the row's <c>UPGRADEREFS</c> select the
    /// <c>UpgradeAbilities</c> overrides composed onto the inherited chain.
    /// </summary>
    public UnitContentSet ReadRows(IReadOnlyCollection<string> rowKeys)
    {
        ArgumentNullException.ThrowIfNull(rowKeys);

        var records = new List<SourceRecord>();
        var documents = ReadDocuments();

        if (_files.UncomposedPatchArchives.Count > 0)
        {
            Read(
                ImportDiagnosticSeverity.Info,
                DiagnosticCodes.ArchiveLayer,
                $"Reading the stock database layer only; patch archives present but not composed: {string.Join(", ", _files.UncomposedPatchArchives)}.");
        }

        foreach (var record in documents)
        {
            switch (record.Type)
            {
                case "UnitType":
                    _unitTypes.TryAdd(record.Name, record);
                    records.AddRange(ReadUnitType(record));
                    break;
                case "UnitModel":
                    _unitModels.TryAdd(record.Name, record);
                    break;
                case "UpgradeAbilities":
                    _upgradeAbilities.TryAdd(record.Name, ((DdfArray?)record.Body.Get("upgradedAbilities"))?.Items.OfType<DdfBlock>().ToList() ?? []);
                    break;
            }
        }

        foreach (var model in documents.Where(r => r.Type == "UnitModel"))
        {
            records.Add(ReadUnitModel(model));
        }

        var roots = new List<ContentId>();
        var table = ReadUnitTable();
        foreach (var rowKey in rowKeys)
        {
            if (table is null || !table.TryGetRow(rowKey, out var row, out var line))
            {
                Read(ImportDiagnosticSeverity.Error, DiagnosticCodes.MissingRoot, $"No '{rowKey}' row in {_files.UnitTablePath}.", new ContentId(Source, ContentKind.Unit, rowKey));
                continue;
            }

            var unitType = row.TryGetValue("UNIT", out var unitTypeName) && unitTypeName.Length > 0 ? unitTypeName : null;
            if (unitType is null || !_unitTypes.ContainsKey(unitType))
            {
                Read(ImportDiagnosticSeverity.Error, DiagnosticCodes.MissingRoot, $"Row '{rowKey}' names UNIT '{unitType}', which is not among the discovered UnitTypes.", new ContentId(Source, ContentKind.Unit, rowKey));
                continue;
            }

            var provenance = new Provenance(Source.Id, table.File, line);
            records.AddRange(ReadEpochRow(unitType, rowKey, row, provenance));
            roots.Add(UnitId(rowKey));
        }

        records.AddRange(_missileRecords);

        return new UnitContentSet(Source, records, roots, _diagnostics);
    }

    private List<DdfRecord> ReadDocuments()
    {
        var documents = new List<DdfRecord>();
        foreach (var path in _files.DdfPaths)
        {
            var entry = _files.Database.GetFile(path);
            if (entry is null)
            {
                Read(ImportDiagnosticSeverity.Error, DiagnosticCodes.MissingDependency, $"DDF file '{path}' was not found.");
                continue;
            }

            try
            {
                using var reader = new StreamReader(entry.Open());
                documents.AddRange(DdfReader.Read(reader.ReadToEnd(), path));
            }
            catch (InvalidDataException exception)
            {
                Read(ImportDiagnosticSeverity.Error, DiagnosticCodes.UnsupportedMechanic, exception.Message);
            }
        }

        return documents;
    }

    private Ee2CsvTable? ReadUnitTable()
    {
        var entry = _files.Database.GetFile(_files.UnitTablePath);
        if (entry is null)
        {
            Read(ImportDiagnosticSeverity.Error, DiagnosticCodes.MissingDependency, $"Unit table '{_files.UnitTablePath}' was not found.");
            return null;
        }

        using var reader = new StreamReader(entry.Open());
        return Ee2CsvTable.Read(reader.ReadToEnd(), _files.UnitTablePath);
    }

    private IEnumerable<SourceRecord> ReadUnitType(DdfRecord type)
    {
        var provenance = At(type.Position);
        var unclassified = new List<UnclassifiedField>();
        var extra = new List<SourceRecord>();
        var unit = new UnitRecord(UnitId(type.Name), provenance) { Parent = type.Parent is { } parent ? UnitId(parent) : null };
        var modules = new List<ModuleEdit>();

        foreach (var member in type.Body.Members)
        {
            switch (member.Key.ToLowerInvariant())
            {
                case "parent":
                    break;
                case "properties":
                    unit = ReadProperties(unit, (DdfBlock)member.Value, unclassified);
                    break;
                case "abilities":
                    foreach (var ability in ((DdfArray)member.Value).Items.OfType<DdfBlock>())
                    {
                        unit = ReadAbility(type, unit, ability, modules, extra, unclassified);
                    }

                    break;
                default:
                    unclassified.Add(Field(member.Key, member.Position));
                    break;
            }
        }

        extra.Insert(0, unit with { Modules = modules, Unclassified = unclassified });
        return extra;
    }

    private UnitRecord ReadProperties(UnitRecord unit, DdfBlock properties, List<UnclassifiedField> unclassified)
    {
        foreach (var member in properties.Members)
        {
            var text = (member.Value as DdfScalar)?.Text;
            switch (member.Key.ToLowerInvariant())
            {
                case "hitpoints":
                    unit = unit with { MaxHealth = Number(text, QuantityUnit.HitPoints) };
                    break;
                case "sizex":
                    // SizeX is the footprint width in tiles; the engine radius is half of it.
                    unit = unit with { GeometryMajorRadius = Number(text, QuantityUnit.SourceDistance, scale: 0.5) };
                    break;
                case "rps" when _policy.MapsRps:
                    // Card WI-M3-02: consumed into the weapon damage type and the armor set.
                    break;
                case "movetype" when text is "kUnitMoveType_Foot" or "Foot":
                    unit = unit with { KindOf = ListOverride<string>.Replace(["INFANTRY", "SELECTABLE"]) };
                    break;
                case "movetype" when text is "kUnitMoveType_Mounted" or "Mounted"
                    or "kUnitMoveType_Wheeled" or "Wheeled"
                    or "kUnitMoveType_Mechanized" or "Mechanized":
                    unit = unit with { KindOf = ListOverride<string>.Replace(["VEHICLE", "SELECTABLE"]) };
                    break;
                case "movetype" when text is "kUnitMoveType_Air" or "Air":
                    // Generals air targeting matches AIRCRAFT + VEHICLE kinds, so EE2 aircraft
                    // carry both; AIRCRAFT alone would make them untargetable by anti-air weapons.
                    unit = unit with { KindOf = ListOverride<string>.Replace(["AIRCRAFT", "VEHICLE", "SELECTABLE"]) };
                    break;
                default:
                    unclassified.Add(Field(member.Key, member.Position));
                    break;
            }
        }

        return unit;
    }

    private UnitRecord ReadAbility(DdfRecord type, UnitRecord unit, DdfBlock ability, List<ModuleEdit> modules, List<SourceRecord> extra, List<UnclassifiedField> unclassified)
    {
        var name = ability.Name ?? string.Empty;
        switch (name)
        {
            case "Move" or "VehicleMove" or "AircraftMove":
                var locomotor = new LocomotorRecord(new ContentId(Source, ContentKind.Locomotor, $"{type.Name}.Move"), At(ability.Position))
                {
                    Parent = AncestorWithAbility(type, name) is { } moveParent ? new ContentId(Source, ContentKind.Locomotor, $"{moveParent}.Move") : null,
                    Speed = Number(ability.GetText("speed"), QuantityUnit.SourceDistancePerSecond),
                    TurnRate = Number(ability.GetText("angSpeed"), QuantityUnit.DegreesPerSecond),
                    PreferredHeight = Number(ability.GetText("CruisingHeight"), QuantityUnit.SourceDistance),
                    CanHover = Flag(ability, "canHover"),
                    Surfaces = ListOverride<string>.Replace(FamilySurfaces[name]),
                    Appearance = Authored.Of(FamilyAppearance(name, ChainProperty(type.Name, "movetype"), Flag(ability, "canHover"))),
                    Unclassified = ability.Members
                        .Where(m => !MovementKeys.Contains(m.Key))
                        .Select(m => Field($"{name}.{m.Key}", m.Position))
                        .ToArray(),
                };
                extra.Add(locomotor);

                // Aircraft use the engine's typed JetAI update instead of the ground AI module;
                // the shared tag means an inherited ground AI module is replaced, never duplicated.
                var aiFamily = name == "AircraftMove" ? "JetAIUpdate" : "AIUpdateInterface";
                modules.Add(ModuleEdit.Add(new UnitModule(AiTag, aiFamily, ModuleKinds.Update, ByTag)));

                // The engine moves locomotor-driven objects only through a physics behavior.
                modules.Add(ModuleEdit.Add(new UnitModule(PhysicsTag, "PhysicsBehavior", ModuleKinds.Update, ByTag)));
                return unit with { Locomotors = ListOverride<ContentId>.Replace([locomotor.Id]) };

            case "LOS":
                return unit with { VisionRange = Number(ability.GetText("range"), QuantityUnit.SourceDistance) };

            case "Attack":
                var (weapon, attack) = AttackRecords(type, AncestorWithAbility(type, "Attack"), ability.Position);
                extra.Add(ApplyAttackSemantics(weapon with
                {
                    PrimaryDamage = Number(ability.GetText("damage"), QuantityUnit.HitPoints),
                    AttackRange = Number(ability.GetText("range"), QuantityUnit.SourceDistance),
                    ReloadTime = Number(ability.GetText("reloadTime"), QuantityUnit.Seconds),
                }, ability));
                extra.Add(attack);
                modules.Add(ModuleEdit.Add(AttackModule(attack.Id)));
                return unit with { KindOf = WithCanAttack(unit) };

            default:
                unclassified.Add(Field(name, ability.Position));
                return unit;
        }
    }

    /// <summary>
    /// Adds CAN_ATTACK without discarding a classification authored on the same UnitType: a unit
    /// that classifies itself (vehicles, aircraft) must keep those kinds alongside CAN_ATTACK.
    /// </summary>
    private static ListOverride<string> WithCanAttack(UnitRecord unit)
    {
        return unit.KindOf.Mode == ListOverrideMode.Replace
            ? ListOverride<string>.Replace(unit.KindOf.Items.Append("CAN_ATTACK").Distinct(StringComparer.Ordinal))
            : ListOverride<string>.Edit(["CAN_ATTACK"], []);
    }

    /// <summary>
    /// Card WI-M2-01: one epoch row composed over its <c>UnitType</c> chain. CSV cells carry the
    /// published stats; the row's <c>UPGRADEREFS</c> name <c>UpgradeAbilities</c> records whose
    /// ability blocks override attack semantics (projectile, ordnance, timing, minimum range) and
    /// movement values for this epoch.
    /// </summary>
    /// <summary>Classes already emitted as armor records in this read (WI-M3-02).</summary>
    private readonly HashSet<string> _emittedRpsArmors = new(StringComparer.OrdinalIgnoreCase);

    private IEnumerable<SourceRecord> ReadEpochRow(string unitType, string rowKey, IReadOnlyDictionary<string, string> row, Provenance provenance)
    {
        var unclassified = new List<UnclassifiedField>();
        var rps = ChainProperty(unitType, "rps");
        var (weapon, attack) = AttackRecords(rowKey, AncestorWithAbility(unitType, "Attack", includeSelf: true), null, provenance, rps);

        // Card WI-M3-02: while the rps mapping is active, the row's class carries an armor set
        // derived from dbcombat; one record is shared by every row of the same class. A requested
        // row whose class is neither mapped nor excluded fails load explicitly.
        if (rps is not null && !_policy.Excludes("rps") && !_policy.MappedDamageTypes.ContainsKey(NormalizeRps(rps)))
        {
            Read(
                ImportDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedMechanic,
                $"'{rowKey}' has rps class '{rps}', which has no authored Generals damage type; add it to RpsDamageTypes or exclude rps.",
                UnitId(rowKey));
        }

        var armor = RpsArmor(rps, rowKey, provenance);
        var emitArmor = armor is not null && _emittedRpsArmors.Add(armor.Id.Name);

        foreach (var (column, value) in row)
        {
            var isCost = CostColumns.Contains(column, StringComparer.OrdinalIgnoreCase);
            var mapped = column.ToUpperInvariant() is "HP" or "LOS" or "DAMAGE" or "RANGE" or "RELOAD" or "BUILDTIME" or "UPGRADEREFS";
            var empty = string.IsNullOrWhiteSpace(value) || value is "0" or "[]" or "All";
            if (mapped || NonGameplayColumns.Contains(column) || (empty && !isCost))
            {
                continue;
            }

            if (!(isCost && value == "0"))
            {
                unclassified.Add(Field(isCost ? "Cost" : column, null, provenance, isCost ? $"{column}={value}" : value));
            }
        }

        var (attackOverride, moveOverride, overrideProvenance) = ComposeRowOverrides(rowKey, row, provenance, unclassified);

        var locomotorId = (ContentId?)null;
        if (moveOverride is { } movement)
        {
            var family = movement.Name!;
            var chainOwner = AncestorWithAbility(unitType, family, includeSelf: true);
            var locomotor = new LocomotorRecord(new ContentId(Source, ContentKind.Locomotor, $"{rowKey}.Move"), overrideProvenance)
            {
                Parent = chainOwner is { } owner ? new ContentId(Source, ContentKind.Locomotor, $"{owner}.Move") : null,
                Speed = Number(movement.GetText("speed"), QuantityUnit.SourceDistancePerSecond),
                TurnRate = Number(movement.GetText("angSpeed"), QuantityUnit.DegreesPerSecond),
                PreferredHeight = Number(movement.GetText("CruisingHeight"), QuantityUnit.SourceDistance),
                CanHover = Flag(movement, "canHover"),
                Surfaces = ListOverride<string>.Replace(FamilySurfaces[family]),
                Appearance = Authored.Of(FamilyAppearance(family, ChainProperty(unitType, "movetype"), Flag(movement, "canHover"))),
                Unclassified = movement.Members
                    .Where(m => !MovementKeys.Contains(m.Key))
                    .Select(m => Field($"{family}.{m.Key}", null, overrideProvenance))
                    .ToArray(),
            };
            locomotorId = locomotor.Id;
            yield return locomotor;
        }

        var composed = weapon with
        {
            PrimaryDamage = Number(Cell(row, "DAMAGE"), QuantityUnit.HitPoints),
            AttackRange = Number(Cell(row, "RANGE"), QuantityUnit.SourceDistance),
            ReloadTime = Number(Cell(row, "RELOAD"), QuantityUnit.Seconds),
        };

        if (attackOverride is { } attackBlock)
        {
            composed = ApplyAttackSemantics(composed, attackBlock, overrideProvenance);
        }

        var visualName = row.TryGetValue("VISUAL", out var visual) && visual.Length > 0 ? visual : null;

        yield return new UnitRecord(UnitId(rowKey), provenance)
        {
            Parent = UnitId(unitType),
            DisplayName = row.TryGetValue("DISPLAYNAME", out var display) ? Authored.Of(display) : default,
            MaxHealth = Number(Cell(row, "HP"), QuantityUnit.HitPoints),
            VisionRange = Number(Cell(row, "LOS"), QuantityUnit.SourceDistance),
            BuildTime = Number(Cell(row, "BUILDTIME"), QuantityUnit.Seconds),
            Visual = visualName is null ? default : Authored.Of(new ContentId(Source, ContentKind.VisualState, visualName)),
            DeathDuration = Authored.Of(new Quantity(_policy.CorpseSeconds, QuantityUnit.Seconds)),
            Locomotors = locomotorId is null ? ListOverride<ContentId>.Inherit : ListOverride<ContentId>.Replace([locomotorId.Value]),
            Armor = armor is null ? default : Authored.Of(armor.Id),
            Modules =
            [
                ModuleEdit.Add(AttackModule(attack.Id)),
                ModuleEdit.Add(new UnitModule(DeathTag, "SlowDeathBehavior", ModuleKinds.Update | ModuleKinds.Die, ByTag)),
            ],
            Unclassified = unclassified,
        };

        if (emitArmor)
        {
            yield return armor!;
        }

        yield return composed;
        yield return attack;
    }

    /// <summary>
    /// Resolves the row's <c>UPGRADEREFS</c> against the parsed <c>UpgradeAbilities</c> records and
    /// returns the effective attack and movement override blocks (last one wins, matching EE2's
    /// ordered reference lists). Refs that are not ability upgrades (veteran/elite stat sets) stay
    /// visible as non-gameplay metadata instead of being silently dropped.
    /// </summary>
    private (DdfBlock? Attack, DdfBlock? Move, Provenance Provenance) ComposeRowOverrides(
        string rowKey,
        IReadOnlyDictionary<string, string> row,
        Provenance rowProvenance,
        List<UnclassifiedField> unclassified)
    {
        var refs = row.TryGetValue("UPGRADEREFS", out var refCell) ? ParseRefList(refCell) : [];
        var blocks = new List<(DdfBlock Block, string Ref)>();
        var unapplied = new List<string>();

        foreach (var reference in refs)
        {
            if (_upgradeAbilities.TryGetValue(reference, out var abilities))
            {
                blocks.AddRange(abilities.Select(block => (block, reference)));
            }
            else
            {
                unapplied.Add(reference);
            }
        }

        if (unapplied.Count > 0)
        {
            unclassified.Add(new UnclassifiedField($"UPGRADEREFS (stat-set refs not applied: {string.Join(" ", unapplied)})", AffectsGameplay: false, rowProvenance));
        }

        var attack = blocks.LastOrDefault(pair => pair.Block.Name == "Attack");
        var move = blocks.LastOrDefault(pair => MovementFamilies.Contains(pair.Block.Name!));
        var provenance = attack.Block is not null
            ? At(attack.Block.Position)
            : move.Block is not null
                ? At(move.Block.Position)
                : rowProvenance;

        var applied = new[] { attack.Block is null ? null : attack.Ref, move.Block is null ? null : move.Ref }
            .Where(name => name is not null)
            .Distinct()
            .ToList();
        if (applied.Count > 0)
        {
            Read(
                ImportDiagnosticSeverity.Info,
                DiagnosticCodes.AbilityOverride,
                $"Row '{rowKey}' composes ability overrides from {string.Join(", ", applied.Select(name => $"'{name}'"))}.");
        }

        return (attack.Block, move.Block, provenance);
    }

    /// <summary>
    /// Maps attack semantics that survive into the engine: <c>applyDamageTime</c> becomes the
    /// pre-attack delay, <c>missileName</c> a projectile record with the missile's authored speed,
    /// <c>ordnance</c> the clip size, <c>areaDamageRadius</c> the splash radius, and
    /// <c>minRangeMobile</c> the minimum attack range. Every other member stays unclassified.
    /// </summary>
    private WeaponRecord ApplyAttackSemantics(WeaponRecord weapon, DdfBlock attack, Provenance? provenance = null)
    {
        var missileName = attack.GetText("missileName") is { } missile ? missile.Trim() : null;
        if (missileName is { Length: > 0 })
        {
            weapon = weapon with { Projectile = Authored.Of(new ContentId(Source, ContentKind.Projectile, missileName)) };
            EmitMissile(missileName, provenance ?? At(attack.Position));
        }

        return weapon with
        {
            PreAttackDelay = Number(attack.GetText("applyDamageTime"), QuantityUnit.Seconds),
            ClipSize = Count(attack.GetText("ordnance")),
            PrimaryDamageRadius = Number(attack.GetText("areaDamageRadius"), QuantityUnit.SourceDistance),
            MinimumAttackRange = Number(attack.GetText("minRangeMobile"), QuantityUnit.SourceDistance),
            Unclassified = attack.Members
                .Where(m => !AttackKeys.Contains(m.Key))
                .Select(m => Field($"Attack.{m.Key}", m.Position))
                .ToArray(),
        };
    }

    /// <summary>Emits one projectile record per distinct missile, resolved against <c>db/Visuals/ammo.ddf</c>.</summary>
    private void EmitMissile(string missileName, Provenance provenance)
    {
        if (!_emittedMissiles.Add(missileName))
        {
            return;
        }

        if (Missiles().TryGetValue(missileName, out var missile))
        {
            var move = missile.Body.Get("abilities") is DdfArray abilities
                ? abilities.Items.OfType<DdfBlock>().FirstOrDefault(block => block.Name == "ProjectileMissileMove")
                : null;

            _missileRecords.Add(new ProjectileRecord(new ContentId(Source, ContentKind.Projectile, missileName), At(missile.Position))
            {
                Speed = Number(move?.GetText("speed"), QuantityUnit.SourceDistancePerSecond),
                Unclassified = (move?.Members ?? [])
                    .Where(m => !m.Key.Equals("speed", StringComparison.OrdinalIgnoreCase))
                    .Select(m => Field($"ProjectileMissileMove.{m.Key}", m.Position))
                    .Concat(missile.Body.Get("abilities") is DdfArray allAbilities
                        ? allAbilities.Items.OfType<DdfBlock>()
                            .Where(block => block.Name != "ProjectileMissileMove")
                            .Select(block => Field($"Missile.{block.Name}", block.Position))
                        : [])
                    .ToArray(),
            });
        }
        else
        {
            Read(
                ImportDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedMechanic,
                $"Attack requires missile '{missileName}', which was not found in {Ee2SourceFiles.MissileTablePath}.");
        }
    }

    private Dictionary<string, DdfRecord> Missiles()
    {
        if (_missiles is not null)
        {
            return _missiles;
        }

        _missiles = new Dictionary<string, DdfRecord>(StringComparer.OrdinalIgnoreCase);
        var entry = _files.Database.GetFile(Ee2SourceFiles.MissileTablePath);
        if (entry is null)
        {
            Read(ImportDiagnosticSeverity.Error, DiagnosticCodes.MissingDependency, $"Missile table '{Ee2SourceFiles.MissileTablePath}' was not found.");
            return _missiles;
        }

        try
        {
            using var reader = new StreamReader(entry.Open());
            foreach (var record in DdfReader.Read(reader.ReadToEnd(), Ee2SourceFiles.MissileTablePath).Where(r => r.Type == "UnitType"))
            {
                _missiles.TryAdd(record.Name, record);
            }
        }
        catch (InvalidDataException exception)
        {
            Read(ImportDiagnosticSeverity.Error, DiagnosticCodes.UnsupportedMechanic, exception.Message);
        }

        return _missiles;
    }

    private VisualStateRecord ReadUnitModel(DdfRecord model)
    {
        var unclassified = new List<UnclassifiedField>();
        var modelKey = EffectiveModelKey(model.Name);
        var animations = new Dictionary<string, Authored<string>>(StringComparer.OrdinalIgnoreCase);

        if (model.Body.Get("States") is DdfArray states && modelKey is not null)
        {
            var byName = states.Items.OfType<DdfBlock>()
                .Where(state => state.GetText("StateName") is not null)
                .GroupBy(state => state.GetText("StateName")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            foreach (var (engine, ddf) in StateNames)
            {
                if (byName.TryGetValue(ddf, out var state) && state.GetText("AnimName") is { } animName)
                {
                    animations[engine] = Authored.Of($"{modelKey}#kf={GraphicsPath(animName) ?? animName}");
                }
            }

            // Extra source states exist but have no engine condition to bind; keep them visible
            // rather than silently dropping or fabricating clips for them.
            foreach (var extra in byName.Keys.Where(name => !StateNames.Any(state => string.Equals(state.Ddf, name, StringComparison.OrdinalIgnoreCase))).OrderBy(name => name, StringComparer.Ordinal))
            {
                unclassified.Add(new UnclassifiedField($"State.{extra}", AffectsGameplay: false, At(model.Position)));
            }
        }

        foreach (var member in model.Body.Members.Where(m => !m.Key.Equals("Parent", StringComparison.OrdinalIgnoreCase)
                     && !m.Key.Equals("DefaultModel", StringComparison.OrdinalIgnoreCase)
                     && !m.Key.Equals("States", StringComparison.OrdinalIgnoreCase)))
        {
            unclassified.Add(new UnclassifiedField(member.Key, AffectsGameplay: false, At(member.Position)));
        }

        return new VisualStateRecord(new ContentId(Source, ContentKind.VisualState, model.Name), At(model.Position))
        {
            Parent = model.Parent is { } parent ? new ContentId(Source, ContentKind.VisualState, parent) : null,
            ModelKey = model.Body.GetText("DefaultModel") is not null && modelKey is not null ? Authored.Of(modelKey) : default,
            StateAnimations = animations,
            Unclassified = unclassified,
        };
    }

    /// <summary>Model key with the policy's regional suffix when that variant exists in the graphics archive.</summary>
    private string? EffectiveModelKey(string modelName)
    {
        for (var current = modelName; current is not null && _unitModels.TryGetValue(current, out var record); current = record.Parent)
        {
            if (record.Body.GetText("DefaultModel") is { } defaultModel)
            {
                var stem = Path.GetFileNameWithoutExtension(defaultModel);
                return NifCacheStem($"{stem}_{_policy.Region}") ?? NifCacheStem(stem) ?? stem;
            }
        }

        return null;
    }

    private string? NifCacheStem(string stem) => GraphicsIndex.TryGetValue($"{stem}.nifcache", out var path) ? Path.GetFileNameWithoutExtension(path) : null;

    private string? GraphicsPath(string fileName) => GraphicsIndex.TryGetValue(fileName, out var path) ? path : null;

    /// <summary>Lower-case file name to archive path, so DDF names resolve regardless of case.</summary>
    private Dictionary<string, string> GraphicsIndex => _graphicsIndex ??= BuildGraphicsIndex(_files.Graphics);

    private static Dictionary<string, string> BuildGraphicsIndex(FileSystem graphics)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { "Graphics", "cache/nifcache" })
        {
            foreach (var entry in graphics.GetFilesInDirectory(root, "*", SearchOption.AllDirectories))
            {
                index.TryAdd(Path.GetFileName(entry.FilePath), entry.FilePath.Replace('\\', '/'));
            }
        }

        return index;
    }

    private (WeaponRecord Weapon, AbilityRecord Attack) AttackRecords(DdfRecord type, string? parentOwner, DdfPosition? position, Provenance? provenance = null)
    {
        return AttackRecords(type.Name, parentOwner, position, provenance, ChainProperty(type.Name, "rps"));
    }

    private (WeaponRecord Weapon, AbilityRecord Attack) AttackRecords(string owner, string? parentOwner, DdfPosition? position, Provenance? provenance = null, string? rps = null)
    {
        var at = provenance ?? At(position!.Value);
        var weapon = new WeaponRecord(new ContentId(Source, ContentKind.Weapon, $"{owner}.Attack"), at)
        {
            Parent = parentOwner is null ? null : new ContentId(Source, ContentKind.Weapon, $"{parentOwner}.Attack"),
            TargetCategories = ListOverride<string>.Replace(TargetCategories(rps, owner)),
            DamageType = Authored.Of(DamageTypeFor(rps)),
        };
        var attack = new AbilityRecord(new ContentId(Source, ContentKind.Ability, $"{owner}.Attack"), at)
        {
            Family = Authored.Of("Attack"),
            Weapon = Authored.Of(weapon.Id),
        };
        return (weapon, attack);
    }

    /// <summary>
    /// Card WI-M3-02: the weapon's engine damage type comes from the authored rps mapping; the
    /// policy default (UNRESISTABLE one-to-one) applies while rps is an excluded mechanic.
    /// Chain records outside the requested closure keep the default; the row-level check in
    /// <see cref="ReadEpochRow"/> fails unmapped requested rows explicitly.
    /// </summary>
    private string DamageTypeFor(string? rps)
    {
        return rps is not null && !_policy.Excludes("rps") && _policy.MappedDamageTypes.TryGetValue(NormalizeRps(rps), out var damageType)
            ? damageType
            : _policy.DamageType;
    }

    /// <summary>
    /// Card WI-M3-02: one armor record per rps class, multipliers straight from the stock
    /// dbcombat defender row over the mapped attacker types (percent divided by 100, zeros
    /// included). A mapped class with no defender row fails load explicitly.
    /// </summary>
    private ArmorRecord? RpsArmor(string? rps, string owner, Provenance provenance)
    {
        if (rps is null || _policy.Excludes("rps"))
        {
            return null;
        }

        var key = NormalizeRps(rps);
        if (!_policy.MappedDamageTypes.TryGetValue(key, out _))
        {
            return null;
        }

        if (!CombatMatrix().TryGetValue(key, out var row))
        {
            Read(
                ImportDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedMechanic,
                $"rps class '{rps}' has no defender row in {Ee2SourceFiles.CombatTablePath}, so no armor set can be derived.",
                new ContentId(Source, ContentKind.Unit, owner));
            return null;
        }

        var multipliers = new Dictionary<string, Authored<double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (attacker, multiplier) in row.Where(pair => _policy.MappedDamageTypes.TryGetValue(pair.Key, out _)))
        {
            multipliers[_policy.MappedDamageTypes[attacker]] = Authored.Of(multiplier / 100.0);
        }

        return new ArmorRecord(new ContentId(Source, ContentKind.Armor, $"Rps{key}"), provenance)
        {
            Multipliers = multipliers,
        };
    }

    /// Card WI-M2-02: target categories derived from the authored rps matrix
    /// (<c>db/Simulation/dbcombat.csv</c>): a nonzero attacker column against any air defender
    /// row adds the airborne category, against any other row the ground category. The multipliers
    /// themselves stay excluded (M3 owns balance); only the zero/nonzero engagement mask is read.
    /// </summary>
    private IReadOnlyList<string> TargetCategories(string? rps, string owner)
    {
        if (rps is null || !CombatMasks().TryGetValue(NormalizeRps(rps), out var mask))
        {
            Read(
                ImportDiagnosticSeverity.Info,
                DiagnosticCodes.NonGameplayMetadata,
                $"'{owner}' has no rps combat column; its weapon defaults to ground targets only.");
            return ["AntiGround"];
        }

        var categories = new List<string>();
        if ((mask & CombatMask.Air) != 0)
        {
            categories.Add("AntiAirborneVehicle");
        }

        if ((mask & CombatMask.Ground) != 0)
        {
            categories.Add("AntiGround");
        }

        return categories;
    }

    [Flags]
    private enum CombatMask
    {
        None = 0,
        Ground = 1 << 0,
        Air = 1 << 1,
    }

    private Dictionary<string, CombatMask>? _combatMasks;

    private Dictionary<string, CombatMask> CombatMasks()
    {
        if (_combatMasks is not null)
        {
            return _combatMasks;
        }

        _combatMasks = new Dictionary<string, CombatMask>(StringComparer.OrdinalIgnoreCase);
        foreach (var (defender, row) in CombatMatrix())
        {
            var isAir = AirCombatRows.Contains(defender, StringComparer.Ordinal);
            foreach (var (attacker, multiplier) in row)
            {
                if (multiplier > 0)
                {
                    _combatMasks.TryGetValue(attacker, out var existing);
                    _combatMasks[attacker] = existing | (isAir ? CombatMask.Air : CombatMask.Ground);
                }
            }
        }

        return _combatMasks;
    }

    private Dictionary<string, Dictionary<string, double>>? _combatMatrix;

    /// <summary>
    /// Card WI-M3-02: the full dbcombat interaction matrix, defender class to attacker class
    /// multiplier (percent). The target-category masks derive from its zero/nonzero pattern;
    /// WI-M3-02's armor sets read the authored multipliers directly.
    /// </summary>
    private Dictionary<string, Dictionary<string, double>> CombatMatrix()
    {
        if (_combatMatrix is not null)
        {
            return _combatMatrix;
        }

        _combatMatrix = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        var entry = _files.Database.GetFile(Ee2SourceFiles.CombatTablePath);
        if (entry is null)
        {
            Read(ImportDiagnosticSeverity.Error, DiagnosticCodes.MissingDependency, $"Combat table '{Ee2SourceFiles.CombatTablePath}' was not found.");
            return _combatMatrix;
        }

        using var reader = new StreamReader(entry.Open());
        var lines = reader.ReadToEnd().Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToArray();

        // The column header is the last comment line before the first data row (Ee2CsvTable's
        // rule); earlier comment lines also contain commas and must not be mistaken for it.
        var header = lines
            .TakeWhile(line => line.StartsWith("//", StringComparison.Ordinal))
            .LastOrDefault(line => line.Contains(','))?[2..];
        var columns = (header?.Split(',').Skip(1).Select(c => c.Trim()) ?? []).ToArray();

        foreach (var line in lines.Where(line => !line.StartsWith("//", StringComparison.Ordinal)))
        {
            var cells = line.Split(',');
            var defender = NormalizeRps(cells[0].Trim());
            if (defender.Length == 0 || defender == "Null")
            {
                continue;
            }

            var row = _combatMatrix.TryGetValue(defender, out var existing) ? existing : [];
            _combatMatrix[defender] = row;
            for (var i = 1; i < columns.Length + 1 && i < cells.Length; i++)
            {
                var attacker = NormalizeRps(columns[i - 1]);
                if (attacker.Length == 0 || !double.TryParse(cells[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var multiplier))
                {
                    continue;
                }

                row[attacker] = multiplier;
            }
        }

        return _combatMatrix;
    }

    private static string NormalizeRps(string rps) => Ee2ImportPolicy.NormalizeRps(rps);

    private static UnitModule AttackModule(ContentId ability) => new("Attack", "Attack", ModuleKinds.None, ByTag, Ability: ability);

    private string? AncestorWithAbility(DdfRecord type, string ability) => AncestorWithAbility(type.Parent, ability, includeSelf: true);
    /// <summary>First authored value of a <c>properties</c> member along a UnitType chain, or null when no chain member authors it.</summary>
    private string? ChainProperty(string unitType, string property)
    {
        for (var current = unitType; current is not null && _unitTypes.TryGetValue(current, out var record); current = record.Parent)
        {
            if (record.Body.Get("properties") is DdfBlock properties && properties.GetText(property) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Engine appearance per movement family: tracked vehicles use treads, wheeled use wheels,
    /// fixed-wing aircraft use wings, and authored hover capability (helicopters) uses hover.
    /// Mounted (horse) lines have no engine counterpart and reuse the wheeled appearance.
    /// </summary>
    private static string FamilyAppearance(string family, string? moveType, Authored<bool> canHover)
    {
        return family switch
        {
            "Move" => "TWO_LEGS",
            "VehicleMove" => moveType is "kUnitMoveType_Mechanized" or "Mechanized" ? "TREADS" : "FOUR_WHEELS",
            _ => canHover.IsPresent && canHover.Value ? "HOVER" : "WINGS",
        };
    }

    private string? AncestorWithAbility(string? name, string ability, bool includeSelf)
    {
        for (var current = includeSelf ? name : (name is not null && _unitTypes.TryGetValue(name, out var start) ? start.Parent : null);
             current is not null && _unitTypes.TryGetValue(current, out var record);
             current = record.Parent)
        {
            if (record.Body.Get("abilities") is DdfArray abilities && abilities.Items.OfType<DdfBlock>().Any(b => b.Name == ability))
            {
                return current;
            }
        }

        return null;
    }

    private UnclassifiedField Field(string name, DdfPosition? position, Provenance? provenance = null, string? detail = null)
    {
        var gameplay = !NonGameplayKeys.Contains(name);
        var label = detail is null ? name : $"{name} ({detail})";
        return new UnclassifiedField(label, gameplay, provenance ?? (position is { } p ? At(p) : null), ExcludedByPolicy: gameplay && _policy.Excludes(name));
    }

    private static string? Cell(IReadOnlyDictionary<string, string> row, string column) => row.TryGetValue(column, out var value) ? value : null;

    private static string[] ParseRefList(string cell) => cell.Trim().Trim('[', ']').Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Authored<Quantity> Number(string? text, QuantityUnit unit, double scale = 1)
    {
        return text is not null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Authored.Of(new Quantity(value * scale, unit))
            : default;
    }

    private static Authored<int> Count(string? text)
    {
        return text is not null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Authored.Of(value)
            : default;
    }

    private static Authored<bool> Flag(DdfBlock block, string key)
    {
        var text = block.GetText(key);
        return text is null ? default : Authored.Of(text is "1" or "true" or "yes");
    }

    private ContentId UnitId(string name) => new(Source, ContentKind.Unit, name);

    private Provenance At(DdfPosition position) => new(Source.Id, position.File, position.Line);

    private void Read(ImportDiagnosticSeverity severity, string code, string message, ContentId? subject = null)
    {
        _diagnostics.Add(new ImportDiagnostic(severity, ImportStage.Read, code, message, subject));
    }
}
