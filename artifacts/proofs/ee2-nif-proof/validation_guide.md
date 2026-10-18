# EE2 NIF proof - validation guide

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

- `ee2-nwg1_galley.png`: War Galley: recognizable complete hull with masts and sail, twelve oar pairs baked at their rotated node placements, upright orientation (mast up), bow toward +X after the source conversion, correct winding and normals (lit surfaces face the camera), nwg1_galley.tga texture mapped coherently (no upside-down or mirrored artwork)
- `ee2-nwg1_galley_walk_0000.png`: start of the rowing stroke: oars squared at their first key angles
- `ee2-nwg1_galley_walk_0767.png`: mid-stroke: oars swept to intermediate angles, visibly different from 0000
- `ee2-nwg1_galley_walk_1433.png`: late stroke: oars at their final key angles, different from both 0000 and 0767
- `ee2-nwg1_galley_idle_0000.png`: rest pose: identical to the static ee2-nwg1_galley.png capture (idle offsets are identity)
- `ee2-ab11_TwinEngineBomber.png`: Twin-Engine Bomber: fuselage, wings, both engine props at 0.7416 baked scale, wheels, and open cockpits; highest-detail body_LOD0 geometry (no LOD1 simplification seams); upright and +X forward; DXT3 alpha blending on wheels/body keeps correct depth; ab11_TwinEngineBomber.tga and af11_biplane_prop.tga mapped coherently
- `ee2-ab11_TwinEngineBomber_walk_0000.png`: ground pose with propellers at their first key, matching the Walk channel values at time 0
- `ee2-ab11_TwinEngineBomber_walk_0083.png`: both propellers turned a quarter turn (90 degrees) from 0000; airframe otherwise unchanged
- `ee2-ab11_TwinEngineBomber_takeoff_0000.png`: ground pose: identical to the static ee2-ab11_TwinEngineBomber.png capture (root NonAccum carries the ground pose at time 0)
- `ee2-ab11_TwinEngineBomber_takeoff_1333.png`: level raised flight pose: root keyed toward its flight rotation while root NonAccum carries the raised translation, clearly off the 0000 ground stance
- `ee2-CIV1_Druid_british.png`: Druid (skinned): upright biped standing on the ground plane, arms and cloak intact, no stretched or detached vertices at the bind pose; CIV1_Druid_british.tga mapped coherently; single body_LOD0 sub-object attached to the root bone
- `ee2-CIV1_Druid_british_walk01_0000.png`: walk stance at time 0: limbs at their first keys, feet on the ground, body upright, identical silhouette to the bind-pose capture
- `ee2-CIV1_Druid_british_walk01_0200.png`: mid-stride: arms and legs visibly swung to intermediate angles, mesh deforming smoothly at joints, no stretched or detached vertices
- `ee2-CIV1_Druid_british_walk01_0400.png`: late stride: limbs at their next keys, pose clearly different from both 0000 and 0200, joints still closed
- `ee2-CIV1_Druid_british_attack01_0933.png`: Attack1 swing: torso rotated with the striking arm extended, weapon arc readable, no vertex tearing at the shoulder
- `ee2-CIV1_Druid_british_death01_1233.png`: end of the death fall: the Druid collapsed toward the ground, limbs folded, mesh intact (no detached or exploded triangles)
