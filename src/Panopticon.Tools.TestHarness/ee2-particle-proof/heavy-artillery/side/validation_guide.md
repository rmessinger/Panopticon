# EE2 particle proof - validation guide

Every capture comes from ZipFileSystem(graphics.zip + textures.zip) ->
NifFile.Read -> NifParticleEffectConverter -> ImportedParticleEffectMaterializer ->
FXParticleSystemTemplate -> Scene3D.ParticleSystemManager -> RenderPipeline particle
bucket. DDF effect scales are baked into template
sizes, speeds, and emitter volumes because the runtime does not scale particle
quad sizes by the world matrix. Muzzle frames render lha12_selfpropelled through
the C4 model pipeline with the smoke following its tag_muzzle_1 firing tag via the
follow-matrix overload; explosion and splash frames spawn at the world origin.
Unit source: heavy_artillery.ddf:HeavyArtillery_12, ForceScale 0.09.
Effect source: db/Visuals/visual_fx_weapon.ddf:Muzzle_HeavyArtillery.MuzzleSmokeEffect, fx_muzzle_heavyartillery_1.nif, EffectScale 0.08.
Attachment: tag_muzzle_1, bone-local offset <0, 0, 0>.
EffectScale and ForceScale are applied independently; follow transforms retain
the scaled tag position without rescaling materialized particle sizes or motion.
Frame 0000 is the first emitted burst (one 30 Hz tick), not an empty spawn; each
frame replays from t=0 under ParticleSystemUtility.BeginSeedScope seeded from the
effect key, so a second run must produce identical frames. No EE2 asset is copied
or committed.

Every capture comes from ZipFileSystem(graphics.zip + textures.zip) ->
NifModelImporter -> texcache decode -> ImportedModelMaterializer -> runtime
Model -> StandaloneModelCapture. The cooked DXT mips upload block-for-block
(DXT1/DXT3; no transcoding) and the models render textured. Animated frames add
NifAnimationImporter -> ImportedAnimationMaterializer -> C2 named binding driven
to a fixed clip time before the capture. No EE2 asset is copied or committed.
Deterministic repeat: a second run must produce the same frames. Mesh world
constants are uploaded synchronously before each capture's passes because
hardware Vulkan drivers can otherwise apply the per-item inline updates after
the draws that read them (stale world matrices on some meshes).

- `fx_muzzle_heavyartillery_1-particle-0000.png`: lha12_selfpropelled: firing-tag placement, alpha edges and relative particle scale at 0.000 s
- `fx_muzzle_heavyartillery_1-particle-0033.png`: lha12_selfpropelled: firing-tag placement, alpha edges and relative particle scale at 0.033 s
- `fx_muzzle_heavyartillery_1-particle-0100.png`: lha12_selfpropelled: firing-tag placement, alpha edges and relative particle scale at 0.100 s
- `fx_muzzle_heavyartillery_1-particle-0200.png`: lha12_selfpropelled: firing-tag placement, alpha edges and relative particle scale at 0.200 s
- `fx_muzzle_heavyartillery_1-particle-0300.png`: lha12_selfpropelled: firing-tag placement, alpha edges and relative particle scale at 0.300 s
- `fx_muzzle_heavyartillery_1-particle-0500.png`: lha12_selfpropelled: firing-tag placement, alpha edges and relative particle scale at 0.500 s
- `fx_muzzle_heavyartillery_1-particle-1000.png`: lha12_selfpropelled: firing-tag placement, alpha edges and relative particle scale at 1.000 s
