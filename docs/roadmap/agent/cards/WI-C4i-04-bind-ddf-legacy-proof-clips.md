# Task Card `WI-C4i-04`: Bind DDF legacy proof clips

> Audience: agent task card.

| Field | Value |
|---|---|
| Card ID | `WI-C4i-04` |
| Roadmap phase | [Track C · C4i](../../track-c-external-assets.md): EE2 legacy KF formats |
| Status | `Blocked` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-C4i-03`, `WI-C4c-05` |
| Diagrams | [C4i architecture diagrams](../../deep-dives/c4i-ee2-legacy-kf-formats.md#10-architecture-diagrams) |

## Goal

Add installed-asset tests proving the rigid 10.0.1.0 guntank and palisade clips import, bind, and
sample through C4c and C2.

## Context to read (and only this)

- `docs/roadmap/deep-dives/c4i-ee2-legacy-kf-formats.md` sections 2, 6, and 9.
- C4c installed clip tests added by `WI-C4c-05`.
- `src/OpenSage.Game.Tests/Content/Importing/Ee2NifImportTests.cs`.
- `src/OpenSage.Game/Graphics/ModelInstance.cs` animation binding changes from C2.

## Steps

1. Add installed tests for `LLM11_guntank.NIF#kf=Graphics/Vehicles/LLM11_guntank_idle.kf` and
   `LLM11_guntank.NIF#kf=Graphics/Vehicles/LLM11_guntank_walk.kf`.
2. Add installed tests for `bld_palisade1_gate.NIF#kf=Graphics/Building/bld_palisade1_gate_open.kf`
   and `bld_palisade1_gate.NIF#kf=Graphics/Building/bld_palisade1_gate_close.kf`.
3. Assert every controlled target binds to exactly one runtime bone.
4. Assert walk samples move at least one guntank wheel away from idle at a mid-clip time.
5. Assert palisade open and close samples move the two gate leaves in opposite directions between
   time 0 and the clip end.
6. Keep tests interactive or installed-asset gated, matching the C4c fixture pattern.

## Files likely to change

- `src/OpenSage.Game.Tests/Content/Importing/Ee2NifImportTests.cs`
- `src/OpenSage.Game.Tests/Content/Importing/NifAnimationImportTests.cs`

## Acceptance criteria

- [ ] All four proof clips import and bind without missing targets.
- [ ] Guntank wheel motion is non-identity in Walk and stable in Idle.
- [ ] Palisade gate Open and Close produce opposite translation offsets.
- [ ] Skinned legacy targets are not required for this card.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
EMPIRE_EARTH_II_PATH="/path/to/Empire Earth II Gold" \
  dotnet test src --filter "FullyQualifiedName~Ee2NifImportTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not add DDF state-machine loading.
- Do not require C4d skinning.
- Do not relax target-name binding if a proof target is missing.

## On completion

- Set Status above to `Done` or `Blocked`.
- If done and `WI-C4i-05` is done, change `WI-C4i-06` to `Ready`.
- Append a result line to `.exchange`.
