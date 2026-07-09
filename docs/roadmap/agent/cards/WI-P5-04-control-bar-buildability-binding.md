# Task Card: `WI-P5-04` — Control-bar buildability binding

| Field | Value |
|---|---|
| Card ID | `WI-P5-04` |
| Roadmap phase | [Track P · P5](../../track-p-playable-baseline.md) — sub-phase P5-2, see [deep dive §4](../../deep-dives/p2-p5-skirmish-ai.md#3-sub-phases), [§1.4](../../deep-dives/p2-p5-skirmish-ai.md#14-p5-construction-and-production-completeness), and [Appendix](../../deep-dives/p2-p5-skirmish-ai.md#how-actions-are-actuated-the-p5-mechanism) |
| Status | `Ready` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | P1 and P4 complete |
| Diagrams | [P5 Architecture Diagrams](../../deep-dives/p2-p5-skirmish-ai.md#7-architecture-diagrams) |

## Goal

Locate and verify the Generals control-bar consumer for buildability state so command buttons reflect `GameObject.CanPurchase(...)`, `CanConstructUnit(...)`, and `CanEnqueueUpgrade(...)` outcomes for prerequisites and money.

## Context to read (and only this)

- `docs/roadmap/implementation-checklist.md:267-270` — checklist item and exact AC to satisfy.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:68-69` — audit gap for UI buildability binding.
- `docs/roadmap/deep-dives/p2-p5-skirmish-ai.md:268-271` — buildability methods to verify.
- `src/OpenSage.Game/Logic/Object/GameObject.cs:1164-1222` — `CanPurchase`, `CanConstructUnit`, and `CanEnqueueUpgrade`.
- `src/OpenSage.Mods.Generals/Gui/GeneralsControlBar.cs:407-505` — current Generals command-button enablement candidate.
- `src/OpenSage.Mods.Generals/Gui/CommandButtonUtils.cs:15-70` — button wiring and callbacks.
- `src/OpenSage.Game.Tests/MockedGameTest.cs:24-34` — headless test base if a UI seam can be tested without game files.

## Steps

1. Investigate with targeted searches only: confirm all Generals consumers of `CanPurchase`, `CanConstructUnit`, and `CanEnqueueUpgrade`. The current candidate is `GeneralsControlBar.ApplyCommandSet(...)`; note if Generals bypasses `CanPurchase(...)` and calls submethods directly.
2. Document the located consumer in code comments only if needed for clarity; otherwise the test name and assertions are the artifact. Do not create a separate documentation file.
3. Add `src/OpenSage.Game.Tests/Gui/GeneralsControlBarBuildabilityTests.cs` as a headless `[Fact]` test file if the control-bar state can be exercised with `MockedGameTest`; ensure the new `.cs` file is UTF-8 with BOM.
4. Verification path first: drive the existing consumer with a selected producer and command buttons for dozer construct, unit build, and upgrade. Assert buttons are disabled when prerequisites or money are missing and enabled when both are satisfied.
5. Fallback implement-if-missing: if no Generals binding exists for one command family, wire the smallest missing branch in `GeneralsControlBar.ApplyCommandSet(...)`, preserving existing special cases for dozers already building, container buttons, purchased/enqueued upgrade overlays, and script-only buttons.
6. If `GameObject.CanPurchase(...)` remains unused by Generals but equivalent submethod calls cover the same states, record that as the verified consumer instead of forcing a wrapper-only refactor.
7. Run `dotnet format src`, then confirm `dotnet format src --verify-no-changes` is clean.

## Files likely to change

- `src/OpenSage.Mods.Generals/Gui/GeneralsControlBar.cs`
- `src/OpenSage.Mods.Generals/Gui/CommandButtonUtils.cs`
- `src/OpenSage.Game.Tests/Gui/GeneralsControlBarBuildabilityTests.cs` (new)

## Acceptance criteria

- [ ] The UI consumer of those methods is identified and documented; buildable/unbuildable state in the control bar reflects prereqs + money.
- [ ] Dozer construct, unit build, and player/object upgrade buttons are each covered by either `CanPurchase(...)` or the equivalent `CanConstructUnit(...)` / `CanEnqueueUpgrade(...)` call.
- [ ] The test or investigation artifact distinguishes missing prerequisites from missing money and asserts both states.
- [ ] Existing UI behavior for dozers already building, container slots, purchased/enqueued upgrade overlays, and script-only commands is preserved.
- [ ] New `.cs` files are UTF-8 with BOM, `dotnet format src` has been run, and `dotnet format src --verify-no-changes` passes if a test/code change is made.

## Validation commands

```sh
rg -n "CanPurchase|CanConstructUnit|CanEnqueueUpgrade" src/OpenSage.Mods.Generals/Gui src/OpenSage.Game/Logic/Object/GameObject.cs
dotnet build src
dotnet test src --filter "FullyQualifiedName~GeneralsControlBarBuildabilityTests"
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not redesign the control bar or command-button model.
- Do not remove existing button overlay, hover, or callback behavior while wiring buildability.
- Do not satisfy this card with a skipped UI/game-install test only; if no headless seam exists, mark `Blocked` and identify the exact seam needed.
- Do not change BFME control bars except to compare existing `CanPurchase(...)` usage as a reference.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Tick the "Locate + verify the control-bar buildability consumer of `GameObject.CanPurchase/CanConstructUnit/CanEnqueueUpgrade`" P5 checklist item in [`implementation-checklist.md`](../../implementation-checklist.md).
- Append a result line to repo-root `.exchange`.
- Confirm `src/OpenSage.Game.Tests/Gui/GeneralsControlBarBuildabilityTests.cs` was created if a headless UI seam was available, or record the blocked seam in this card.

## Result

<!-- append after completing -->

