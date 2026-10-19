# Task Card `WI-A5-27`: Fix WorldBuilder edit application ownership

| Field | Value |
|---|---|
| Card ID | `WI-A5-27` |
| Roadmap phase | [Track A · A5](../../track-a-foundations.md): A5-6 WorldBuilder baseline |
| Status | `Done` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | `WI-A5-01` must confirm the audit finding |
| Diagrams | Not applicable: bounded WorldBuilder baseline repair. |

## Goal

Ensure each WorldBuilder edit is applied exactly once before its first undo, including object
placement.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a5-6-worldbuilder-baseline.md`.
- `src/Panopticon.Tools.WorldBuilder.Core/UndoRedoStack.cs`.
- `ObjectPlacementEdit.cs`, `HeightMapEdit.cs`, and `BlendTileEdit.cs`.
- Tool callers that construct and push those edits.

## Steps

1. Establish one application owner consistent with the existing undo/redo contract.
2. Remove any caller-side duplicate application.
3. Add tests for placement, height, and blend edits.
4. Assert push, undo, redo, and repeated cycles.

## Files likely to change

- `src/Panopticon.Tools.WorldBuilder.Core/UndoRedoStack.cs`
- Affected edit/tool files identified by the audit
- New WorldBuilder Core tests

## Acceptance criteria

- [x] One placement creates exactly one object.
  - Root cause: `CreatePlacement` applied the edit internally, then `UndoRedoStack.Push`
    applied it again; `ObjectsList.AddObject` appends, so every placement added the object
    twice. Factories no longer self-apply; `Push` is the single application owner.
- [x] Undo removes it and redo restores exactly one.
  - `UndoRedoEditApplicationTests.PlacementPushAddsExactlyOneObject_AndUndoRedoCycles`
    (three cycles, `Assert.Single` + `Assert.Same`).
- [x] Height and blend edits preserve their prior behavior.
  - `BlendTileEdit.CreateSingle` also no longer self-applies (its double application was
    idempotent); `HeightMapEdit.Capture` applies via callback and `Push` re-copies `_after`,
    unchanged. Covered by the blend and height cycle tests.
- [x] Repeated undo/redo does not accumulate duplicates.
  - Cycled three times in the placement, deletion, blend, and height tests.
- [x] Standard validation passes.
  - New test project `Panopticon.Tools.WorldBuilder.Core.Tests` (4/4), wired into
    `src/Panopticon.sln` and the CI workflow; build clean, format + brace clean.

## Validation commands

```sh
dotnet build src
dotnet test src --filter "FullyQualifiedName~WorldBuilder|FullyQualifiedName~UndoRedo"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

Fix only audit-confirmed application ownership; no UI or scaling changes.

## On completion

- Set Status above to `Done` or `Blocked`.
- Append a result line to `.exchange`.
