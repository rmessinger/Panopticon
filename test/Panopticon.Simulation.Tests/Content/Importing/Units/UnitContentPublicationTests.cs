using System;
using System.Linq;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.Logic.Object;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.MaterializationFixtures;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitContentPublicationTests
{
    private static AssetStore CreateStore()
    {
        var store = new AssetStore(SageGame.CncGenerals, null, null, null, null, null, null, OnDemandAssetLoadStrategy.None);
        store.PushScope();
        return store;
    }

    private static MaterializedUnitContent Prepare(AssetStore store, UnitContentSet set)
    {
        var publisher = new UnitContentPublisher(store);
        var resolution = UnitImportResolution.Resolve(set, Registry);
        Assert.True(resolution.Succeeded);
        return UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(Policy, publisher.Lookup, visualStore: store));
    }

    private static ContentId Soldier => Id(ContentKind.Unit, "Soldier");

    [Fact]
    public void PublishesWholeClosureIntoSourceCollections()
    {
        var store = CreateStore();
        var content = Prepare(store, ValidSoldier());

        var result = new UnitContentPublisher(store).Publish(content);
        var collections = store.GetSourceContent(Source);
        var definition = collections.ObjectDefinitions.GetByName("Soldier");

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        Assert.Same(content.Get<ObjectDefinition>(Soldier), definition);
        Assert.Same(collections.WeaponTemplates.GetByName("Rifle"), definition.WeaponSets.Values.Single().Slots[0].Weapon.Value);
        Assert.Null(store.ObjectDefinitions.GetByName("Soldier"));
        Assert.Equal(4, result.Published!.Ids.Count);
    }

    [Fact]
    public void SameNameInAnotherSourceDoesNotCollide()
    {
        var store = CreateStore();
        var other = new ContentSource("other", caseSensitiveNames: true);
        var publisher = new UnitContentPublisher(store);
        Assert.True(publisher.Publish(Prepare(store, ValidSoldier())).Succeeded);

        var otherSoldier = new UnitRecord(new ContentId(other, ContentKind.Unit, "Soldier"), At(1)) { MaxHealth = Authored.Of(Hp(5)) };
        var otherContent = Prepare(store, new UnitContentSet(other, [otherSoldier], [otherSoldier.Id]));

        Assert.True(publisher.Publish(otherContent).Succeeded);
        Assert.NotSame(store.GetSourceContent(Source).ObjectDefinitions.GetByName("Soldier"), store.GetSourceContent(other).ObjectDefinitions.GetByName("Soldier"));
    }

    [Fact]
    public void CollisionWithinSourceIsRejectedBeforeAnyRegistration()
    {
        var store = CreateStore();
        var publisher = new UnitContentPublisher(store);
        publisher.Publish(Prepare(store, ValidSoldier()));
        var depth = store.ScopeDepth;

        var result = publisher.Publish(Prepare(store, ValidSoldier()));

        Assert.False(result.Succeeded);
        Assert.Equal(4, result.Diagnostics.Count(d => d.Code == DiagnosticCodes.NameCollision));
        Assert.Equal(depth, store.ScopeDepth);
    }

    [Fact]
    public void ExplicitOverrideShadowsLowerScope()
    {
        var store = CreateStore();
        var publisher = new UnitContentPublisher(store);
        publisher.Publish(Prepare(store, ValidSoldier()));

        var buffed = Unit("Soldier") with { MaxHealth = Authored.Of(Hp(500)) };
        var result = publisher.Publish(Prepare(store, Set(buffed)), overrides: [buffed.Id]);

        Assert.True(result.Succeeded);
        var body = (ActiveBodyModuleData)store.GetSourceContent(Source).ObjectDefinitions.GetByName("Soldier").Behaviors[UnitBodyMovementMaterializer.DefaultBodyTag].Data;
        Assert.Equal(500f, body.MaxHealth);
    }

    [Fact]
    public void MidPublicationFailureRollsBackEverything()
    {
        var store = CreateStore();
        var depth = store.ScopeDepth;
        var publisher = new UnitContentPublisher(store)
        {
            AfterRegister = id =>
            {
                if (id.Kind == ContentKind.Armor)
                {
                    throw new InvalidOperationException("injected");
                }
            },
        };

        var result = publisher.Publish(Prepare(store, ValidSoldier()));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.PublicationFailed && d.Message.Contains("injected"));
        Assert.Equal(depth, store.ScopeDepth);
        Assert.Null(publisher.Lookup(Soldier));
        Assert.Null(publisher.Lookup(Id(ContentKind.Weapon, "Rifle")));
    }

    [Fact]
    public void VisualRequestsWithoutInstallerBlockPublication()
    {
        var store = CreateStore();
        var visual = new VisualStateRecord(Id(ContentKind.VisualState, "Art"), At(1))
        {
            ModelKey = Authored.Of("m"),
            StateAnimations = VisualStateRecord.RequiredStates.ToDictionary(s => s, _ => Authored.Of("a")),
        };
        var unit = Unit("Soldier") with { MaxHealth = Authored.Of(Hp(1)), Visual = Authored.Of(visual.Id) };

        var result = new UnitContentPublisher(store).Publish(Prepare(store, Set(unit, visual)));

        Assert.False(result.Succeeded);
        Assert.Null(store.GetSourceContent(Source).ObjectDefinitions.GetByName("Soldier"));
    }

    [Fact]
    public void UnloadRemovesContentAndCachedReferencesStopResolving()
    {
        var store = CreateStore();
        var publisher = new UnitContentPublisher(store);
        var content = Prepare(store, ValidSoldier());
        var result = publisher.Publish(content);
        var weaponReference = content.Get<ObjectDefinition>(Soldier).WeaponSets.Values.Single().Slots[0].Weapon;

        Assert.NotNull(weaponReference.Value);

        publisher.Unload(result.Published!);

        Assert.Null(publisher.Lookup(Soldier));
        Assert.Null(weaponReference.Value);
        Assert.True(result.Published!.IsUnloaded);
    }

    [Fact]
    public void UnloadEnforcesReverseOrderAndLiveObjects()
    {
        var store = CreateStore();
        var publisher = new UnitContentPublisher(store);
        var first = publisher.Publish(Prepare(store, ValidSoldier())).Published!;
        var other = new ContentSource("other", caseSensitiveNames: true);
        var tank = new UnitRecord(new ContentId(other, ContentKind.Unit, "Tank"), At(1)) { MaxHealth = Authored.Of(Hp(5)) };
        var second = publisher.Publish(Prepare(store, new UnitContentSet(other, [tank], [tank.Id]))).Published!;

        Assert.Throws<InvalidOperationException>(() => publisher.Unload(first));

        var liveTank = (ObjectDefinition)publisher.Lookup(tank.Id)!;
        Assert.Throws<InvalidOperationException>(() => publisher.Unload(second, [liveTank]));

        publisher.Unload(second, []);
        publisher.Unload(first);
        Assert.Throws<InvalidOperationException>(() => publisher.Unload(first));
    }

    [Fact]
    public void GeneralsSourceCollidesWithBuiltIns()
    {
        var store = CreateStore();
        var builtIn = new ObjectDefinition("AmericaInfantryRanger");
        store.ObjectDefinitions.Add(builtIn);
        var ranger = new UnitRecord(new ContentId(ContentSource.Generals, ContentKind.Unit, "americainfantryranger"), At(1)) { MaxHealth = Authored.Of(Hp(1)) };
        var registry = new UnitCapabilityRegistry(UnitCapabilityRegistry.DefaultTypedModuleFamilies, [], UnitCapabilityRegistry.DefaultAbilityFamilies);
        var resolution = UnitImportResolution.Resolve(new UnitContentSet(ContentSource.Generals, [ranger], [ranger.Id]), registry);
        var content = UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(Policy));

        var result = new UnitContentPublisher(store).Publish(content);

        Assert.Equal(DiagnosticCodes.NameCollision, Assert.Single(result.Diagnostics).Code);
        Assert.Same(builtIn, store.ObjectDefinitions.GetByName("AmericaInfantryRanger"));
    }
}
