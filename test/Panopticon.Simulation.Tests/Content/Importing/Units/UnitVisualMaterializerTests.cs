using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Gltf;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.MaterializationFixtures;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitVisualMaterializerTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Content", "Importing", "Gltf", "Fixtures");

    private static VisualStateRecord Visual(string model = "animated-skin.glb", string animation = "animated-skin.glb#animation=0")
    {
        return new VisualStateRecord(Id(ContentKind.VisualState, "SoldierArt"), At(7))
        {
            ModelKey = Authored.Of(model),
            StateAnimations = new Dictionary<string, Authored<string>>
            {
                ["Idle"] = Authored.Of(animation),
                ["Move"] = Authored.Of(animation),
                ["Attack"] = Authored.Of(animation),
                ["Death"] = Authored.Of(animation),
            },
        };
    }

    private static MaterializedUnitContent MaterializeWithVisual(VisualStateRecord visual)
    {
        var unit = Unit("Soldier") with { MaxHealth = Authored.Of(Hp(100)), Visual = Authored.Of(visual.Id) };
        return Materialize(Set(unit, visual));
    }

    private static ImportedVisualInstaller FixtureInstaller()
    {
        var context = new AssetImportContext(new DiskFileSystem(FixtureRoot), language: null);
        return new ImportedVisualInstaller(new GltfModelImporter(new GltfImportOptions()), new GltfAnimationImporter(new GltfImportOptions()), context);
    }

    [Fact]
    public void DrawModuleBindsFourStatesToConditionFlags()
    {
        var content = MaterializeWithVisual(Visual());
        var definition = content.Get<ObjectDefinition>(Id(ContentKind.Unit, "Soldier"));
        var draw = Assert.IsType<W3dModelDrawModuleData>(definition.Draws[UnitVisualMaterializer.DrawTag].Data);

        Assert.Equal("synthetic:animated-skin.glb", Assert.Single(draw.ConditionStates).Model.Name);
        Assert.Equal(["IDLE", "MOVE", "ATTACK", "DEATH"], draw.AnimationStates.Select(s => s.StateName));
        Assert.True(draw.AnimationStates[0].IsIdleAnimation);
        Assert.True(draw.AnimationStates[1].ConditionFlags[0].Get(ModelConditionFlag.Moving));
        Assert.True(draw.AnimationStates[2].ConditionFlags[0].Get(ModelConditionFlag.FiringA));
        Assert.True(draw.AnimationStates[2].ConditionFlags[1].Get(ModelConditionFlag.BetweenShotsFiringA));
        Assert.True(draw.AnimationStates[3].ConditionFlags[0].Get(ModelConditionFlag.Dying));
        Assert.Equal(AnimationMode.Once, draw.AnimationStates[3].Animations[0].AnimationMode);
    }

    [Fact]
    public void RequestsCarrySourceKeysAndQualifiedNames()
    {
        var content = MaterializeWithVisual(Visual());

        var model = Assert.Single(content.VisualAssets, r => r.Kind == VisualAssetKind.Model);
        Assert.Equal("animated-skin.glb", model.SourceKey);
        Assert.Equal("synthetic:animated-skin.glb", model.RuntimeName);
        Assert.Equal(4, content.VisualAssets.Count(r => r.Kind == VisualAssetKind.Animation));
    }

    [Fact]
    public void InstallerVerifiesRealImportedAssets()
    {
        var content = MaterializeWithVisual(Visual());

        Assert.Empty(FixtureInstaller().Verify(content.VisualAssets));
    }

    [Fact]
    public void MissingAssetIsReported()
    {
        var content = MaterializeWithVisual(Visual(model: "missing.glb"));

        var diagnostic = Assert.Single(FixtureInstaller().Verify(content.VisualAssets));
        Assert.Equal(DiagnosticCodes.MissingVisualState, diagnostic.Code);
        Assert.Contains("missing.glb", diagnostic.Message);
    }

    [Fact]
    public void MissingStateFailsValidation()
    {
        var visual = Visual() with
        {
            StateAnimations = new Dictionary<string, Authored<string>> { ["Idle"] = Authored.Of("a"), ["Move"] = Authored.Of("a"), ["Attack"] = Authored.Of("a") },
        };
        var unit = Unit("Soldier") with { MaxHealth = Authored.Of(Hp(100)), Visual = Authored.Of(visual.Id) };

        var resolution = UnitImportResolution.Resolve(Set(unit, visual), Registry);

        Assert.Contains(resolution.Diagnostics, d => d.Code == DiagnosticCodes.MissingVisualState && d.Message.Contains("Death"));
    }

    [Fact]
    public void UnitWithoutVisualHasNoDrawAndNoRequests()
    {
        var content = Materialize(ValidSoldier());

        Assert.Empty(content.VisualAssets);
        Assert.Empty(content.Get<ObjectDefinition>(Id(ContentKind.Unit, "Soldier")).Draws);
    }

    [Fact]
    public void MaterializationNeedsNoGraphicsDevice()
    {
        var content = MaterializeWithVisual(Visual());

        Assert.True(content.Succeeded);
        Assert.Null(((W3dModelDrawModuleData)content.Get<ObjectDefinition>(Id(ContentKind.Unit, "Soldier")).Draws[UnitVisualMaterializer.DrawTag].Data).ConditionStates[0].Model.Value);
    }
}
