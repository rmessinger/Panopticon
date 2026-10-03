#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon;
using Panopticon.Content.Importing;

namespace Panopticon.Content.Importing.Nif;

/// <summary>
/// DDF-sourced placement of one EE2 particle effect (WI-C4h-05): effect name, NIF file name,
/// optional tag or bone name, local offset, world scale, follow mode, and one-shot mode.
/// Bindings come from game-data tables (visual/weapon/bridge DDF entries verified against the
/// install); the NIF parser and importer stay free of DDF names.
/// </summary>
public sealed class NifParticleEffectBinding
{
    public string EffectName { get; }

    /// <summary>Effect model file name as written in the DDF entry, e.g. "fx_explosion_1.nif".</summary>
    public string NifFileName { get; }

    /// <summary>graphics.zip nifcache asset key for the effect model.</summary>
    public string NifAssetKey { get; }

    /// <summary>Target bone or tag name; null spawns the effect at a fixed world matrix.</summary>
    public string? TargetBoneName { get; }

    public bool FollowTarget { get; }

    public Vector3 LocalOffset { get; }

    /// <summary>Attachment-space offset in model units, applied before the bone world transform.</summary>
    public Vector3 BoneLocalOffset { get; }

    /// <summary>DDF world scale (SequenceWorldScales, SplashScales, EffectScale); 1 when absent.</summary>
    public float WorldScale { get; }

    public bool OneShot { get; }

    /// <summary>DDF source path and block for diagnostics, e.g. "db/Visuals/visual_expl_ammo.ddf:ExplosionSequence".</summary>
    public string DdfSource { get; }

    public NifParticleEffectBinding(
        string effectName,
        string nifFileName,
        string? targetBoneName,
        bool followTarget,
        Vector3 localOffset,
        float worldScale,
        bool oneShot,
        string ddfSource,
        Vector3 boneLocalOffset = default)
    {
        Ensure.NotNullOrEmpty(effectName, "Effect name must not be empty.");

        Ensure.NotNullOrEmpty(nifFileName, "NIF file name must not be empty.");

        if (!nifFileName.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"NIF file name '{nifFileName}' must end with .nif.",
                nameof(nifFileName));

        if (followTarget && string.IsNullOrEmpty(targetBoneName))
            throw new ArgumentException(
                "A following effect binding requires a target bone or tag name.",
                nameof(targetBoneName));

        if (!float.IsFinite(localOffset.X) || !float.IsFinite(localOffset.Y) || !float.IsFinite(localOffset.Z))
            throw new ArgumentException("Binding local offset must be finite.", nameof(localOffset));

        if (!float.IsFinite(worldScale) || worldScale <= 0.0f)
            throw new ArgumentException("Binding world scale must be finite and positive.", nameof(worldScale));

        Ensure.NotNullOrEmpty(ddfSource, "DDF source must not be empty.");

        EffectName = effectName;
        NifFileName = nifFileName;
        NifAssetKey = $"cache/nifcache/{nifFileName[..^".nif".Length]}.nifcache";
        TargetBoneName = targetBoneName;
        FollowTarget = followTarget;
        LocalOffset = localOffset;
        if (!float.IsFinite(boneLocalOffset.X) || !float.IsFinite(boneLocalOffset.Y) || !float.IsFinite(boneLocalOffset.Z))
            throw new ArgumentException("Bone-local offset must be finite.", nameof(boneLocalOffset));

        BoneLocalOffset = boneLocalOffset;
        WorldScale = worldScale;
        OneShot = oneShot;
        DdfSource = ddfSource;
    }

    public ImportedParticleAttachment ToAttachment()
    {
        return new ImportedParticleAttachment(
            TargetBoneName,
            FollowTarget,
            LocalOffset,
            WorldScale,
            OneShot,
            BoneLocalOffset);
    }

    /// <summary>
    /// Proof bindings from the verified install: explosion via ammo ExplosionSequence, muzzle
    /// smoke via the weapon-table MuzzleSmokeEffect, water splash via bridge SplashNifNames.
    /// The firing tag comes from the object's turret or unit DDF tag banks, which name
    /// tag_muzzle_N nodes; the proof model's firing tag is tag_muzzle_1. The model's bare
    /// muzzle node is the barrel geometry pivot (mid-barrel), not a firing tag.
    /// </summary>
    public static IReadOnlyList<NifParticleEffectBinding> GetProofBindings()
    {
        return new[]
        {
            new NifParticleEffectBinding(
                "fx_explosion_1",
                "fx_explosion_1.nif",
                targetBoneName: null,
                followTarget: false,
                localOffset: Vector3.Zero,
                worldScale: 0.005f,
                oneShot: true,
                ddfSource: "db/Visuals/visual_expl_ammo.ddf:ExplosionSequence"),
            new NifParticleEffectBinding(
                "fx_muzzle_tank",
                "fx_muzzle_tank.nif",
                targetBoneName: "tag_muzzle_1",
                followTarget: true,
                localOffset: Vector3.Zero,
                worldScale: 0.09f,
                oneShot: false,
                ddfSource: "db/Visuals/visual_fx_weapon.ddf:Muzzle_FiringShell.MuzzleSmokeEffect",
                // This proof model's firing tag is 2.7 model units inside the barrel end.
                boneLocalOffset: new Vector3(3f, 0f, 0f)),
            new NifParticleEffectBinding(
                "fx_watersplash_1",
                "fx_watersplash_1.nif",
                targetBoneName: null,
                followTarget: false,
                localOffset: Vector3.Zero,
                worldScale: 0.15f,
                oneShot: true,
                ddfSource: "db/Units/bridge.ddf:BridgeModel_1.SplashNifNames"),
        };
    }
}
