# Task Card `WI-C7b-01`: Read the M3 container and model tags

| Field | Value |
|---|---|
| Card ID | `WI-C7b-01` |
| Roadmap phase | [Track C · C7b](../../track-c-external-assets.md): StarCraft II static M3 models |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-C7a-04` |
| Diagrams | [C7a/C7b architecture diagrams](../../deep-dives/sc2-casc-m3-storage.md#14-architecture-diagrams) |

## Goal

Create `Panopticon.FileFormats.M3` with an MD34 reader and typed structures for the C7b tags in their exact proof versions.

## Context to read (and only this)

- `docs/roadmap/deep-dives/sc2-casc-m3-storage.md` sections 5, 7, 8.1, and 12.
- `src/Panopticon.FileFormats.Nif/`: project shape and binary reader style.

## Steps

1. Create `src/Panopticon.FileFormats.M3/Panopticon.FileFormats.M3.csproj`, add it to the solution, `Panopticon.Simulation`, and the test project.
2. Add `M3File.Read(Stream)`: check `43DM`, read the index (reversed tags), and expose `M3Reference` resolution to typed spans with tag and version checks.
3. Add `M3Model` (MODL v23 offsets from section 5.2), `M3Bone` (name, flags, parent, and the three animated component headers with defaults), `M3Region` (REGN v3), `M3Batch` (BAT_ v1), `M3Division` (faces, regions, batches), `M3MaterialReference`, `M3Material` (MAT_ v15 name, flags, blend mode, specularity, alpha test threshold, 13 layer references), and `M3Layer` (LAYR v22 path, flags, UV source, color channels).
4. Add `M3VertexFormat.FromFlags(uint)`: flags `0x0186007D` give stride 36 with UV1, `0x0182007D` stride 32 without. Others throw `NotSupportedException` with the value.
5. Unsupported tag versions throw `NotSupportedException` naming tag, version, and the stream name.
6. Add `M3TestDataBuilder` that writes a minimal MD34 file: one bone, one region with a triangle, one batch, one material with a diffuse layer. Test reading it and each rejection.

## Files likely to change

- `src/Panopticon.FileFormats.M3/*` (new)
- `src/Panopticon.sln`, `src/Panopticon.Simulation/Panopticon.Simulation.csproj`, `test/Panopticon.Simulation.Tests/Panopticon.Simulation.Tests.csproj`
- `test/Panopticon.Simulation.Tests/Data/M3/M3TestDataBuilder.cs`, `M3FileTests.cs` (new)

## Acceptance criteria

- [ ] The synthetic file round-trips every field listed in step 3.
- [ ] MODL v24, REGN v4, and vertex flags `0x0186007F` each fail naming the value.
- [ ] Unreferenced tags (`PAR_`, `RIB_`, `LITE`) are ignored.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
python3 scripts/check_brace_newline.py
```

## Out of scope / guardrails

- Do not parse `SEQS`, `STC_`, `STG_`, or tracks. That is C7c.
- No geometry conversion. That is `WI-C7b-02`.
- Do not commit StarCraft II files, extracted bytes, or captures.

## On completion

- Set Status above to `Done` or `Blocked` with reason.
- If done, change `WI-C7b-02` and `WI-C7b-03` to `Ready`.
- Append a result line to `.exchange`.
