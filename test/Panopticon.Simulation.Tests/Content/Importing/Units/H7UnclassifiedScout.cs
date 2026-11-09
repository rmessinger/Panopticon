#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Generals;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.Logic.Object;

namespace Panopticon.Tests.Content.Importing.Units;

/// <summary>
/// Synthetic valued-unclassified scout shared by the H7-02 regression and H7-04 fault-injection suites.
/// The Generals INI adapter never produces unclassified fields, so the source set is built directly;
/// resolver, writer, reader, and validator are the production stages.
/// </summary>
internal static class H7UnclassifiedScout
{
    public const string UnitName = "SyntheticScout";

    public const string ValueFieldName = "FutureArmorPenetration";

    public const string BlockFieldName = "FutureComplexBlock";

    public static (UnitImportResolution Resolution, string UnitName) Resolve(bool policyExcluded, bool withBlockField = false)
    {
        var source = new ContentSource("h7-unclassified", caseSensitiveNames: false);
        var provenance = new Provenance(source.Id, "synthetic/scout.ini", 12);
        var unitId = new ContentId(source, ContentKind.Unit, UnitName);

        var unclassified = new List<UnclassifiedField>
        {
            new UnclassifiedField(
                ValueFieldName,
                AffectsGameplay: true,
                provenance,
                ExcludedByPolicy: policyExcluded,
                RawValue: "42.5"),
        };

        if (withBlockField)
        {
            unclassified.Add(new UnclassifiedField(
                BlockFieldName,
                AffectsGameplay: false,
                provenance,
                RawBlock: ["line 1", "line 2"]));
        }

        var unit = new UnitRecord(unitId, provenance)
        {
            DisplayName = Authored.Of("Synthetic Scout"),
            MaxHealth = Authored.Of(new Quantity(120, QuantityUnit.HitPoints)),
            InitialHealth = Authored.Of(new Quantity(120, QuantityUnit.HitPoints)),
            Weapons = ListOverride<ContentId>.Replace([new ContentId(source, ContentKind.Weapon, "ScoutRifle")]),
            Armor = Authored.Of(new ContentId(source, ContentKind.Armor, "ScoutArmor")),
            Locomotors = ListOverride<ContentId>.Replace([new ContentId(source, ContentKind.Locomotor, "ScoutFeet")]),
            Modules = [ModuleEdit.Add(new UnitModule("ModuleTag_Body", "ActiveBody", ModuleKinds.Body))],
            Unclassified = unclassified,
        };

        var weapon = new WeaponRecord(new ContentId(source, ContentKind.Weapon, "ScoutRifle"), provenance)
        {
            PrimaryDamage = Authored.Of(new Quantity(10, QuantityUnit.HitPoints)),
            AttackRange = Authored.Of(new Quantity(120, QuantityUnit.GameDistance)),
            ReloadTime = Authored.Of(new Quantity(30, QuantityUnit.LogicFrames)),
        };

        var armor = new ArmorRecord(new ContentId(source, ContentKind.Armor, "ScoutArmor"), provenance)
        {
            Multipliers = new Dictionary<string, Authored<double>>(StringComparer.OrdinalIgnoreCase)
            {
                ["SMALL_ARMS"] = Authored.Of(0.5),
            },
        };

        var locomotor = new LocomotorRecord(new ContentId(source, ContentKind.Locomotor, "ScoutFeet"), provenance)
        {
            Speed = Authored.Of(new Quantity(30, QuantityUnit.DistancePerSecond)),
        };

        var set = new UnitContentSet(source, [unit, weapon, armor, locomotor], [unitId]);
        return (UnitImportResolution.Resolve(set, GeneralsUnitAdapter.CreateRegistry()), UnitName);
    }

    /// <summary>Retained provider for legacy-free closures; any request is a suite bug.</summary>
    public sealed class EmptyRetainedPayloadProvider : IUnitPackageRetainedPayloadProvider
    {
        public UnitPackageRetainedBlock? GetBlock(UnitRetainedBlockRequest request)
        {
            throw new InvalidOperationException($"No synthetic record needs retention, but '{request.RecordId}' requested a block.");
        }
    }
}
