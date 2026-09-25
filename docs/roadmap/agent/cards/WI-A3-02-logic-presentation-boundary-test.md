# Task Card `WI-A3-02`: Lock the logic-to-presentation boundary with an enforcement test

| Field | Value |
|---|---|
| Card ID | `WI-A3-02` |
| Roadmap phase | [Track A · A3](../../track-a-foundations.md): Simulation/presentation seam |
| Status | `Done` |
| Type | `test-addition` |
| Effort | `S` |
| Prerequisites | `WI-A3-01`, plus an A3 deep dive with populated architecture diagrams (the [agent guide](../README.md#how-to-maintain-this-guide) gate) |
| Diagrams | [a3-sim-presentation-seam.md §8](../../deep-dives/a3-sim-presentation-seam.md#8-architecture-diagrams) |

## Goal

Add a test that fails when a logic type reads render-only or audio-only state, so the boundary the
A3 deep dive defines cannot silently regress. The test starts from the known-violation baseline the
audit produced and shrinks as violations are fixed.

## Context to read (and only this)

- `docs/roadmap/implementation/track-a-a3-sim-presentation-seam.md`: the audit's `read` rows, which
  seed the baseline allowlist.
- The A3 deep dive: the boundary definition the test encodes, and which namespaces count as logic
  versus presentation.
- `test/Panopticon.Simulation.Tests/`: existing test conventions. Find a comparable structural test with
  `grep -rl "Assembly" test/Panopticon.Simulation.Tests`.

## Steps

1. Add a test that reflects over `Panopticon.Simulation`'s logic types (the namespaces the deep dive names)
   and asserts none of them reference the presentation types the deep dive names.
2. Seed an explicit allowlist from the audit's `read` rows so the test passes on the current tree.
   Each allowlist entry carries the audit row it came from as a one-line justification.
3. Exclude `Logic/Object/Draw/` by namespace, per the audit's presentation-by-role finding, rather
   than allowlisting each of its types.
4. Assert the allowlist is exact: a violation that is fixed but left in the allowlist fails the
   test. This is what makes the list shrink instead of rot.
5. Keep the test in the non-interactive suite. It must need no game install and no GPU.
6. Run `dotnet format src` and confirm `dotnet format src --verify-no-changes`.

## Files likely to change

- `test/Panopticon.Simulation.Tests/` (new test file)

## Acceptance criteria

- [ ] The test passes on the unmodified tree with the audit-derived allowlist.
- [ ] Adding a new logic-to-presentation read fails the test.
- [ ] Removing a violation without updating the allowlist also fails the test.
- [ ] The test runs under `dotnet test src --filter Category!=Interactive` with no game install.
- [ ] Standard validation passes (see [guide](../README.md#standard-validation)).

## Validation commands

```sh
dotnet build src
dotnet test src --filter Category!=Interactive
dotnet format src --verify-no-changes
```

## Out of scope / guardrails

- Do not fix any violation in this card. The card locks the baseline; separate cards shrink it.
- Do not add a new analyzer package or build-time linting tool. A test is sufficient.
- Do not widen the logic or presentation namespace lists beyond what the deep dive names.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Note partial progress against A3's checklist item in
  [`implementation-checklist.md`](../../implementation-checklist.md): the boundary check exists, but
  the item also requires the violations to be removed and a headless run to prove logic runs with no
  presentation, which A1 delivers.
- Append a result line to repo-root `.exchange`, including the allowlist size.
- Follow-up cards to create:
  - One card per cluster of violations in the allowlist, each removing a cluster and shrinking the
    list.
