using System;
using System.Linq;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Logic.Object;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.MaterializationFixtures;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitBodyMovementMaterializerTests
{
    private static UnitRecord Walker() => Unit("Walker") with
    {
        DisplayName = Authored.Of("Walker"),
        MaxHealth = Authored.Of(Hp(120)),
        InitialHealth = Authored.Of(Hp(90)),
        GeometryMajorRadius = Authored.Of(new Quantity(0.5, QuantityUnit.Meters)),
        GeometryHeight = Authored.Of(Distance(12)),
        VisionRange = Authored.Of(Distance(200)),
        KindOf = ListOverride<string>.Replace(["INFANTRY", "SELECTABLE"]),
        Locomotors = ListOverride<ContentId>.Replace([Id(ContentKind.Locomotor, "Legs")]),
        Modules =
        [
            ModuleEdit.Add(Body()),
            ModuleEdit.Add(new UnitModule("ModuleTag_AI", "AIUpdateInterface", ModuleKinds.Update)),
        ],
    };

    private static LocomotorRecord Legs() => Locomotor("Legs") with
    {
        Speed = Authored.Of(Speed(30)),
        TurnRate = Authored.Of(new Quantity(180, QuantityUnit.DegreesPerSecond)),
        Acceleration = Authored.Of(new Quantity(90, QuantityUnit.DistancePerSecondSquared)),
        Appearance = Authored.Of("TWO_LEGS"),
        Surfaces = ListOverride<string>.Replace(["GROUND", "RUBBLE"]),
    };

    [Fact]
    public void BodyGeometryAndVisionMaterialize()
    {
        var content = Materialize(Set(Walker(), Legs()));
        var definition = content.Get<ObjectDefinition>(Id(ContentKind.Unit, "Walker"));
        var body = (ActiveBodyModuleData)definition.Behaviors["ModuleTag_Body"].Data;

        Assert.True(content.Succeeded);
        Assert.Equal("Walker", definition.Name);
        Assert.Equal(120f, body.MaxHealth);
        Assert.Equal(90f, body.InitialHealth);
        Assert.Equal(5f, definition.Geometry.MajorRadius);
        Assert.Equal(12f, definition.Geometry.Shapes[0].Height);
        Assert.Equal(200f, definition.VisionRange);
        Assert.True(definition.KindOf.Get(ObjectKinds.Infantry));
        Assert.IsType<AIUpdateModuleData>(definition.Behaviors["ModuleTag_AI"].Data);
    }

    [Fact]
    public void LocomotorConvertsPerSecondToPerFrame()
    {
        var content = Materialize(Set(Walker(), Legs()));
        var locomotor = content.Get<LocomotorTemplate>(Id(ContentKind.Locomotor, "Legs"));

        Assert.Equal(1f, locomotor.MaxSpeed, 5);
        Assert.Equal((float)(Math.PI / 30), locomotor.MaxTurnRate, 5);
        Assert.Equal(0.1f, locomotor.Acceleration, 5);
        Assert.Equal(LocomotorAppearance.TwoLegs, locomotor.Appearance);
        Assert.Equal(Surfaces.Ground | Surfaces.Rubble, locomotor.Surfaces);
    }

    [Fact]
    public void LocomotorSetReferencesStagedTemplate()
    {
        var content = Materialize(Set(Walker(), Legs()));
        var definition = content.Get<ObjectDefinition>(Id(ContentKind.Unit, "Walker"));

        var reference = Assert.Single(definition.LocomotorSets[LocomotorSetType.Normal].Locomotors);
        Assert.Same(content.Get<LocomotorTemplate>(Id(ContentKind.Locomotor, "Legs")), reference.Value);
    }

    [Fact]
    public void ConversionsKeepSourceValues()
    {
        var content = Materialize(Set(Walker(), Legs()));
        var radius = Assert.Single(content.Conversions, c => c.Field == nameof(UnitRecord.GeometryMajorRadius));

        Assert.Equal(new Quantity(0.5, QuantityUnit.Meters), radius.Source);
        Assert.Equal(5, radius.Runtime);
    }

    [Fact]
    public void MissingBodyModuleGetsDefaultBody()
    {
        var unit = Unit("Bare") with { MaxHealth = Authored.Of(Hp(10)) };
        var definition = Materialize(Set(unit)).Get<ObjectDefinition>(unit.Id);

        Assert.Equal(10f, ((ActiveBodyModuleData)definition.Behaviors[UnitBodyMovementMaterializer.DefaultBodyTag].Data).MaxHealth);
    }

    [Fact]
    public void UnknownSurfaceFailsMaterialization()
    {
        var content = Materialize(Set(Walker(), Legs() with { Surfaces = ListOverride<string>.Replace(["LAVA"]) }));

        Assert.False(content.Succeeded);
        Assert.Contains(content.Diagnostics, d => d.Code == DiagnosticCodes.MaterializationFailed && d.Message.Contains("LAVA"));
    }

    [Fact]
    public void MetersWithoutScaleFail()
    {
        var resolution = UnitImportResolution.Resolve(Set(Walker(), Legs()), Registry);
        var content = UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(new UnitConversionPolicy(SageGame.CncGenerals)));

        Assert.False(content.Succeeded);
    }

    [Fact]
    public void InvalidSpeedNeverReachesMaterialization()
    {
        var resolution = UnitImportResolution.Resolve(Set(Walker(), Legs() with { Speed = Authored.Of(Speed(double.NaN)) }), Registry);

        Assert.False(resolution.Succeeded);
        Assert.Throws<InvalidOperationException>(() => UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(Policy)));
    }

    [Fact]
    public void DefinitionsAreDetached()
    {
        var content = Materialize(Set(Walker(), Legs()));

        Assert.All(content.Assets.Values, asset => Assert.Equal(0, asset.InternalId));
    }

    [Fact]
    public void InheritedModulesOfSameKindAllSurviveMaterialization()
    {
        var parent = Walker() with
        {
            Modules =
            [
                ModuleEdit.Add(new UnitModule("ModuleTag_AI", "AIUpdateInterface", ModuleKinds.Update)),
                ModuleEdit.Add(new UnitModule("ModuleTag_Physics", "PhysicsBehavior", ModuleKinds.Update)),
            ],
        };
        var child = Unit("Child", "Walker") with { MaxHealth = Authored.Of(Hp(100)) };

        var definition = Materialize(Set(child, parent, Legs())).Get<ObjectDefinition>(child.Id);

        Assert.IsType<AIUpdateModuleData>(definition.Behaviors["ModuleTag_AI"].Data);
        Assert.IsType<PhysicsBehaviorModuleData>(definition.Behaviors["ModuleTag_Physics"].Data);
    }
}
