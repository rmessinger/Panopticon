# EE2 particle proof - validation guide

Every capture comes from ZipFileSystem(graphics.zip + textures.zip) ->
NifFile.Read -> NifParticleEffectConverter -> ImportedParticleEffectMaterializer ->
FXParticleSystemTemplate -> Scene3D.ParticleSystemManager -> RenderPipeline particle
bucket. DDF world scales (explosion 0.005, splash 0.15) are baked into template
sizes, speeds, and emitter volumes because the runtime does not scale particle
quad sizes by the world matrix. Muzzle frames render lhm13_mainbattletank through
the C4 model pipeline with the smoke following its Muzzle tag via the
follow-matrix overload; explosion and splash frames spawn at the world origin.
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

- `fx_explosion_1-particle-0000.png`: first burst just emitted: two tiny bright additive spark clusters at the world origin (grow-from-zero keeps the first tick's quads small)
- `fx_explosion_1-particle-0500.png`: additive fireball ~180 px wide at the origin with a rim of ~10-20 px sparks; first system (0.5 s lifetime) already dead, second system at full intensity
- `fx_explosion_1-particle-1000.png`: dissipating smoke ring ~260 px at half the peak intensity; second system's particles (1 s lifetime, emission stop 0.225 s) in their final frames
- `fx_muzzle_tank-particle-0000.png`: main battle tank at the fixed three-quarter yaw; the first burst's puffs are sub-pixel because every muzzle system grows from size zero
- `fx_muzzle_tank-particle-0500.png`: small smoke puffs (tens of px) at the resolved Muzzle tag beside the barrel; the tank itself unchanged
- `fx_muzzle_tank-particle-1000.png`: puffs grown larger (grow windows 0.33-1.25 s at EffectScale 0.05) and spread along the muzzle axis; still additive and anchored at the barrel
- `fx_watersplash_1-particle-0000.png`: single small seed quad (~20 px) at the fixed splash origin: the first particle of the 15/s column system
- `fx_watersplash_1-particle-0500.png`: centered spray column of overlapping additive quads (mature sizes ~85 px at this camera); clearly denser and wider than 0000
- `fx_watersplash_1-particle-1000.png`: column larger and brighter than 0500: the late systems (emission start 0.5 s, lifetimes up to 2.67 s) are now dominant and still growing
