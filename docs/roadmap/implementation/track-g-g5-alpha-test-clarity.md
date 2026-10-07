# G5 Implementation: Alpha-Test Clarity

> Audience: agent. Card sequence and evidence for [G5](../deep-dives/g5-alpha-test-clarity.md).

## Scope

Cutout-only mip coverage and alpha-to-coverage, preserving legacy variants.
Source of truth: [design and diagrams](../deep-dives/g5-alpha-test-clarity.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| WI-G5-01 | Material/texture-role table and distance captures | none |
| WI-G5-02 | Coverage mips, cutout pipeline and comparisons | WI-G5-01, WI-G4-02 |

## Audit evidence to complete

Record cutout versus blended uses, shared images, compressed formats, authored mips and fragment
alpha source. Verify the minification defect's cause. Existing CPU mips use Lanczos3, not box filtering.

## Validation and risks

Synthetic coverage tests include tiny mips and fractional LOD. Bake alpha rescale once, without
an additional threshold correction. G6 controls RGB transfer; alpha stays linear. Compare
foliage/fences at effective 1x and 4x; particles/HUD are unchanged. Faithful retains DDS mips.
