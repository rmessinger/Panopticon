# EE2 particle proof - validation guide

Every capture comes from ZipFileSystem(graphics.zip + textures.zip) ->
NifFile.Read -> NifParticleEffectConverter -> ImportedParticleEffectMaterializer ->
FXParticleSystemTemplate -> Scene3D.ParticleSystemManager -> RenderPipeline particle
bucket. DDF world scales (explosion 0.005, tank muzzle 0.09, splash 0.15) are baked into template
sizes, speeds, and emitter volumes because the runtime does not scale particle
quad sizes by the world matrix. Muzzle frames render lhm13_mainbattletank through
the C4 model pipeline with the smoke following its tag_muzzle_1 firing tag via the
follow-matrix overload; explosion and splash frames spawn at the world origin.
Tank muzzle source: heavy_mounted1.ddf HeavyMounted1_13.ChildNames references
Muzzle_FiringShell in visual_fx_weapon.ddf, using fx_muzzle_tank.nif at scale 0.09.
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
- `fx_explosion_1-particle-0100.png`: early expanding explosion for muzzle-size reference
- `fx_explosion_1-particle-0200.png`: early fireball before the late smoke frames
- `fx_explosion_1-particle-0500.png`: additive fireball ~180 px wide at the origin with a rim of ~10-20 px sparks; first system (0.5 s lifetime) already dead, second system at full intensity
- `fx_explosion_1-particle-1000.png`: dissipating smoke ring ~260 px at half the peak intensity; second system's particles (1 s lifetime, emission stop 0.225 s) in their final frames
- `fx_muzzle_tank-particle-0000.png`: main battle tank at the fixed three-quarter yaw; the first burst's puffs are sub-pixel because every muzzle system grows from size zero
- `fx_muzzle_tank-particle-0033.png`: second simulation tick: initial muzzle growth
- `fx_muzzle_tank-particle-0100.png`: early muzzle burst using the tank's Muzzle_FiringShell scale 0.09
- `fx_muzzle_tank-particle-0200.png`: early muzzle emission before the late smoke samples
- `fx_muzzle_tank-particle-0300.png`: transition from early emission to lingering muzzle smoke
- `fx_muzzle_tank-particle-0500.png`: alpha-blended muzzle puffs just beyond the barrel end, using the firing tag's model-local forward offset
- `fx_muzzle_tank-particle-1000.png`: translucent puffs beyond the barrel tip, without opaque square edges
- `fx_watersplash_1-particle-0000.png`: single small seed quad (~20 px) at the fixed splash origin: the first particle of the 15/s column system
- `fx_watersplash_1-particle-0100.png`: early splash seed and initial growth for reference
- `fx_watersplash_1-particle-0200.png`: early water spray before the late emission systems start
- `fx_watersplash_1-particle-0500.png`: centered spray column of alpha-blended, feathered quads; no opaque square around the spray
- `fx_watersplash_1-particle-1000.png`: column larger and brighter than 0500: the late systems (emission start 0.5 s, lifetimes up to 2.67 s) are now dominant and still growing
