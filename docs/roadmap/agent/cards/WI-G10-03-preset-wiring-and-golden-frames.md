# Task Card `WI-G10-03`: Proportions opt-in and golden frames

> Audience: agent. Settings exposure and visual regression coverage.

| Field | Value |
|---|---|
| Card ID | `WI-G10-03` |
| Roadmap phase | [Track G · G10](../../track-g-rendering-clarity.md): Realistic proportions |
| Status | `Blocked` |
| Type | `implementation` |
| Effort | `M` |
| Prerequisites | `WI-G10-02`, `WI-G3-04` |
| Diagrams | [G10 architecture](../../deep-dives/g10-realistic-proportions.md#5-architecture-diagrams) |

## Goal

Expose realistic proportions as an explicit presentation option, disabled by default in both
presets. Guard the audited aircraft/vehicle ratios with proportion-mode captures.

## Context to read (and only this)

- [G10 design](../../deep-dives/g10-realistic-proportions.md) and
  [implementation plan](../../implementation/track-g-g10-realistic-proportions.md).
- G2 settings application and the G3 golden store/update contract.
- The reference manifest and render-only override path from `WI-G10-01` and `WI-G10-02`.

## Steps

1. Expose and persist the proportions option through G2, with both preset defaults false.
   Switching applies presentation state without restarting or changing simulation state.
2. Add fixed-camera captures of Comanche and MiG beside Crusader with the option on/off.
   Use the audited projection, reference ratios and tolerances rather than raw screen widths.
3. Add proportion-mode goldens through the explicit reviewed G3 update procedure.
4. Verify an injected visual-rescale error fails comparison. Preserve any unexpected golden
   or gameplay mismatch and wait for user determination.
5. Record the option, captures and reference-host validation in the implementation plan.

## Acceptance criteria

- [ ] Both presets leave the option off until explicitly enabled.
- [ ] Enabled length ratios meet the audit's stated tolerance in the fixed-camera captures.
- [ ] An injected rescale regression is detected by the proportion-mode gate.
- [ ] Disabled `Faithful` output matches its pre-phase golden, with identical gameplay queries.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Guardrails

No implicit enabling in `Enhanced`, automatic golden regeneration or new viewer.

## On completion

Set this card and G10 to `Done` only when all G10 cards pass, tick G10-3 and the phase checklist,
update README progress, and append a repo-root `.exchange` result.
