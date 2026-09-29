using System;
using System.IO;
using System.Numerics;
using Panopticon.Content.Importing.Nif;
using Panopticon.Graphics;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public class NifParticleEffectAttachmentResolverTests
{
    private static ModelBone[] CreateBones(params (int Index, string Name, int ParentIndex)[] entries)
    {
        var bones = new ModelBone[entries.Length];
        foreach (var (index, name, parentIndex) in entries)
        {
            bones[index] = new ModelBone(
                index,
                name,
                parentIndex == -1 ? null : bones[parentIndex],
                Vector3.Zero,
                Quaternion.Identity,
                Matrix4x4.Identity);
        }

        return bones;
    }

    private static NifParticleEffectBinding CreateBinding(
        string? targetBoneName = null,
        bool followTarget = false,
        float worldScale = 1.0f,
        Vector3 localOffset = default)
    {
        return new NifParticleEffectBinding(
            "fx_proof",
            "fx_proof.nif",
            targetBoneName,
            followTarget,
            localOffset,
            worldScale,
            oneShot: !followTarget,
            ddfSource: "db/Visuals/proof.ddf:ProofBlock");
    }

    [Fact]
    public void RootAttachmentResolvesToTheUniqueRootBone()
    {
        var bones = CreateBones((0, "Root", -1), (1, "Body", 0), (2, "Muzzle", 1));
        var binding = CreateBinding();

        var index = NifParticleEffectAttachmentResolver.ResolveBoneIndex(bones, binding);

        Assert.Equal(0, index);
    }

    [Fact]
    public void TagAttachmentMatchesCaseInsensitively()
    {
        var bones = CreateBones((0, "Root", -1), (1, "Muzzle", 0));
        var binding = CreateBinding(targetBoneName: "MUZZLE", followTarget: true);

        var index = NifParticleEffectAttachmentResolver.ResolveBoneIndex(bones, binding);

        Assert.Equal(1, index);
    }

    [Fact]
    public void MissingTagFailsNamingDdfSourceEffectAndTarget()
    {
        var bones = CreateBones((0, "Root", -1));
        var binding = CreateBinding(targetBoneName: "Muzzle", followTarget: true);

        var exception = Assert.Throws<InvalidDataException>(
            () => NifParticleEffectAttachmentResolver.ResolveBoneIndex(bones, binding));

        Assert.Contains("fx_proof", exception.Message);
        Assert.Contains("db/Visuals/proof.ddf:ProofBlock", exception.Message);
        Assert.Contains("Muzzle", exception.Message);
        Assert.Contains("no bone named", exception.Message);
    }

    [Fact]
    public void DuplicateTagFailsNamingDdfSourceEffectAndTarget()
    {
        var bones = CreateBones((0, "Root", -1), (1, "Muzzle", 0), (2, "muzzle", 0));
        var binding = CreateBinding(targetBoneName: "MUZZLE", followTarget: true);

        var exception = Assert.Throws<InvalidDataException>(
            () => NifParticleEffectAttachmentResolver.ResolveBoneIndex(bones, binding));

        Assert.Contains("2 bones named 'MUZZLE'", exception.Message);
    }

    [Fact]
    public void WorldSpawnAppliesScaleAndOffsetOnceToTheOriginMatrix()
    {
        var binding = CreateBinding(worldScale: 0.5f, localOffset: new Vector3(10, 20, 30));
        var origin = Matrix4x4.CreateTranslation(1, 2, 3);

        var plan = NifParticleEffectAttachmentResolver.ResolveToWorldSpawn(binding, origin);

        Assert.Equal(NifParticleAttachmentKind.WorldSpawn, plan.Kind);
        Assert.Equal(-1, plan.BoneIndex);
        Assert.Null(plan.FollowMatrix);
        Assert.Equal(Matrix4x4.CreateScale(0.5f) * origin * Matrix4x4.CreateTranslation(10, 20, 30), plan.FixedWorldMatrix);
        Assert.Equal(new Vector3(11, 22, 33), plan.FixedWorldMatrix.Translation);
        Assert.Equal(0.5f, plan.FixedWorldMatrix.M11);
    }

    [Fact]
    public void FollowBonePlanReadsTheLiveBoneMatrixWithOffsetAndScale()
    {
        var bones = CreateBones((0, "Root", -1), (1, "Muzzle", 0));
        var binding = CreateBinding(targetBoneName: "Muzzle", followTarget: true, worldScale: 0.25f);
        var boneWorld = Matrix4x4.CreateTranslation(4, 5, 6);
        var readCount = 0;

        Matrix4x4 Provider(int index)
        {
            readCount++;
            Assert.Equal(1, index);
            return boneWorld;
        }

        var plan = NifParticleEffectAttachmentResolver.ResolveToBone(binding, bones, Provider);

        Assert.Equal(NifParticleAttachmentKind.FollowBone, plan.Kind);
        Assert.Equal(1, plan.BoneIndex);
        Assert.NotNull(plan.FollowMatrix);
        var follow = plan.FollowMatrix!;

        var first = follow();
        Assert.Equal(new Vector3(4, 5, 6), first.Translation);
        Assert.Equal(0.25f, first.M11);

        boneWorld = Matrix4x4.CreateTranslation(7, 8, 9);
        var second = follow();

        Assert.Equal(new Vector3(7, 8, 9), second.Translation);
        Assert.Equal(3, readCount);

        var third = follow();
        Assert.Equal(second, third);
    }

    [Fact]
    public void OneShotBoneSpawnCapturesTheMatrixAtResolveTime()
    {
        var bones = CreateBones((0, "Root", -1), (1, "Muzzle", 0));
        var binding = CreateBinding(targetBoneName: "Muzzle", followTarget: false, worldScale: 2.0f);
        var boneWorld = Matrix4x4.CreateTranslation(1, 2, 3);

        Matrix4x4 Provider(int index) => boneWorld;

        var plan = NifParticleEffectAttachmentResolver.ResolveToBone(binding, bones, Provider);

        Assert.Equal(NifParticleAttachmentKind.WorldSpawn, plan.Kind);
        Assert.Equal(1, plan.BoneIndex);
        Assert.Null(plan.FollowMatrix);

        boneWorld = Matrix4x4.CreateTranslation(9, 9, 9);

        Assert.Equal(new Vector3(1, 2, 3), plan.FixedWorldMatrix.Translation);
        Assert.Equal(2.0f, plan.FixedWorldMatrix.M11);
    }

    [Fact]
    public void FollowBindingCannotResolveToAWorldMatrix()
    {
        var binding = CreateBinding(targetBoneName: "Muzzle", followTarget: true);

        Assert.Throws<InvalidOperationException>(
            () => NifParticleEffectAttachmentResolver.ResolveToWorldSpawn(binding, Matrix4x4.Identity));
    }

    [Fact]
    public void ProofBindingsResolveToTheVerifiedDdfEntries()
    {
        var bindings = NifParticleEffectBinding.GetProofBindings();

        Assert.Equal(3, bindings.Count);

        var explosion = bindings[0];
        Assert.Equal("fx_explosion_1", explosion.EffectName);
        Assert.Equal("cache/nifcache/fx_explosion_1.nifcache", explosion.NifAssetKey);
        Assert.Null(explosion.TargetBoneName);
        Assert.False(explosion.FollowTarget);
        Assert.True(explosion.OneShot);
        Assert.Equal(0.005f, explosion.WorldScale);
        Assert.Equal("db/Visuals/visual_expl_ammo.ddf:ExplosionSequence", explosion.DdfSource);

        var muzzle = bindings[1];
        Assert.Equal("fx_muzzle_tank", muzzle.EffectName);
        Assert.Equal("cache/nifcache/fx_muzzle_tank.nifcache", muzzle.NifAssetKey);
        Assert.Equal("Muzzle", muzzle.TargetBoneName);
        Assert.True(muzzle.FollowTarget);
        Assert.False(muzzle.OneShot);
        Assert.Equal("db/Visuals/visual_fx_weapon.ddf:MuzzleSmokeEffect", muzzle.DdfSource);

        var splash = bindings[2];
        Assert.Equal("fx_watersplash_1", splash.EffectName);
        Assert.Equal("cache/nifcache/fx_watersplash_1.nifcache", splash.NifAssetKey);
        Assert.Equal(0.15f, splash.WorldScale);
        Assert.Equal("db/Units/bridge.ddf:BridgeModel_1.SplashNifNames", splash.DdfSource);

        foreach (var binding in bindings)
        {
            var attachment = binding.ToAttachment();
            Assert.Equal(binding.TargetBoneName, attachment.TargetBoneName);
            Assert.Equal(binding.FollowTarget, attachment.FollowTarget);
            Assert.Equal(binding.WorldScale, attachment.WorldScale);
            Assert.Equal(binding.OneShot, attachment.OneShot);
        }
    }

    [Fact]
    public void BindingValidationRejectsMalformedEntries()
    {
        Assert.Throws<ArgumentException>(
            () => new NifParticleEffectBinding("", "fx.nif", null, false, Vector3.Zero, 1, true, "s"));
        Assert.Throws<ArgumentException>(
            () => new NifParticleEffectBinding("fx", "fx.nifcache", null, false, Vector3.Zero, 1, true, "s"));
        Assert.Throws<ArgumentException>(
            () => new NifParticleEffectBinding("fx", "fx.nif", null, true, Vector3.Zero, 1, true, "s"));
        Assert.Throws<ArgumentException>(
            () => new NifParticleEffectBinding("fx", "fx.nif", null, false, Vector3.Zero, 0, true, "s"));
        Assert.Throws<ArgumentException>(
            () => new NifParticleEffectBinding("fx", "fx.nif", null, false, Vector3.Zero, 1, true, ""));
    }
}
