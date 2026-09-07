# Task Card `WI-A5-12`: Add the domain visualization projection

| Field | Value |
|---|---|
| Card ID | `WI-A5-12` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): domain-parameterized authoring and replay |
| Status | `Blocked` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-A4-10`, `WI-A5-06` |
| Diagrams | [A5 §8](../../deep-dives/a5-visualization.md#8-architecture-diagrams) |

## Goal

Expose topology validation, stable bucket keys, visual primitives, metric descriptors, and optional
categorical overlays through A4-1 without spatial assumptions in ReplaySketch.

## Context to read (and only this)

- A5 projection contract in the implementation plan.
- A4 simulation-domain contracts and RTS/transport adapters.
- `src/OpenSage.Tools.ReplaySketch.Core/Profiles/MapPreviewRenderer.cs`.
- `src/OpenSage.Game/Recording/WorldStateSnapshot.cs` and `SignatureTree.cs`.

## Steps

1. Add the A5 consumer interface and domain visual primitive types from the design.
2. Implement RTS spatial projection.
3. Implement the minimal graph-domain projection.
4. Route topology/probe validation and stable bucket metadata through the same adapter.
5. Add spatial and non-spatial consumer tests.

## Files likely to change

- A4 domain interface/adapters
- New visualization types under `src/OpenSage.Tools.ReplaySketch.Core/`
- Focused tests

## Acceptance criteria

- [ ] ReplaySketch consumes no RTS concrete type through the interface.
- [ ] RTS and graph domains produce valid distinct primitive sets.
- [ ] Bucket keys align with T1 recording.
- [ ] Optional overlay absence is explicit.
- [ ] Standard validation passes.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~VisualizationProjection"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

No MAUI renderer, fidelity algorithm, or new domain.

## On completion

- Set Status above to `Done` or `Blocked`.
- Update `seams.md` derived-visualization note if symbols changed.
- Append a result line to `.exchange`.
