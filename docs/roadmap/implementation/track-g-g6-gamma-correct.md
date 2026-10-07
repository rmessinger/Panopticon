# G6 Implementation: Gamma-Correct Rendering

> Audience: agent. Card sequence and evidence for [G6](../deep-dives/g6-gamma-correct-rendering.md).

## Scope

Role-aware texture transfer, linear RGB filtering and one SDR output encoding.
Source of truth: [design and diagrams](../deep-dives/g6-gamma-correct-rendering.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| WI-G6-01 | Colour/data role and transfer-boundary inventory | none |
| WI-G6-02 | Linear texture/render variants and dithered output | WI-G6-01, WI-G2-03, WI-G3-03 |

## Audit evidence to complete

Record colour/data roles, compressed sRGB support, authored mips, lighting/fog constants,
terrain arrays, water auxiliary targets and UI composition. Select shader or hardware output
encoding explicitly and document precision/capability fallback.

## Validation and risks

Black/white colour mip is approximately sRGB 188; data mip is 128. Alpha is unchanged. Test
transfer endpoints and double encoding. Dither is bounded, zero-mean and screen-coordinate
stable. Faithful uses the unchanged legacy path. Coordinate texture cache policy with G5/G7.
