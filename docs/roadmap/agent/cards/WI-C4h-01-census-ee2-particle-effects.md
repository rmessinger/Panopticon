# Task Card `WI-C4h-01`: Census EE2 particle effects

| Field | Value |
|---|---|
| Card ID | `WI-C4h-01` |
| Roadmap phase | [Track C · C4h](../../track-c-external-assets.md): EE2 particle effects |
| Status | `Blocked` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | `WI-C4c-06` |
| Diagrams | [C4h architecture diagrams](../../deep-dives/c4h-ee2-particle-effects.md#11-architecture-diagrams) |

## Goal

Create a checked-in installed-asset descriptor test that locks the C4h proof effects, DDF sources,
and observed particle block inventory without committing EE2 assets.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4h-ee2-particle-effects.md` sections 1, 2, 3, and the reproduction
  appendix.
- `docs/roadmap/deep-dives/ee2-gamebryo-model-storage.md` sections 2, 3, and 5.
- `src/OpenSage.Game.Tests/Content/Importing/Ee2NifImportTests.cs`: installed EE2 test pattern.
- `src/OpenSage.FileFormats.Nif/NifFile.cs:21-55`: current NIF header and block loop.

## Steps

1. Add a small test helper that opens `EMPIRE_EARTH_II_PATH/zips/graphics.zip` and `db.zip` through
   the existing archive APIs or `ZipArchive`, without extracting files.
2. Reproduce the C4h header-only block census for `cache/nifcache/fx_*.nifcache`.
3. Assert the locked proof entries:
   - `fx_explosion_1`: 68 blocks, 22041 bytes;
   - `fx_muzzle_tank`: 88 blocks, 12815 bytes;
   - `fx_watersplash_1`: 89 blocks, 31478 bytes.
4. Assert that the measured corpus contains no `NiPSys*` block names and does contain the legacy
   particle block counts from the deep dive.
5. Assert that `db.zip` references each proof effect from the DDF file named in the deep dive.
6. If all assertions pass, promote `WI-C4h-02` to `Ready`.

## Files likely to change

- `src/OpenSage.Game.Tests/Content/Importing/Ee2ParticleEffectInventoryTests.cs` (new)

## Acceptance criteria

- [ ] The test names every proof DDF source and proof NIF path.
- [ ] The test fails if a proof block count, byte length, or DDF binding changes.
- [ ] The test confirms `NiPSys*` absence and legacy particle block presence.
- [ ] No EE2 asset bytes are copied into the repository.

## Validation commands

```sh
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2ParticleEffectInventoryTests"
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not parse particle block bodies in this card.
- Do not add runtime particle import or materialization.
- Do not update roadmap propagation files; the parent phase owner handles those files.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C4h-02` to `Ready`.
- Append a result line to `.exchange`.
