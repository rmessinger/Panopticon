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
/// Card WI-H2a-16: emits shared interchange for EE2 ground units. <c>UnitType</c> chains become unit
/// records with their abilities (<c>Move</c>, <c>LOS</c>, <c>Attack</c>) as locomotor, vision, and
/// weapon records; the tech-tree CSV row for the policy epoch becomes a child unit carrying that
/// epoch's stats and visual; <c>UnitModel</c> chains become visual-state records. Distances stay in
/// EE2 tiles (<see cref="QuantityUnit.SourceDistance"/>) for the conversion policy to scale.
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

    private static readonly HashSet<string> NonGameplayKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "DisplayName", "DisplayNameScenEdit", "VerboseTooltip", "icon", "sounds", "fullMapOverlay",
    };

    private static readonly HashSet<string> NonGameplayColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "UPGRADE", "UNIT", "TYPE", "EPOCH", "DISPLAYNAME", "VISUAL", "UI VISUAL", "ICON", "MMCOLOR", "FULLMAP", "TOOLTIP",
        "SNDSEL", "SNDMOVE", "SNDATTACK", "SNDDEATH",
    };

    private static readonly string[] CostColumns = ["FOOD", "WOOD", "STONE", "GOLD", "TIN", "IRON", "SALTPETER", "OIL", "URANIUM"];

    private readonly Ee2SourceFiles _files;
    private readonly Ee2ImportPolicy _policy;
    private readonly Dictionary<string, DdfRecord> _unitTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DdfRecord> _unitModels = new(StringComparer.Ordinal);
    private readonly List<ImportDiagnostic> _diagnostics = [];
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

        var records = new List<SourceRecord>();
        var documents = ReadDocuments();

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
            }
        }

        foreach (var model in documents.Where(r => r.Type == "UnitModel"))
        {
            records.Add(ReadUnitModel(model));
        }

        var roots = new List<ContentId>();
        var table = ReadUnitTable();
        foreach (var unitType in rootNames)
        {
            var rowKey = $"{unitType}UpgradeEpoch{_policy.Epoch}";
            if (table is null || !table.TryGetRow(rowKey, out var row, out var line))
            {
                Read(ImportDiagnosticSeverity.Error, DiagnosticCodes.MissingRoot, $"No '{rowKey}' row in {_files.UnitTablePath}.", new ContentId(Source, ContentKind.Unit, rowKey));
                continue;
            }

            var provenance = new Provenance(Source.Id, table.File, line);
            records.AddRange(ReadEpochRow(unitType, rowKey, row, provenance));
            roots.Add(UnitId(rowKey));
        }

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
                case "movetype" when text == "kUnitMoveType_Foot":
                    unit = unit with { KindOf = ListOverride<string>.Replace(["INFANTRY", "SELECTABLE"]) };
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
            case "Move":
                var locomotor = new LocomotorRecord(new ContentId(Source, ContentKind.Locomotor, $"{type.Name}.Move"), At(ability.Position))
                {
                    Parent = AncestorWithAbility(type, "Move") is { } moveParent ? new ContentId(Source, ContentKind.Locomotor, $"{moveParent}.Move") : null,
                    Speed = Number(ability.GetText("speed"), QuantityUnit.SourceDistancePerSecond),
                    TurnRate = Number(ability.GetText("angSpeed"), QuantityUnit.DegreesPerSecond),
                    Surfaces = ListOverride<string>.Replace(["GROUND", "RUBBLE"]),
                    Appearance = Authored.Of("TWO_LEGS"),
                };
                extra.Add(locomotor);
                modules.Add(ModuleEdit.Add(new UnitModule(AiTag, "AIUpdateInterface", ModuleKinds.Update, ByTag)));

                // The engine moves locomotor-driven objects only through a physics behavior.
                modules.Add(ModuleEdit.Add(new UnitModule(PhysicsTag, "PhysicsBehavior", ModuleKinds.Update, ByTag)));
                return unit with { Locomotors = ListOverride<ContentId>.Replace([locomotor.Id]) };

            case "LOS":
                return unit with { VisionRange = Number(ability.GetText("range"), QuantityUnit.SourceDistance) };

            case "Attack":
                var (weapon, attack) = AttackRecords(type.Name, AncestorWithAbility(type, "Attack"), ability.Position);
                extra.Add(weapon with
                {
                    PrimaryDamage = Number(ability.GetText("damage"), QuantityUnit.HitPoints),
                    AttackRange = Number(ability.GetText("range"), QuantityUnit.SourceDistance),
                    ReloadTime = Number(ability.GetText("reloadTime"), QuantityUnit.Seconds),
                    Unclassified = ability.Members.Where(m => m.Key is not ("damage" or "range" or "reloadTime")).Select(m => Field($"Attack.{m.Key}", m.Position)).ToArray(),
                });
                extra.Add(attack);
                modules.Add(ModuleEdit.Add(AttackModule(attack.Id)));
                return unit with { KindOf = ListOverride<string>.Edit(["CAN_ATTACK"], []) };

            default:
                unclassified.Add(Field(name, ability.Position));
                return unit;
        }
    }

    private IEnumerable<SourceRecord> ReadEpochRow(string unitType, string rowKey, IReadOnlyDictionary<string, string> row, Provenance provenance)
    {
        var unclassified = new List<UnclassifiedField>();
        var (weapon, attack) = AttackRecords(rowKey, AncestorWithAbility(unitType, "Attack", includeSelf: true), null, provenance);

        foreach (var (column, value) in row)
        {
            var isCost = CostColumns.Contains(column, StringComparer.OrdinalIgnoreCase);
            var mapped = column.ToUpperInvariant() is "HP" or "LOS" or "DAMAGE" or "RANGE" or "RELOAD" or "BUILDTIME";
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
            Modules =
            [
                ModuleEdit.Add(AttackModule(attack.Id)),
                ModuleEdit.Add(new UnitModule(DeathTag, "SlowDeathBehavior", ModuleKinds.Update | ModuleKinds.Die, ByTag)),
            ],
            Unclassified = unclassified,
        };

        yield return weapon with
        {
            PrimaryDamage = Number(Cell(row, "DAMAGE"), QuantityUnit.HitPoints),
            AttackRange = Number(Cell(row, "RANGE"), QuantityUnit.SourceDistance),
            ReloadTime = Number(Cell(row, "RELOAD"), QuantityUnit.Seconds),
        };

        yield return attack;
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

    private (WeaponRecord Weapon, AbilityRecord Attack) AttackRecords(string owner, string? parentOwner, DdfPosition? position, Provenance? provenance = null)
    {
        var at = provenance ?? At(position!.Value);
        var weapon = new WeaponRecord(new ContentId(Source, ContentKind.Weapon, $"{owner}.Attack"), at)
        {
            Parent = parentOwner is null ? null : new ContentId(Source, ContentKind.Weapon, $"{parentOwner}.Attack"),
            TargetCategories = ListOverride<string>.Replace(["AntiGround"]),
            DamageType = Authored.Of(_policy.DamageType),
        };
        var attack = new AbilityRecord(new ContentId(Source, ContentKind.Ability, $"{owner}.Attack"), at)
        {
            Family = Authored.Of("Attack"),
            Weapon = Authored.Of(weapon.Id),
        };
        return (weapon, attack);
    }

    private static UnitModule AttackModule(ContentId ability) => new("Attack", "Attack", ModuleKinds.None, ByTag, Ability: ability);

    private string? AncestorWithAbility(DdfRecord type, string ability) => AncestorWithAbility(type.Parent, ability, includeSelf: true);

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

    private static Authored<Quantity> Number(string? text, QuantityUnit unit, double scale = 1)
    {
        return text is not null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Authored.Of(new Quantity(value * scale, unit))
            : default;
    }

    private ContentId UnitId(string name) => new(Source, ContentKind.Unit, name);

    private Provenance At(DdfPosition position) => new(Source.Id, position.File, position.Line);

    private void Read(ImportDiagnosticSeverity severity, string code, string message, ContentId? subject = null)
    {
        _diagnostics.Add(new ImportDiagnostic(severity, ImportStage.Read, code, message, subject));
    }
}
