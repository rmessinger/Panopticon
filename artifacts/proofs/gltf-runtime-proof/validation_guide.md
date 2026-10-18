# glTF runtime proof - validation guide

Every capture comes from GltfModelImporter -> ImportedModelMaterializer ->
runtime Model -> StandaloneModelCapture; animated captures add C2 named
binding through ModelInstance.CreateAnimationInstance. Deterministic repeat:
a second run must produce the same frame set. All captures apply a uniform
magnification of 20x so ~1-meter fixtures clear the diagnostic
camera's near plane; the one-meter check therefore reads one meter =
20 * 3.28084 world units in the rendered frame.

- `gltf-pbr-materials-static.png`: Upright Z-up model, +X forward, one-meter triangle at 3.28084 world units, base-color and emissive textures visible, alpha mask and double-sided state from the material
- `gltf-rigid-scene-t0_0.png`: Rig rest pose: named 'arm' sub-object at its bind position; winding preserved (triangle visible from +Z camera)
- `gltf-rigid-scene-t1_0.png`: Named animation moved the attached unskinned 'arm' sub-object upward (+Y glTF = +Z world) and rotated it; named binding through C2
- `gltf-animated-skin-t0_0.png`: Two-influence skin deformation at a keyed time: inverse binds and non-identity rest pose position the vertices on jointB
- `gltf-animated-skin-t0_5.png`: Between STEP translation keys: joint holds the t=0 translation while LINEAR rotation is mid-interpolation
- `gltf-animated-skin-t1_0.png`: Two-influence skin deformation at a keyed time: inverse binds and non-identity rest pose position the vertices on jointB
