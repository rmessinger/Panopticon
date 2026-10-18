# PBR Material Proof — Validation Guide

Every capture renders one synthetic quad through the core PBR shader family. Expected checks:

| Capture | Expected check |
|---|---|
| pbr-dielectric | Matte red-brown diffuse response under default lighting; no specular hotspot. |
| pbr-metal-smooth | Bright specular hotspot; near-black diffuse (metal kills diffuse albedo). |
| pbr-dielectric-matte | Same base color as metal-smooth but broad, flat diffuse and no hotspot. |
| pbr-metallic-roughness-texture | Roughness from the checker G channel modulates the hotspot. |
| pbr-normal-imported-tangents | Flat normal map: shading matches an unmapped quad (identity normal). |
| pbr-normal-generated-tangents | Identical to normal-imported-tangents (generated basis equals flat map). |
| pbr-occlusion-emissive | Red emissive added on top of occlusion-modulated ambient. |
| pbr-alpha-mask | Fully opaque texels (alpha 1 > cutoff 0.5): complete quad. |
| pbr-alpha-blend | Green quad at half opacity; background shows through. |
| pbr-double-sided | Front-facing quad renders; back-face culling disabled (compare with dielectric). |
| pbr-uv1-selection | Same flat red as an embedded base color, sampled from UV set 1. |
| pbr-sampler-repeat | 4x4 checker tiles repeated across the quad (UVs span 0..2). |
| pbr-sampler-clamp | Checker stretched: edge pixels smeared instead of tiled. |
| pbr-filter-nearest | Hard checker edges under magnification. |
| pbr-filter-linear | Blended checker edges under magnification (compare with nearest). |
| pbr-skinned | Blue diffuse quad, identical family and lighting to dielectric. |
| pbr-colorspace-emissive | Known emissive sRGB round trip; asserted programmatically within one 8-bit step. |

Determinism: the dielectric capture is repeated and byte-compared automatically.