#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing;
using Panopticon.Content.Importing.Nif;
using Panopticon.Content.Loaders;
using Panopticon.Data;
using Panopticon.FileFormats.Nif;
using Panopticon.Graphics;
using Panopticon.IO;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Tools.TestHarness;

/// <summary>
/// Headless C4 proof: mounts the operator-supplied EE2 graphics.zip through
/// ZipFileSystem, imports both proof targets through NifModelImporter,
/// materializes them through ImportedModelMaterializer (runtime path:
/// NifModelImporter -> ImportedModelMaterializer -> Model), and captures one
/// static PNG per target plus C4c animated frames at fixed clip times
/// (NifAnimationImporter -> ImportedAnimationMaterializer -> C2 named binding).
/// C4d adds the skinned Druid: body_LOD0 attaches to the root bone with
/// four-influence skinning, and the GPU skinning path (WI-C4d-04) drives the
/// walk/attack/death captures. Fixed-function diffuse renders with
/// texture-stage count zero; source texture names stay in the imported
/// descriptors. No EE2 asset is copied or committed.
/// C4e adds Ee2PropertyProof: the same pipeline with property channels
/// (visibility, texture transform, material color, alpha) sampled at fixed
/// times through CreateAnimationInstance.
/// C4f adds Ee2ScaleProof: uniform scale channels (WI-C4f-03) sampled at the
/// first and final takeoff keys, with the static baselines framed identically.
/// C4g adds Ee2MorphProof: farm yam imports through the morph KF pairing key
/// with the MorphTargets feature gate, and Target1 weight keys drive the CPU
/// morph buffer at weights 0, 0.5, and 1 (WI-C4g-03/04/05).
/// C4i adds Ee2LegacyKfProof: the DDF-bound rigid proof clips drive the same
/// pipeline from NetImmerse 10.0.1.0 KF entries (legacy header, root-list
/// footer, cooked flags 0x0C/0x05), with guntank wheel rotation and palisade
/// gate translation sampled at first and final keys.
/// </summary>
internal static class Ee2NifProofScenario
{
    private const int CaptureSize = 512;

    public const string ArchiveRelativePath = "zips/graphics.zip";
    public const string TexturesArchiveRelativePath = "zips/textures.zip";

    /// <summary>
    /// Presentation-only yaw: NIF forward maps to engine +X, so unrotated
    /// models present bow-first to the default arcball view; the yaw turns a
    /// three-quarter view without altering converted data.
    /// </summary>
    public const float CaptureYawRadians = -MathF.PI / 3f;

    public static readonly (string Key, string Check)[] CapturePlan =
    [
        ("nwg1_galley",
            "War Galley: recognizable complete hull with masts and sail, twelve oar pairs baked at " +
            "their rotated node placements, upright orientation (mast up), bow toward +X after the " +
            "source conversion, correct winding and normals (lit surfaces face the camera), " +
            "nwg1_galley.tga texture mapped coherently (no upside-down or mirrored artwork)"),
        ("ab11_TwinEngineBomber",
            "Twin-Engine Bomber: fuselage, wings, both engine props at 0.7416 baked scale, wheels, " +
            "and open cockpits; highest-detail body_LOD0 geometry (no LOD1 simplification seams); " +
            "upright and +X forward; DXT3 alpha blending on wheels/body keeps correct depth; " +
            "ab11_TwinEngineBomber.tga and af11_biplane_prop.tga mapped coherently"),
        ("CIV1_Druid_british",
            "Druid (skinned): upright biped standing on the ground plane, arms and cloak intact, " +
            "no stretched or detached vertices at the bind pose; CIV1_Druid_british.tga mapped " +
            "coherently; single body_LOD0 sub-object attached to the root bone"),
    ];

    /// <summary>
    /// C4c/C4d animated plan: deterministic clip times from the deep dives
    /// (c4c section 8, c4d section 11). File names use the KF stem (which is
    /// already <model>_<clip>) plus zero-padded milliseconds.
    /// </summary>
    public static readonly (string ModelKey, string KfPath, float TimeSeconds, string Check)[] AnimatedCapturePlan =
    [
        ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 0f,
            "start of the rowing stroke: oars squared at their first key angles"),
        ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 0.767f,
            "mid-stroke: oars swept to intermediate angles, visibly different from 0000"),
        ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_walk.kf", 1.433f,
            "late stroke: oars at their final key angles, different from both 0000 and 0767"),
        ("nwg1_galley", "Graphics/Vehicles/nwg1_galley_idle.kf", 0f,
            "rest pose: identical to the static ee2-nwg1_galley.png capture (idle offsets are identity)"),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf", 0f,
            "ground pose with propellers at their first key, matching the Walk channel values at time 0"),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_walk.kf", 1f / 12f,
            "both propellers turned a quarter turn (90 degrees) from 0000; airframe otherwise unchanged"),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf", 0f,
            "ground pose: identical to the static ee2-ab11_TwinEngineBomber.png capture (root NonAccum carries the ground pose at time 0)"),
        ("ab11_TwinEngineBomber", "Graphics/Vehicles/ab11_TwinEngineBomber_takeoff.kf", 1.333f,
            "level raised flight pose: root keyed toward its flight rotation while root NonAccum carries the raised translation, clearly off the 0000 ground stance"),
        ("CIV1_Druid_british", "Graphics/Humans/CIV1_Druid_british_walk01.kf", 0f,
            "walk stance at time 0: limbs at their first keys, feet on the ground, body upright, identical silhouette to the bind-pose capture"),
        ("CIV1_Druid_british", "Graphics/Humans/CIV1_Druid_british_walk01.kf", 0.2f,
            "mid-stride: arms and legs visibly swung to intermediate angles, mesh deforming smoothly at joints, no stretched or detached vertices"),
        ("CIV1_Druid_british", "Graphics/Humans/CIV1_Druid_british_walk01.kf", 0.4f,
            "late stride: limbs at their next keys, pose clearly different from both 0000 and 0200, joints still closed"),
        ("CIV1_Druid_british", "Graphics/Humans/CIV1_Druid_british_attack01.kf", 0.9333f,
            "Attack1 swing: torso rotated with the striking arm extended, weapon arc readable, no vertex tearing at the shoulder"),
        ("CIV1_Druid_british", "Graphics/Humans/CIV1_Druid_british_death01.kf", 1.2333f,
            "end of the death fall: the Druid collapsed toward the ground, limbs folded, mesh intact (no detached or exploded triangles)"),
    ];

    /// <summary>
    /// C4e property proof plan: one static baseline per family model plus
    /// animated frames at the fixed sample times asserted by
    /// Ee2NifPropertyAnimationTests. The market open/close clips cannot bind:
    /// their target node names exist in no cooked model, so the market entry
    /// covers the embedded static state only (the bind failure is asserted by
    /// the interactive tests).
    /// </summary>
    public static readonly (string Key, string Check)[] PropertyCapturePlan =
    [
        ("civ12_EIFV_egypt",
            "Egyptian EIFV: complete vehicle with treads; static baseline shows the tread texture at " +
            "its unanimated UV offset for comparison with the walk frames"),
        ("afb14_StealthFighterBomber",
            "Stealth Fighter Bomber: static baseline renders the model's own material colors; the " +
            "idle frames apply the animated ambient (1,1,1) and specular (0,0,0) overrides per slot"),
        ("faufish",
            "Resource fish: static baseline with no alpha animation applied (solid fish)"),
        ("LHA10_siegegun",
            "Siege gun: static baseline with the base sub-object (Editable Mesh_LOD0 under bone " +
            "'base') visible; the idle frame at 1 s hides it"),
        ("bld_market8",
            "Market 8: static model with Object04 visible; the open/close .kf clips do not bind " +
            "because their market_open_* node names exist in no cooked model"),
    ];

    public static readonly (string ModelKey, string KfPath, float TimeSeconds, string Check)[] PropertyAnimatedCapturePlan =
    [
        ("civ12_EIFV_egypt", "Graphics/Vehicles/civ12_EIFV_egypt_walk.kf", 0f,
            "tread UV offset at 0: texture aligned with the static baseline"),
        ("civ12_EIFV_egypt", "Graphics/Vehicles/civ12_EIFV_egypt_walk.kf", 1f,
            "tread UV offset at 1.5 after the 1 s walk clip: the tread strip shows a measurable pixel " +
            "delta against 0000; the fine-grained tread pattern wraps over 1.5 units, so the check is " +
            "the diff plus the sampled state (Ee2NifPropertyAnimationTests), not a large visual slide"),
        ("afb14_StealthFighterBomber", "Graphics/Vehicles/afb14_StealthFighterBomber_idle.kf", 0f,
            "idle clip at time 0: ambient color overridden to (1,1,1) and specular to (0,0,0) on " +
            "every material slot; airframe otherwise identical to the static baseline"),
        ("afb14_StealthFighterBomber", "Graphics/Vehicles/afb14_StealthFighterBomber_idle.kf", 0.0333f,
            "idle clip at its final key: same material color state as 0000 (both keys hold the " +
            "same values), rendering identically"),
        ("faufish", "Graphics/Animals/sh_faufish_idle01.kf", 0f,
            "alpha fade at 0 s: the eight animated Plane sub-objects sample alpha 0 and render " +
            "invisible; the packed school of un-animated fish covers nearly the same pixels, so the " +
            "visible delta vs the static baseline is small and appears only where the animated fish " +
            "protrude from the pack"),
        ("faufish", "Graphics/Animals/sh_faufish_idle01.kf", 1f,
            "alpha fade at 1 s: planes sample mid-fade alphas (0 to 1 per slot); faint translucent " +
            "plane detail appears over the body, differing from both 0000 and 2000"),
        ("faufish", "Graphics/Animals/sh_faufish_idle01.kf", 2f,
            "alpha fade at 2 s: most planes near-opaque (up to 1) with two fully transparent; clearly " +
            "more plane detail than 0000 and stronger than 1000"),
        ("LHA10_siegegun", "Graphics/Vehicles/LHA10_siegegun_idle01.kf", 1f,
            "visibility toggle at 1 s: the 'base' bone hides, removing the Editable Mesh_LOD0 " +
            "sub-object from the render; every other part unchanged from the static baseline"),
    ];

    internal static string GetAnimatedCaptureFileName(string kfPath, float timeSeconds)
    {
        var stem = Path.GetFileNameWithoutExtension(kfPath);
        var milliseconds = (int)MathF.Round(timeSeconds * 1000f);
        return $"ee2-{stem}_{milliseconds:0000}.png";
    }

    internal static string GetScaleCaptureFileName(string kfPath, float timeSeconds)
    {
        var stem = Path.GetFileNameWithoutExtension(kfPath);
        var milliseconds = (int)MathF.Round(timeSeconds * 1000f);
        return $"{stem}-scale-{milliseconds:0000}.png";
    }

    internal static string GetMorphCaptureFileName(string kfPath, float timeSeconds)
    {
        var stem = Path.GetFileNameWithoutExtension(kfPath);
        var milliseconds = (int)MathF.Round(timeSeconds * 1000f);
        return $"{stem}-morph-{milliseconds:0000}.png";
    }

    /// <summary>
    /// The single import key for the C4g morph proof: the paired morph KF
    /// request plus the MorphTargets gate carried by the import context.
    /// </summary>
    public const string FarmYamMorphModelKey = "bld_farm_yam#morph-kf=Graphics/Building/bld_farm_yam.kf";

    /// <summary>
    /// C4g morph proof plan: the farm yam base shape (all morph weights at
    /// their default 0) plus the seeding clip sampled at weights 0, 0.5, and
    /// 1 (linear keys 0 -> 1 over 0.9666667 s, asserted by
    /// Ee2MorphImportTests).
    /// </summary>
    public static readonly (string Key, string Check)[] MorphCapturePlan =
    [
        (FarmYamMorphModelKey,
            "Farm yam base shape: morph weights at their default 0 and no clip applied; complete " +
            "textured yam mound, no deformities; framed by its own arcball fit"),
    ];

    public static readonly (string ModelKey, string KfPath, float TimeSeconds, string Check)[] MorphAnimatedCapturePlan =
    [
        (FarmYamMorphModelKey, "Graphics/Building/bld_farm_yam.kf", 0f,
            "seeding clip at weight 0: Target1 deforms nothing; framing differs from the static " +
            "baseline because the clip's root bone keys pose the model before the arcball fit, so " +
            "the morph comparison runs within this animated sequence (0000 -> 0483 -> 0967 share " +
            "one camera)"),
        (FarmYamMorphModelKey, "Graphics/Building/bld_farm_yam.kf", 0.4833f,
            "seeding clip at weight 0.5: Target1 deltas applied at half weight, silhouette visibly " +
            "grown from 0000 and smaller than 0967, mesh still closed and textured; ambient/diffuse " +
            "color keys darken the material toward green (C4e channels riding the same clip)"),
        (FarmYamMorphModelKey, "Graphics/Building/bld_farm_yam.kf", 0.9667f,
            "seeding clip at weight 1: full Target1 deformation (largest delta 5.4214 source units " +
            "converted to engine feet), the largest silhouette of the three frames (mounds merged " +
            "into one canopy), no detached or inverted geometry"),
    ];

    /// <summary>
    /// C4f scale proof plan: one static baseline per aircraft plus the first
    /// and final takeoff keys. Takeoff scales run from uniform 1.0 down to
    /// the clip minimum (deep-dive table values are min to max, not time
    /// order). The scaled gear parts hang from bone origins inside the
    /// airframe, so the final-key frames show them pulled up out of sight;
    /// the numeric scale samples are asserted by
    /// Ee2KfScaleAnimationImportTests and the captures prove the channel
    /// drives the geometry with stable lighting.
    /// </summary>
    public static readonly (string Key, string Check)[] ScaleCapturePlan =
    [
        ("ab14_stealthbomber",
            "Stealth bomber: static baseline with both LandingGear_02/03 struts at their authored " +
            "rest scale (uniform 1.0); airframe, textures, and framing identical to the takeoff frames"),
        ("ab15_orbital",
            "Orbital: static baseline with both LandingGear_02/03 struts at uniform rest scale 1.0"),
        ("afb13_StrikeFighter",
            "Strike fighter: static baseline with the rearwheeltwo wheel at uniform rest scale 1.0"),
    ];

    public static readonly (string ModelKey, string KfPath, float TimeSeconds, string Check)[] ScaleAnimatedCapturePlan =
    [
        ("ab14_stealthbomber", "Graphics/Vehicles/ab14_stealthbomber_takeoff.kf", 0f,
            "takeoff first key: LandingGear_02 and LandingGear_03 sample uniform scale 1.0, matching " +
            "the static baseline"),
        ("ab14_stealthbomber", "Graphics/Vehicles/ab14_stealthbomber_takeoff.kf", 3.3333f,
            "takeoff final key: both landing-gear struts scaled to 0.883883 of rest; the struts hang " +
            "from bone origins inside the airframe, so the scaled gear pulls up out of sight against " +
            "the 0000 frame (the pixel delta localizes exactly at the gear blob; the numeric 1.0 -> " +
            "0.883883 samples are asserted by Ee2KfScaleAnimationImportTests); airframe and lighting " +
            "unchanged"),
        ("ab15_orbital", "Graphics/Vehicles/ab15_orbital_takeoff.kf", 0f,
            "takeoff first key: LandingGear_02 and LandingGear_03 sample uniform scale 1.0, matching " +
            "the static baseline (byte-identical to it)"),
        ("ab15_orbital", "Graphics/Vehicles/ab15_orbital_takeoff.kf", 0.6667f,
            "takeoff final key: both landing-gear struts scaled to 0.883883 of rest toward their bone " +
            "origins and retracted behind the wing (delta localizes at the gear nub); the numeric " +
            "samples are asserted by Ee2KfScaleAnimationImportTests; lighting stable"),
        ("afb13_StrikeFighter", "Graphics/Vehicles/afb13_StrikeFighter_takeoff.kf", 0f,
            "takeoff first key: the rearwheeltwo wheel samples uniform scale 1.0, matching the static " +
            "baseline (byte-identical to it)"),
        ("afb13_StrikeFighter", "Graphics/Vehicles/afb13_StrikeFighter_takeoff.kf", 3.3333f,
            "takeoff final key: the rearwheeltwo wheel scaled to 0.665078 of rest toward its bone " +
            "origin and fully retracted behind the airframe (delta localizes at the wheel; numeric " +
            "samples asserted by Ee2KfScaleAnimationImportTests); no lighting blowout or artifacts"),
    ];

    /// <summary>
    /// C4i legacy proof plan: two rigid DDF-bound targets driven by 10.0.1.0
    /// clips (NetImmerse File Format header, legacy controller flags 0x0C and
    /// 0x05, trimmed "end " text keys). Guntank wheels are rotation-only
    /// channels; the palisade leaves are translation-only channels.
    /// </summary>
    public static readonly (string Key, string Check)[] LegacyKfCapturePlan =
    [
        ("LLM11_guntank",
            "Gun tank (rigid): complete hull, turret, and six road wheels at their bind rotations; " +
            "upright with +X forward after the source conversion; textures mapped coherently"),
        ("bld_palisade1_gate",
            "Palisade gate (rigid): timber palisade with both gate leaves at their closed bind " +
            "positions; upright; textures mapped coherently"),
    ];

    public static readonly (string ModelKey, string KfPath, float TimeSeconds, string Check)[] LegacyKfAnimatedCapturePlan =
    [
        ("LLM11_guntank", "Graphics/Vehicles/LLM11_guntank_idle.kf", 0f,
            "idle at time 0: wheels at their first keys, matching the static baseline silhouette"),
        ("LLM11_guntank", "Graphics/Vehicles/LLM11_guntank_idle.kf", 0.7833f,
            "idle mid-clip: constant root channels hold the pose; wheels that carry idle keys stay " +
            "at their keyed angles, so the frame reads identical to 0000 (the idle clip holds the " +
            "wheels still while the tank stands)"),
        ("LLM11_guntank", "Graphics/Vehicles/LLM11_guntank_walk.kf", 0f,
            "walk at time 0: same hull pose as idle; wheels at their first walk keys"),
        ("LLM11_guntank", "Graphics/Vehicles/LLM11_guntank_walk.kf", 0.7833f,
            "walk mid-clip: the track silhouette shifts along the wheel line (178 px localized diff vs idle; " +
            "the skirts occlude the hubs, so wheel rotation is asserted numerically by Ee2LegacyKfImportTests, " +
            ">1 degree at mid-clip); hull otherwise held"),
        ("bld_palisade1_gate", "Graphics/Building/bld_palisade1_gate_open.kf", 0f,
            "open at time 0: both gate leaves at the closed bind pose, matching the static baseline"),
        ("bld_palisade1_gate", "Graphics/Building/bld_palisade1_gate_open.kf", 0.9667f,
            "open final key: the two gate leaves slid in opposite directions along the gate axis " +
            "(translation-only channels; deltas asserted by Ee2LegacyKfImportTests), leaving the " +
            "gateway open"),
        ("bld_palisade1_gate", "Graphics/Building/bld_palisade1_gate_close.kf", 0f,
            "close at time 0: leaves at the open pose, matching the open final-key frame"),
        ("bld_palisade1_gate", "Graphics/Building/bld_palisade1_gate_close.kf", 0.9667f,
            "close final key: both leaves returned to the closed bind pose, matching the static " +
            "baseline silhouette"),
    ];

    public static int Run(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        return RunCore(
            installation,
            config,
            reporter,
            "ee2-nif-proof",
            CapturePlan,
            AnimatedCapturePlan,
            guideTitle: null,
            guidePreamble: null);
    }

    public static int RunLegacyKfProof(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        return RunCore(
            installation,
            config,
            reporter,
            "ee2-legacy-kf-proof",
            LegacyKfCapturePlan,
            LegacyKfAnimatedCapturePlan,
            guideTitle: "# EE2 legacy KF proof - validation guide",
            guidePreamble:
            [
                "Rigid DDF-bound proof clips from the C4i legacy seam: the KF entries carry the",
                "NetImmerse File Format, Version 10.0.1.0 header (no user-version word, root-list",
                "footer), the readers are the C4c block types, and the import path accepts the cooked",
                "legacy controller flags 0x0C and 0x05 while trimming text-key values before the",
                "start/end comparison. Uppercase .KF lookup resolves through the case-insensitive",
                "zip file system. Skinned legacy targets and the NetImmerse 4.2.2.0 container are",
                "rejected with owning-phase diagnostics (asserted by Ee2LegacyKfImportTests). The",
                "walk/idle wheel rotation and gate-leaf translation samples are asserted by",
                "Ee2LegacyKfImportTests; these captures show the same bindings visually.",
            ]);
    }

    public static int RunPropertyProof(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        return RunCore(
            installation,
            config,
            reporter,
            "ee2-property-proof",
            PropertyCapturePlan,
            PropertyAnimatedCapturePlan,
            guideTitle: "# EE2 property animation proof - validation guide",
            guidePreamble:
            [
                "Every capture comes from the same import pipeline as ee2-nif-proof; animated frames",
                "add C4e property channels (visibility, texture transform, material color, material",
                "alpha) sampled at fixed clip times through CreateAnimationInstance. The market",
                "open/close clips do not bind: their market_open_* targets exist in no cooked model,",
                "which the interactive tests assert as the documented InvalidDataException.",
            ]);
    }

    public static int RunScaleProof(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        return RunCore(
            installation,
            config,
            reporter,
            "ee2-scale-proof",
            ScaleCapturePlan,
            ScaleAnimatedCapturePlan,
            guideTitle: "# EE2 scale animation proof - validation guide",
            guidePreamble:
            [
                "Every capture comes from the same import pipeline as ee2-nif-proof; animated frames",
                "add C4f uniform scale channels sampled at the first and final takeoff keys through",
                "CreateAnimationInstance. Takeoff clips scale landing gear and the rear wheel down",
                "from uniform 1.0 to the clip minimum toward their bone origins inside the airframe,",
                "so each final-key frame shows the named gear part pulled up out of sight against its",
                "0000 counterpart, with the pixel delta localized at that part. The numeric 1.0 ->",
                "minimum samples are asserted by Ee2KfScaleAnimationImportTests. The Royal Chariot",
                "rest-scale parity proof is not captured here: its skinned shape stays behind the",
                "C4d non-unit-rest skin guard, and Ee2KfScaleAnimationImportTests asserts the",
                "0.8499998 rest reconstruction on the skeleton path instead.",
            ],
            animatedFileName: GetScaleCaptureFileName);
    }

    public static int RunMorphProof(GameInstallation installation, TestHarnessConfig config, ITestHarnessReporter reporter)
    {
        return RunCore(
            installation,
            config,
            reporter,
            "ee2-morph-proof",
            MorphCapturePlan,
            MorphAnimatedCapturePlan,
            guideTitle: "# EE2 morph animation proof - validation guide",
            guidePreamble:
            [
                "The farm yam imports through the paired morph KF key",
                "bld_farm_yam#morph-kf=Graphics/Building/bld_farm_yam.kf with the MorphTargets feature",
                "gate enabled on the import context (the gate-disabled diagnostic is asserted by",
                "Ee2MorphImportTests). Animated frames sample the linear Target1 weight keys 0 -> 1",
                "over 0.9666667 s through CreateAnimationInstance; the CPU morph buffer rebuilds",
                "before the draw. The morph proof is the animated sequence itself (one shared",
                "camera, monotonic silhouette growth); the static baseline is framed differently",
                "because the clip's root bone keys pose the model before the arcball fit. The 0 /",
                "0.5 / 1 weight samples are asserted by",
                "Ee2MorphImportTests.FarmYamAnimationDrivesTarget1WeightsAtRuntime. The embedded",
                "morph smoke (quadratic weight curves on fx_emp_flash) is covered by the interactive",
                "tests; fx_muzzle_cannon stays behind the NiUVController parse guard.",
            ],
            animatedFileName: GetMorphCaptureFileName,
            staticFileName: _ => "bld_farm_yam-morph-static",
            animationModelKey: _ => "bld_farm_yam",
            enabledFeatureGates: [ImportedFeatureGates.MorphTargets]);
    }

    private static int RunCore(
        GameInstallation installation,
        TestHarnessConfig config,
        ITestHarnessReporter reporter,
        string outputDirectory,
        (string Key, string Check)[] capturePlan,
        (string ModelKey, string KfPath, float TimeSeconds, string Check)[] animatedPlan,
        string? guideTitle,
        string[]? guidePreamble,
        Func<string, float, string>? animatedFileName = null,
        Func<string, string>? staticFileName = null,
        Func<string, string>? animationModelKey = null,
        string[]? enabledFeatureGates = null)
    {
        var ee2Root = Environment.GetEnvironmentVariable("EMPIRE_EARTH_II_PATH");
        if (string.IsNullOrEmpty(ee2Root))
        {
            reporter.Error("Missing EMPIRE_EARTH_II_PATH pointing at an Empire Earth II install (source models).");
            return 1;
        }

        var archivePath = Path.Combine(ee2Root, ArchiveRelativePath);
        if (!File.Exists(archivePath))
        {
            reporter.Error($"Missing EE2 graphics archive at '{archivePath}'.");
            return 1;
        }

        var texturesArchivePath = Path.Combine(ee2Root, TexturesArchiveRelativePath);
        if (!File.Exists(texturesArchivePath))
        {
            reporter.Error($"Missing EE2 textures archive at '{texturesArchivePath}'.");
            return 1;
        }

        Directory.CreateDirectory(outputDirectory);

        using var game = new Game(
            installation,
            config.Renderer,
            new Configuration { NoAudio = true },
            null);

        using var ee2FileSystem = new ZipFileSystem(archivePath);
        using var ee2TexturesFileSystem = new ZipFileSystem(texturesArchivePath);
        var compositeFileSystem = new CompositeFileSystem(ee2FileSystem, ee2TexturesFileSystem);
        var importContext = new AssetImportContext(compositeFileSystem, language: null, enabledFeatureGates);
        var importer = new NifModelImporter();
        var animationImporter = new NifAnimationImporter();

        var gameLoadContext = game.AssetStore.LoadContext;
        var loadContext = new AssetLoadContext(
            new CompositeFileSystem(gameLoadContext.FileSystem),
            gameLoadContext.Language,
            gameLoadContext.GraphicsDevice,
            gameLoadContext.StandardGraphicsResources,
            gameLoadContext.ShaderResources,
            gameLoadContext.ShaderSetStore,
            gameLoadContext.AssetStore);

        var captures = new List<(string FileName, string Check)>();

        foreach (var (key, check) in capturePlan)
        {
            var imported = importer.ImportModel(key, importContext)
                ?? throw new InvalidOperationException($"EE2 import returned null for '{key}'.");

            // Decode and register the model's cooked textures under their
            // source .tga names so fixed-function materialization resolves them.
            var installed = Ee2TexcacheTextureInstaller.Install(
                game,
                compositeFileSystem,
                CollectTextureNames(imported));
            reporter.Info($"Installed {installed} texcache textures for '{key}'.");

            var model = ImportedModelMaterializer.MaterializeModel(imported, loadContext);

            // Import keys for gated models (for example the morph KF pairing
            // key) contain path separators; callers supply a file-safe name.
            var outputName = staticFileName != null ? staticFileName(key) : $"ee2-{key}";
            var outputPath = Path.Combine(outputDirectory, $"{outputName}.png");

            // First execution of each material pipeline variant after device
            // creation is unstable (see GltfRuntimeProof); warm every model once.
            StandaloneModelCapture.Capture(
                game,
                model,
                loadContext,
                Path.Combine(Path.GetTempPath(), $"ee2-nif-proof-warmup-{outputName}.png"),
                new Size(CaptureSize, CaptureSize),
                worldMatrix: System.Numerics.Matrix4x4.CreateRotationZ(CaptureYawRadians),
                options: new StandaloneCaptureOptions(
                    Background: new Mathematics.ColorRgbaF(0.2f, 0.4f, 0.9f, 1f)));

            StandaloneModelCapture.Capture(
                game,
                model,
                loadContext,
                outputPath,
                new Size(CaptureSize, CaptureSize),
                worldMatrix: System.Numerics.Matrix4x4.CreateRotationZ(CaptureYawRadians),
                options: new StandaloneCaptureOptions(
                    Background: new Mathematics.ColorRgbaF(0.2f, 0.4f, 0.9f, 1f)));

            captures.Add(($"{outputName}.png", check));
            reporter.Info($"Captured {outputName}.png");

            foreach (var (_, kfPath, timeSeconds, animatedCheck) in animatedPlan.Where(e => e.ModelKey == key))
            {
                // Gated import keys are not plain model names; the animation
                // request always targets the bare model key.
                var animationModel = animationModelKey != null ? animationModelKey(key) : key;
                var importedAnimation = animationImporter.ImportAnimation($"{animationModel}#kf={kfPath}", importContext)
                    ?? throw new InvalidOperationException($"EE2 animation import returned null for '{animationModel}#kf={kfPath}'.");
                var animation = ImportedAnimationMaterializer.Materialize(importedAnimation);

                var nameForTime = animatedFileName ?? GetAnimatedCaptureFileName;
                var animatedName = nameForTime(kfPath, timeSeconds);

                // Property animation can select pipeline variants the static
                // warm-up never exercises (for example the blend pass forced
                // by a sampled alpha), so warm each pose once too.
                StandaloneModelCapture.Capture(
                    game,
                    model,
                    loadContext,
                    Path.Combine(Path.GetTempPath(), $"ee2-nif-proof-warmup-{animatedName}"),
                    new Size(CaptureSize, CaptureSize),
                    configureInstance: instance => PlayToTime(instance, animation, timeSeconds),
                    worldMatrix: System.Numerics.Matrix4x4.CreateRotationZ(CaptureYawRadians),
                    options: new StandaloneCaptureOptions(
                        Background: new Mathematics.ColorRgbaF(0.2f, 0.4f, 0.9f, 1f)));

                StandaloneModelCapture.Capture(
                    game,
                    model,
                    loadContext,
                    Path.Combine(outputDirectory, animatedName),
                    new Size(CaptureSize, CaptureSize),
                    configureInstance: instance => PlayToTime(instance, animation, timeSeconds),
                    worldMatrix: System.Numerics.Matrix4x4.CreateRotationZ(CaptureYawRadians),
                    options: new StandaloneCaptureOptions(
                        Background: new Mathematics.ColorRgbaF(0.2f, 0.4f, 0.9f, 1f)));

                captures.Add((animatedName, animatedCheck));
                reporter.Info($"Captured {animatedName}");
            }
        }

        WriteValidationGuide(outputDirectory, captures, guideTitle, guidePreamble);
        reporter.Info($"Validation guide: {Path.GetFullPath(Path.Combine(outputDirectory, "validation_guide.md"))}");

        return 0;
    }

    public static IEnumerable<string> CollectTextureNames(ImportedModel model)
    {
        foreach (var subObject in model.SubObjects)
        {
            if (subObject.RenderObject is not ImportedMesh mesh)
            {
                continue;
            }

            foreach (var material in mesh.Materials)
            {
                foreach (var property in material.Properties)
                {
                    if (property.Name == "Texture0" &&
                        property.Kind == ImportedMaterialPropertyKind.TextureName)
                    {
                        yield return property.TextureName;
                    }
                }
            }
        }
    }

    public static string DescribePrerequisites()
    {
        return $"EMPIRE_EARTH_II_PATH (source models; needs {ArchiveRelativePath}) and the rendering " +
            "host installation resolved by the harness";
    }

    private static void PlayToTime(ModelInstance instance, Panopticon.Graphics.Animation.ModelAnimation animation, float seconds)
    {
        var animationInstance = instance.CreateAnimationInstance(
            animation,
            AnimationMode.Once,
            AnimationFlags.StartFrameFirst,
            gameObject: null!,
            random: new ConstantRandom());

        animationInstance.Play();

        const int steps = 8;
        var delta = TimeSpan.FromSeconds(seconds / steps);
        for (var i = 0; i < steps; i++)
        {
            animationInstance.Update(new TimeInterval(TimeSpan.Zero, delta));
        }
    }

    private sealed class ConstantRandom : IRandom
    {
        public uint Seed => 0;

        public void Initialize(uint seed)
        {
        }

        public int Next(int lo, int hi) => lo;

        public float NextSingle(float lo, float hi) => lo;
    }

    internal static void WriteValidationGuide(
        string outputDirectory,
        List<(string FileName, string Check)> captures,
        string? title = null,
        string[]? preamble = null)
    {
        using var writer = new StreamWriter(Path.Combine(outputDirectory, "validation_guide.md"));
        writer.WriteLine(title ?? "# EE2 NIF proof - validation guide");
        writer.WriteLine();
        if (preamble != null)
        {
            foreach (var line in preamble)
            {
                writer.WriteLine(line);
            }

            writer.WriteLine();
        }

        writer.WriteLine("Every capture comes from ZipFileSystem(graphics.zip + textures.zip) ->");
        writer.WriteLine("NifModelImporter -> texcache decode -> ImportedModelMaterializer -> runtime");
        writer.WriteLine("Model -> StandaloneModelCapture. The cooked DXT mips upload block-for-block");
        writer.WriteLine("(DXT1/DXT3; no transcoding) and the models render textured. Animated frames add");
        writer.WriteLine("NifAnimationImporter -> ImportedAnimationMaterializer -> C2 named binding driven");
        writer.WriteLine("to a fixed clip time before the capture. No EE2 asset is copied or committed.");
        writer.WriteLine("Deterministic repeat: a second run must produce the same frames. Mesh world");
        writer.WriteLine("constants are uploaded synchronously before each capture's passes because");
        writer.WriteLine("hardware Vulkan drivers can otherwise apply the per-item inline updates after");
        writer.WriteLine("the draws that read them (stale world matrices on some meshes).");
        writer.WriteLine();
        foreach (var (fileName, check) in captures)
        {
            writer.WriteLine($"- `{fileName}`: {check}");
        }
    }
}
