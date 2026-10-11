using System.Collections.Generic;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.FX;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.MaterializationFixtures;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitDamageEffectLifecycleTests : MockedGameTest
{
    private sealed class EffectRecorder : FXNugget
    {
        public List<(uint Frame, Vector3 Position, Quaternion Rotation)> Hits { get; } = [];

        internal override void Execute(FXListExecutionContext context)
        {
            Hits.Add((context.GameEngine.GameLogic.CurrentFrame.Value, context.Position, context.Rotation));
        }
    }

    private sealed class BoundsClient(AxisAlignedBoundingBox bounds, params AxisAlignedBoundingBox[] extraParts) : IGameObjectClient
    {
        public uint ClientObjectId { get; set; }

        public bool ShadowsEnabled { get; set; }

        public void LogicTick(in TimeInterval gameTime) { }

        public void Destroy() { }

        public void TriggerSelection() { }

        public void SetAnimationDuration(LogicFrameSpan frames) { }

        public void SetSupplyBoxesRemaining(float boxPercentage) { }

        public void SetTerrainDecal(ObjectDecalType decalType) { }

        public void ShowSubObject(string subObject) { }

        public void HideSubObject(string subObject) { }

        public void StopSway() { }

        public void SetDebrisModel(string modelName) { }

        public bool TryGetBoneTransform(string boneName, out Transform boneTransform)
        {
            boneTransform = null!;
            return false;
        }

        public bool TryGetBonePose(string boneName, out Matrix4x4 pose)
        {
            pose = default;
            return false;
        }

        public Matrix4x4? GetWeaponLaunchBoneTransform(WeaponSlot slot, int weaponIndex) => null;

        public Matrix4x4? GetWeaponFireFXBoneTransform(WeaponSlot slot, int weaponIndex) => null;

        public bool TryGetModelSpaceBounds(out AxisAlignedBoundingBox modelBounds)
        {
            modelBounds = bounds;
            return true;
        }

        public bool TryGetModelSpacePartBounds(out System.Collections.Generic.IReadOnlyList<AxisAlignedBoundingBox> partBounds)
        {
            partBounds = [bounds, .. extraParts];
            return true;
        }

        public void DrawInspector() { }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MappedTargetPlaysOnAttackerFacingBounds(bool sourceAlive)
    {
        var hull = new AxisAlignedBoundingBox(new Vector3(-20, -5, 0), new Vector3(20, 5, 10));
        var barrel = new AxisAlignedBoundingBox(new Vector3(18, -1, 4), new Vector3(70, 1, 6));
        var configured = RunWeapon(true, true, hull, sourceAlive, barrel);
        var origin = RunWeapon(true, true);

        Assert.Equal(origin.Health, configured.Health);
        Assert.Equal(origin.Dead, configured.Dead);
        Assert.Equal(origin.Hits.Count, configured.Hits.Count);

        // Victim at (60, 20, 0) with a 0.5 rad yaw; the attacker stands at (10, 20, 0). A barrel-like
        // part sticks out to local +X; the contact must sit on the hull surface, not the barrel tip.
        var expected = new Vector3(60, 20, 0);
        if (sourceAlive)
        {
            var localToWorld = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f))
                * Matrix4x4.CreateTranslation(60, 20, 0);
            Assert.True(DamageFXContact.TryGetAttackerFacingSurfacePoint([hull, barrel], localToWorld, new Vector3(10, 20, 0), out expected));
            Assert.NotEqual(new Vector3(60, 20, 0), expected);
            Assert.True(Vector3.Distance(new Vector3(60, 20, 0), expected) < 25, $"contact {expected} escapes the hull extent");
        }

        Assert.All(configured.Hits, hit => Assert.True(Vector3.Distance(expected, hit.Position) < 1e-3f, $"expected {expected}, actual {hit.Position}"));
    }

    [Fact]
    public void NativeAndUnmappedArmorSetsKeepOriginPlacement()
    {
        Assert.Equal(DamageFXPlacement.VictimOrigin, new ArmorTemplateSet().DamageFXPlacement);
        var content = Materialize(UnitDamageEffectPolicyTests.TankContent());
        Assert.Equal(DamageFXPlacement.VictimOrigin, Assert.Single(content.Get<ObjectDefinition>(Id(ContentKind.Unit, "Tank")).ArmorSets).DamageFXPlacement);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RealWeaponHitsUseVictimPlacementAndThrottleWithoutChangingDamageOrDeath(bool hasArmor)
    {
        var configured = RunWeapon(true, hasArmor);
        var unconfigured = RunWeapon(false, hasArmor);

        Assert.Equal(unconfigured.Health, configured.Health);
        Assert.Equal(unconfigured.DamageCount, configured.DamageCount);
        Assert.True(configured.Dead);
        Assert.Equal(unconfigured.Dead, configured.Dead);
        Assert.True(configured.Hits.Count > 1);
        Assert.True(configured.Hits.Count < configured.DamageCount);
        Assert.All(configured.Hits, hit =>
        {
            Assert.Equal(new Vector3(60, 20, 0), hit.Position);
            Assert.Equal(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f), hit.Rotation);
        });
        for (var i = 1; i < configured.Hits.Count; i++)
        {
            Assert.True(configured.Hits[i].Frame - configured.Hits[i - 1].Frame >= 3);
        }

        Assert.Empty(unconfigured.Hits);
    }

    private static (List<float> Health, int DamageCount, bool Dead, List<(uint Frame, Vector3 Position, Quaternion Rotation)> Hits) RunWeapon(bool configured, bool hasArmor, AxisAlignedBoundingBox? bounds = null, bool sourceAlive = true, AxisAlignedBoundingBox? extraPart = null)
    {
        var game = new TestGame(SageGame.CncGenerals);
        var recorder = new EffectRecorder();
        var fxList = new FXList();
        fxList.Nuggets.Add(recorder);
        var effect = new DamageFX();
        var group = effect.Groups[(int)DamageType.SmallArms, (int)VeterancyLevel.Regular];
        group.ThrottleTime = new LogicFrameSpan(3);
        group.MajorFX = new LazyAssetReference<FXList>(fxList);
        group.MinorFX = group.MajorFX;
        var source = hasArmor ? UnitDamageEffectPolicyTests.TankContent() : Set(Unit("Tank") with
        {
            MaxHealth = Authored.Of(Hp(100)),
            KindOf = ListOverride<string>.Replace(["VEHICLE"]),
        });
        var resolution = UnitImportResolution.Resolve(source, Registry);
        var content = UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(
            Policy, damageEffects: configured ? new Dictionary<ContentId, string> { [Id(ContentKind.Unit, "Tank")] = "TankDamageFX" } : null,
            damageEffectLookup: _ => effect));
        Assert.True(content.Succeeded);
        var victim = game.GameLogic.CreateObject(content.Get<ObjectDefinition>(Id(ContentKind.Unit, "Tank")), null!);
        victim.UpdateTransform(new Vector3(60, 20, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f));
        if (bounds is { } modelBounds)
        {
            victim.Client = extraPart is { } part
                ? new BoundsClient(modelBounds, part)
                : new BoundsClient(modelBounds);
        }

        var attacker = game.GameLogic.CreateObject(new ObjectDefinition("Shooter"), null!);
        attacker.UpdateTransform(new Vector3(10, 20, 0), Quaternion.Identity);
        if (!sourceAlive)
        {
            attacker.IsEffectivelyDead = true;
        }

        var template = new WeaponTemplate { AttackRange = 100, CoolDownDelayBetweenShots = new RangeDuration(LogicFrameSpan.One) };
        template.Nuggets.Add(new DamageNugget { Damage = 10, DamageType = DamageType.SmallArms, DeathType = DeathType.Normal });
        var weapon = new Weapon(attacker, template, WeaponSlot.Primary, game.GameEngine);
        weapon.SetTarget(new WeaponTarget(game.GameLogic, victim.Id));
        var health = new List<float>();
        var damageCount = 0;
        var previous = victim.BodyModule.Health;
        for (var frame = 0; frame < 300 && victim.BodyModule.Health > 0; frame++)
        {
            game.GameLogic.Update();
            // The mock host has no drawable. Execute the actual weapon damage nugget, not synthetic damage inputs.
            template.Nuggets[0].Execute(new WeaponEffectExecutionContext(weapon, game.GameEngine));
            health.Add(victim.BodyModule.Health);
            if (victim.BodyModule.Health < previous)
            {
                damageCount++;
            }

            previous = victim.BodyModule.Health;
        }

        Assert.Equal(hasArmor ? 20 : 10, damageCount);
        return (health, damageCount, victim.IsEffectivelyDead, recorder.Hits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PublicationNeverReplacesNativeDefinitionsOrEffects(bool valid)
    {
        var store = Generals.AssetStore;
        var native = new ObjectDefinition("Tank");
        store.ObjectDefinitions.Add(native);
        var effect = new DamageFX();
        effect.AssignImportedName("DamageFX", "TankDamageFX");
        store.DamageFXs.Add(effect);
        var publisher = new UnitContentPublisher(store);
        var resolution = UnitImportResolution.Resolve(UnitDamageEffectPolicyTests.TankContent(), Registry);
        var content = UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(
            Policy, publisher.Lookup, damageEffects: new Dictionary<ContentId, string> { [Id(ContentKind.Unit, "Tank")] = valid ? "TankDamageFX" : "Missing" },
            damageEffectLookup: name => store.DamageFXs.GetByName(name)));
        var depth = store.ScopeDepth;
        if (valid)
        {
            var result = publisher.Publish(content);
            Assert.True(result.Succeeded);
            Assert.Same(effect, Assert.Single(((ObjectDefinition)publisher.Lookup(Id(ContentKind.Unit, "Tank"))!).ArmorSets).DamageFX.Value);
            publisher.Unload(result.Published!);
        }
        else
        {
            Assert.Throws<System.InvalidOperationException>(() => publisher.Publish(content));
            Assert.Null(publisher.Lookup(Id(ContentKind.Unit, "Tank")));
        }

        Assert.Same(native, store.ObjectDefinitions.GetByName("Tank"));
        Assert.Same(effect, store.DamageFXs.GetByName("TankDamageFX"));
        Assert.Empty(native.ArmorSets);
        Assert.Equal(depth, store.ScopeDepth);
    }
}
