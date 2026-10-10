# Task Card `WI-X1-04`: Readable mixed-source duel capture

> Audience: agent.

| Field | Value |
|---|---|
| Card ID | `WI-X1-04` |
| Roadmap phase | [X1](../../track-x-fixes-tweaks.md) |
| Status | `Ready` |
| Type | `pattern-replication` |
| Effort | `S` |
| Prerequisites | `WI-X1-03` |
| Diagrams | [Architecture](../../deep-dives/x1-imported-unit-hit-feedback.md#architecture-diagrams) |

## Goal

Produce a clearly identified two-unit duel with visible target impacts, actor labels, health feedback, and complete framing.

## Context to read (and only this)

- [Audit/results](../../implementation/track-x-x1-hit-feedback.md#audit-and-results).
- `docs/screenshots.md`.
- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/capture-ee2-reference-roster.json`.
- `src/Panopticon.Tools.TestHarness/ScenarioCaptureRunner.cs`: actor labels, camera fitting, capture/report paths.
- Existing capture UI/overlay facilities identified by X1-01.

## Steps

1. Opt the tank into the landed damage-effect policy. Keep real weapon orders and both `ActorDamaged` assertions.
2. Widen the camera and capture bounds to include both full models and effects throughout movement and firing. Reuse labels and health bars or add the audit's bounded capture-only overlay; do not build a new general HUD.
3. Capture real rendered frames at a cadence that preserves short-lived impacts. Verify Rebel hits visibly affect the tank and tank hits visibly affect the Rebel; no composited fake hit flashes.
4. Share a labeled duel GIF and its machine-readable report under `/mnt/pit2/Network Storage/temp/`. Put per-frame PNGs under `frames/x1-hit-feedback/`. Record command, frame examples, and both damage results.
5. If shared capture/UI code changes, follow its AGENTS.md and run ReplaySketch screenshot mode for map and model tabs; preserve the map panel. Record any presenting-display limitation without substituting headless assertions for visual proof.

## Files likely to change

- `src/Panopticon.Tools.ReplaySketch.Core/Profiles/capture-ee2-reference-roster.json`.
- Audit-approved capture-only framing/overlay code and focused tests, if existing facilities are insufficient.
- `docs/roadmap/implementation/track-x-x1-hit-feedback.md` and phase/checklist/progress documents.
- `docs/screenshots.md`, only for a newly discovered capture capability or limitation.

## Acceptance criteria

- [ ] GIF clearly identifies the EE2 tank and Generals Rebel; both complete units remain in frame.
- [ ] Target-side impacts and health reduction are visible in both attack directions, supported by real weapon damage assertions.
- [ ] GIF cadence shows short-lived impacts; report and reproduction command accompany it.
- [ ] Existing Generals and ReplaySketch map behavior remain intact; any blocked visual proof leaves this card Blocked.
- [ ] Output is labeled as a duel, not a full roster, rearm, or mass-battle proof.

## Validation commands

```sh
CNC_GENERALS_PATH="/home/rmessinger/dev/Command and Conquer Generals" \
EMPIRE_EARTH_II_PATH="/home/rmessinger/dev/Empire Earth II Gold" \
dotnet run --project src/Panopticon.Tools.TestHarness -- \
  --headless --built-in-scenario ScenarioCapture --scenario capture-ee2-reference-roster.json
```

Use the documented rendering/capture recipe for visible-effect proof. Run build, relevant tests, format, and brace checks for source edits.

## Out of scope / guardrails

No M2 aircraft/animation remediation or EE2 particle importer. Health bars alone do not satisfy impact evidence. Preserve determinism-deviation artifacts and wait for operator determination.

## On completion

Set Done only with rendered evidence, tick X1-4, set X1 Done when all cards pass, update README progress and `.exchange`, and leave the PR open.
