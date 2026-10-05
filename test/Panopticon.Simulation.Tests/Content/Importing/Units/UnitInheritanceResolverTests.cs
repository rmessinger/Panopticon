using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Logic.Object;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitInheritanceResolverTests
{
    private static InheritanceResult Resolve(params SourceRecord[] records)
    {
        return new UnitInheritanceResolver().Resolve(new UnitContentSet(Source, records, []));
    }

    private static UnitRecord ResolvedUnit(InheritanceResult result, string name) => (UnitRecord)result.Resolved[Id(ContentKind.Unit, name)];

    [Fact]
    public void OmittedFieldInheritsAndZeroOverrides()
    {
        var parent = Unit("Base") with { MaxHealth = Authored.Of(Hp(100)), BuildCost = Authored.Of(Money(500)) };
        var child = Unit("Child", "Base") with { BuildCost = Authored.Of(Money(0)) };

        var result = Resolve(parent, child);
        var resolved = ResolvedUnit(result, "Child");

        Assert.Empty(result.Diagnostics);
        Assert.Null(resolved.Parent);
        Assert.Equal(100, resolved.MaxHealth.Value.Value);
        Assert.Equal(parent.Id, resolved.MaxHealth.Origin);
        Assert.Equal(0, resolved.BuildCost.Value.Value);
        Assert.Equal(child.Id, resolved.BuildCost.Origin);
    }

    [Fact]
    public void EmptyListReplacesAndInheritKeeps()
    {
        var parent = Unit("Base") with { KindOf = ListOverride<string>.Replace(["INFANTRY", "SELECTABLE"]) };
        var cleared = Unit("Cleared", "Base") with { KindOf = ListOverride<string>.Replace([]) };
        var kept = Unit("Kept", "Base");
        var edited = Unit("Edited", "Base") with { KindOf = ListOverride<string>.Edit(["HERO"], ["selectable"]) };

        var result = Resolve(parent, cleared, kept, edited);

        Assert.Empty(ResolvedUnit(result, "Cleared").KindOf.Resolved);
        Assert.Equal(["INFANTRY", "SELECTABLE"], ResolvedUnit(result, "Kept").KindOf.Resolved);
        Assert.Equal(["INFANTRY", "HERO"], ResolvedUnit(result, "Edited").KindOf.Resolved);
    }

    [Fact]
    public void MultipleLevelsResolveInOrderRegardlessOfRecordOrder()
    {
        var grandchild = Unit("C", "B") with { VisionRange = Authored.Of(Distance(150)) };
        var child = Unit("B", "A") with { BuildCost = Authored.Of(Money(300)) };
        var root = Unit("A") with { MaxHealth = Authored.Of(Hp(80)), VisionRange = Authored.Of(Distance(100)) };

        var resolved = ResolvedUnit(Resolve(grandchild, child, root), "C");

        Assert.Equal(80, resolved.MaxHealth.Value.Value);
        Assert.Equal(300, resolved.BuildCost.Value.Value);
        Assert.Equal(150, resolved.VisionRange.Value.Value);
        Assert.Equal(root.Id, resolved.MaxHealth.Origin);
    }

    [Fact]
    public void SourceRecordsAreNotMutated()
    {
        var parent = Unit("Base") with { MaxHealth = Authored.Of(Hp(100)) };
        var child = Unit("Child", "Base");

        Resolve(parent, child);

        Assert.False(child.MaxHealth.IsPresent);
        Assert.Equal(parent.Id, child.Parent);
        Assert.Null(parent.MaxHealth.Origin);
    }

    [Fact]
    public void MissingParentFailsChildOnly()
    {
        var result = Resolve(Unit("Orphan", "Nowhere"), Unit("Fine"));

        Assert.Equal(DiagnosticCodes.MissingParent, Assert.Single(result.Diagnostics).Code);
        Assert.False(result.Resolved.ContainsKey(Id(ContentKind.Unit, "Orphan")));
        Assert.True(result.Resolved.ContainsKey(Id(ContentKind.Unit, "Fine")));
    }

    [Fact]
    public void FailedParentPropagatesToDescendants()
    {
        var result = Resolve(Unit("Orphan", "Nowhere"), Unit("Child", "Orphan"));

        Assert.Empty(result.Resolved);
        Assert.All(result.Diagnostics, d => Assert.Equal(DiagnosticCodes.MissingParent, d.Code));
    }

    [Fact]
    public void CycleIsReported()
    {
        var result = Resolve(Unit("A", "B"), Unit("B", "A"));

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.InheritanceCycle);
        Assert.Empty(result.Resolved);
    }

    [Fact]
    public void ExcessiveDepthIsReported()
    {
        var records = Enumerable.Range(0, UnitInheritanceResolver.MaxDepth + 2)
            .Select(i => (SourceRecord)Unit($"U{i}", i == 0 ? null : $"U{i - 1}"))
            .ToArray();

        var result = Resolve(records);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.InheritanceTooDeep);
    }

    [Fact]
    public void DuplicateRecordIsReported()
    {
        var result = Resolve(Unit("Twin"), Unit("twin"));

        Assert.Equal(DiagnosticCodes.DuplicateRecord, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(result.Resolved);
    }

    [Fact]
    public void DefaultModuleYieldsToOverlappingKind()
    {
        var parent = Unit("Base") with { Modules = [ModuleEdit.Add(Body("ModuleTag_BaseBody"))] };
        var child = Unit("Child", "Base") with
        {
            Modules = [ModuleEdit.Add(new UnitModule("ModuleTag_Body", "StructureBody", ModuleKinds.Body))],
        };

        var modules = ResolvedUnit(Resolve(parent, child), "Child").ResolvedModules.ToList();

        Assert.Equal("ModuleTag_Body", Assert.Single(modules).Tag);
        Assert.False(modules[0].Inherited);
    }

    [Fact]
    public void AlwaysInheritSurvivesUntilRemovedByTag()
    {
        var inheritable = new UnitModule("ModuleTag_Squish", "SquishCollide", ModuleKinds.Collide, ModuleInheritanceMode.AlwaysInherit);
        var parent = Unit("Base") with { Modules = [ModuleEdit.Add(inheritable)] };
        var keeps = Unit("Keeps", "Base") with
        {
            Modules = [ModuleEdit.Add(new UnitModule("ModuleTag_Other", "FireWeaponCollide", ModuleKinds.Collide))],
        };
        var removes = Unit("Removes", "Base") with { Modules = [ModuleEdit.Remove("ModuleTag_Squish")] };

        var result = Resolve(parent, keeps, removes);

        Assert.Contains(ResolvedUnit(result, "Keeps").ResolvedModules, m => m.Tag == "ModuleTag_Squish" && m.Inherited);
        Assert.Empty(ResolvedUnit(result, "Removes").ResolvedModules);
    }

    [Fact]
    public void OverrideableByLikeKindYieldsToSameFamilyOnly()
    {
        var overrideable = new UnitModule("ModuleTag_Death", "DestroyDie", ModuleKinds.Die, ModuleInheritanceMode.OverrideableByLikeKind);
        var parent = Unit("Base") with { Modules = [ModuleEdit.Add(overrideable)] };
        var otherFamily = Unit("Other", "Base") with
        {
            Modules = [ModuleEdit.Add(new UnitModule("ModuleTag_Fx", "FXListDie", ModuleKinds.Die))],
        };
        var sameFamily = Unit("Same", "Base") with
        {
            Modules = [ModuleEdit.Add(new UnitModule("ModuleTag_Death2", "DestroyDie", ModuleKinds.Die))],
        };

        var result = Resolve(parent, otherFamily, sameFamily);

        Assert.Equal(2, ResolvedUnit(result, "Other").ResolvedModules.Count());
        Assert.Equal("ModuleTag_Death2", Assert.Single(ResolvedUnit(result, "Same").ResolvedModules).Tag);
    }

    [Fact]
    public void ReplaceSwapsModuleAndExclusiveGroupReplaces()
    {
        var parent = Unit("Base") with
        {
            Modules =
            [
                ModuleEdit.Add(new UnitModule("ModuleTag_AI", "AIUpdateInterface", ModuleKinds.Update, ExclusiveGroup: "AIUpdate")),
                ModuleEdit.Add(new UnitModule("ModuleTag_AI2", "TransportAIUpdate", ModuleKinds.Update, ExclusiveGroup: "AIUpdate")),
                ModuleEdit.Replace("ModuleTag_AI2", new UnitModule("ModuleTag_AI3", "DozerAIUpdate", ModuleKinds.Update)),
            ],
        };

        var modules = ResolvedUnit(Resolve(parent), "Base").ResolvedModules.ToList();

        Assert.Equal(["ModuleTag_AI3"], modules.Select(m => m.Tag));
    }

    [Fact]
    public void ArmorMultipliersMergePerDamageType()
    {
        var parent = Armor("Base") with
        {
            Multipliers = new Dictionary<string, Authored<double>> { ["SMALL_ARMS"] = Authored.Of(0.5), ["FLAME"] = Authored.Of(1.0) },
        };
        var child = new ArmorRecord(Id(ContentKind.Armor, "Child"), At(9))
        {
            Parent = parent.Id,
            Multipliers = new Dictionary<string, Authored<double>> { ["FLAME"] = Authored.Of(0.0) },
        };

        var resolved = (ArmorRecord)Resolve(parent, child).Resolved[child.Id];

        Assert.Equal(0.5, resolved.Multipliers["SMALL_ARMS"].Value);
        Assert.Equal(0.0, resolved.Multipliers["FLAME"].Value);
        Assert.Equal(child.Id, resolved.Multipliers["FLAME"].Origin);
    }

    [Fact]
    public void ParentOfDifferentRecordTypeIsRejected()
    {
        var weapon = Weapon("Shared");
        var unit = Unit("U") with { Parent = Id(ContentKind.Unit, "Shared") };
        var badUnitParent = new UnitRecord(Id(ContentKind.Unit, "Bad"), At(1)) { Parent = Id(ContentKind.Unit, "U") };

        var result = Resolve(weapon, unit, badUnitParent);

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.MissingParent);
    }
}
