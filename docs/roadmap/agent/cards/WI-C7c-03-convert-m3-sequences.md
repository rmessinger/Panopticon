# Task Card `WI-C7c-03`: Convert embedded M3 sequences

| Field | Value |
|---|---|
| Card ID | `WI-C7c-03` |
| Roadmap phase | [Track C · C7c](../../track-c-external-assets.md): StarCraft II skinned M3 animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C7c-02` |
| Diagrams | [C7c architecture diagrams](../../deep-dives/c7c-sc2-skinned-animation.md#13-architecture-diagrams) |

## Goal

Add `M3AnimationImporter` for `<model>#seq=<name>` keys, producing complete absolute local channels from `SD3V` and `SD4Q` tracks and `BONE` defaults.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c7c-sc2-skinned-animation.md` sections 4, 5, 8, and 11.
- `src/Panopticon.Simulation/Content/Importing/Nif/NifAnimationKey.cs`, `NifAnimationImporter.cs`: key parsing and importer shape.
- `src/Panopticon.Simulation/Content/Importing/IAnimationImporter.cs`, `ImportedKeyframe.cs`, `ImportedAnimationTarget.cs`.

## Steps

1. Extend `Panopticon.FileFormats.M3` with `SEQS` v1, `STG_` v0, `STC_` v4 (ids, refs, `SD3V` and `SD4Q` arrays), and track blocks with `I32_` frames and `VEC3` or `QUAT` keys.
2. Add `M3AnimationKey.Parse`: `<model>#seq=<name>` and `<model>#m3a=<path>#seq=<name>`, with the name matched case-insensitively.
3. Add `M3SequenceConverter`: follow section 8 steps 1 to 5 for retained bones, conjugating values, keeping quaternion hemispheres, and emitting constant channels from `BONE` defaults. Composition is `AbsoluteLocal`. Count ignored tracks in diagnostics.
4. Add `M3AnimationImporter : IAnimationImporter` (`Id` `m3`) for embedded sequences. The `#m3a=` form throws `NotSupportedException` until `WI-C7c-04`.
5. Synthetic tests: a two-bone clip with one animated rotation plus a constant default, a multi-STC sequence rejected, non-linear interpolation rejected, and decreasing times rejected.
6. Installed tests: the nine embedded proof clips from section 2 (not the `.m3a`) import with the recorded durations and bind through C2 with no missing targets.

## Files likely to change

- `src/Panopticon.FileFormats.M3/M3Sequence.cs`, `M3SequenceClip.cs`, `M3Track.cs` (new)
- `src/Panopticon.Simulation/Content/Importing/M3/M3AnimationKey.cs`, `M3SequenceConverter.cs`, `M3AnimationImporter.cs` (new)
- `test/Panopticon.Simulation.Tests/Content/Importing/M3AnimationImporterTests.cs` (new), `Sc2M3ImportTests.cs`

## Acceptance criteria

- [ ] Every retained bone has translation, rotation, and scale channels in each proof clip.
- [ ] Durations match section 2, and C2 binding reports no missing targets.
- [ ] The section 11 sequence and track failures fire in synthetic tests.
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

- No `.m3a` loading. That is `WI-C7c-04`.
- No blending of `STG_` groups or `STS_` masks.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C7c-04` to `Ready`.
- Append a result line to `.exchange`.
