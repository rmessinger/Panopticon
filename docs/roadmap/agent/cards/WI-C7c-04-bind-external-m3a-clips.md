# Task Card `WI-C7c-04`: Bind external .m3a clips

| Field | Value |
|---|---|
| Card ID | `WI-C7c-04` |
| Roadmap phase | [Track C · C7c](../../track-c-external-assets.md): StarCraft II skinned M3 animation |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C7c-03` |
| Diagrams | [C7c architecture diagrams](../../deep-dives/c7c-sc2-skinned-animation.md#13-architecture-diagrams) |

## Goal

Support `<model>#m3a=<path>#seq=<name>` keys by reading `.m3a` animation tags and binding tracks to the target model by animation id with name checks.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c7c-sc2-skinned-animation.md` sections 9 and 11.
- `src/Panopticon.Simulation/Content/Importing/M3/M3AnimationImporter.cs`, `M3SequenceConverter.cs`.
- `src/Panopticon.FileFormats.M3/M3File.cs`: version checks to relax for `.m3a`.

## Steps

1. Add an `M3File` read mode for animation files: accept `MODL` v25 and parse only `BONE`, `SEQS`, `STG_`, `STC_`, and track tags. Geometry and material tags stay opaque.
2. Build a map from animation id to the `.m3a` bone name, and resolve each id against the target model's bone headers. A missing id or a name mismatch throws `InvalidDataException` naming the id and both bones.
3. Convert with `M3SequenceConverter`, using the target model's retained bones and `BONE` defaults.
4. Synthetic tests: a matching `.m3a`, a missing id, and a renamed bone.
5. Installed test: `zealot.m3#m3a=.../zealotoptionalanims.m3a#seq=Stand Morph Start` imports with duration 1,000 ms and binds with no missing targets.

## Files likely to change

- `src/Panopticon.FileFormats.M3/M3File.cs`
- `src/Panopticon.Simulation/Content/Importing/M3/M3AnimationImporter.cs`, `M3SequenceConverter.cs`
- `test/Panopticon.Simulation.Tests/Content/Importing/M3AnimationImporterTests.cs`, `Sc2M3ImportTests.cs`

## Acceptance criteria

- [ ] The proof `.m3a` clip imports and binds.
- [ ] Missing ids and name mismatches fail naming both sides.
- [ ] `.m3a` geometry and material tag versions do not cause errors.
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

- No binding by bone name alone. The id rule is the decision.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C7c-05` to `Ready`.
- Append a result line to `.exchange`.
