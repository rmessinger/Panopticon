# Task Card `WI-C7c-02`: Convert M3 skins

| Field | Value |
|---|---|
| Card ID | `WI-C7c-02` |
| Roadmap phase | [Track C · C7c](../../track-c-external-assets.md): StarCraft II skinned M3 animation |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C7c-01`, `WI-C7b-05` |
| Diagrams | [C7c architecture diagrams](../../deep-dives/c7c-sc2-skinned-animation.md#13-architecture-diagrams) |

## Goal

Make `M3ModelImporter` emit the skin-bone skeleton with `IREF` binds and four-influence skinned vertices in the engine frame.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c7c-sc2-skinned-animation.md` sections 3, 5, 6, and 11.
- `src/Panopticon.Simulation/Content/Importing/M3/M3ModelImporter.cs`, `M3GeometryConverter.cs`, `M3CoordinateConverter.cs`.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifSkinConverter.cs`: C4d skin validation pattern.

## Steps

1. Parse `IREF` as 16 little-endian floats read in order into `Matrix4x4` (`M11` first). In the System.Numerics row-vector convention this is the inverse bind directly, so `inverse(IREF)` is the bind world with translation in `M41..M43`. Also parse the `MODL` skin bone count and bone lookup in `Panopticon.FileFormats.M3`.
2. Add `M3SkinConverter`: keep bones `0 .. skinBoneCount - 1`. Compute bind locals from `inverse(IREF)` and decompose them into translation, rotation, and per-axis scale. Conjugate with `M3CoordinateConverter` and set `InverseBindMatrix = K^-1 * IREF * K`.
3. Map each vertex's nonzero lookups through the region range and `MODL` bone lookup to bone indices. Validate the weight sum within 0.01, at most four influences, and indices below the skin bone count. Mark meshes skinned.
4. Tests: a synthetic two-bone skinned model where bind skinning reproduces raw positions within 1e-5. Also test rejection of a lookup at the skin bone count and a bad weight sum.
5. Installed test: the section 3 influence histograms and skeleton sizes for the three proof units, and bind-pose skinning reproducing the C7b static positions within 1e-3.

## Files likely to change

- `src/Panopticon.FileFormats.M3/M3Model.cs` and an `M3InverseBind` reader
- `src/Panopticon.Simulation/Content/Importing/M3/M3SkinConverter.cs` (new), `M3ModelImporter.cs`, `M3GeometryConverter.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/M3SkinConverterTests.cs` (new), `Sc2M3ImportTests.cs`

## Acceptance criteria

- [ ] Skeleton sizes are 23, 25, and 31, and the influence histograms match section 3.
- [ ] Bind-pose skinning matches the static positions within 1e-3 for every proof vertex.
- [ ] The failure rows for weights, influences, and lookup range fire in synthetic tests.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~M3"
STARCRAFT_II_PATH="/path/to/StarCraft II" \
  dotnet test src --filter "FullyQualifiedName~Sc2M3"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- No animation import. That is `WI-C7c-03`.
- Do not truncate influences.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C7c-03` to `Ready`.
- Append a result line to `.exchange`.
