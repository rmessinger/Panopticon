# EE2 NIF proof - validation guide

Every capture comes from ZipFileSystem(graphics.zip + textures.zip) ->
NifModelImporter -> texcache decode -> ImportedModelMaterializer -> runtime
Model -> StandaloneModelCapture. The cooked DXT mips upload block-for-block
(DXT1/DXT3; no transcoding) and the models render textured. No EE2 asset is
copied or committed. Deterministic repeat: a second run must produce the
same frames.

- `ee2-nwg1_galley.png`: War Galley: recognizable complete hull with masts and sail, twelve oar pairs baked at their rotated node placements, upright orientation (mast up), bow toward +X after the source conversion, correct winding and normals (lit surfaces face the camera), nwg1_galley.tga texture mapped coherently (no upside-down or mirrored artwork)
- `ee2-ab11_TwinEngineBomber.png`: Twin-Engine Bomber: fuselage, wings, both engine props at 0.7416 baked scale, wheels, and open cockpits; highest-detail body_LOD0 geometry (no LOD1 simplification seams); upright and +X forward; DXT3 alpha blending on wheels/body keeps correct depth; ab11_TwinEngineBomber.tga and af11_biplane_prop.tga mapped coherently
