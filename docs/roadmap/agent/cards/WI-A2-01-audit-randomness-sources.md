# Task Card `WI-A2-01`: Audit randomness sources and classify them as seeded or uncontrolled

| Field | Value |
|---|---|
| Card ID | `WI-A2-01` |
| Roadmap phase | [Track A · A2](../../track-a-foundations.md): Statistical validation |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | None: A2 has no deep dive yet. This card is one of the investigations that produces the material for one. |

## Goal

Produce the authoritative list of every randomness source in `src/`, classified as seeded through
`IRandom` or uncontrolled, and split by whether it affects simulation logic or only presentation.
This is the first checklist item of A2 and the input to `WI-A2-03`. Read-only: no code changes.

## Context to read (and only this)

- `docs/roadmap/track-a-foundations.md`: the A2 phase row.
- `docs/roadmap/open-questions.md`: the determinism-priority Decision.
- `src/OpenSage.Mathematics/IRandom.cs`: the seeded-source interface and `SystemRandom`.
- `src/OpenSage.Game/IGame.cs:200-210`: `CreateRandom()`, the factory every seeded consumer uses.
- `src/OpenSage.Game/Logic/GameLogic.cs:60-75`: the logic-side `Random` instance.
- `src/OpenSage.Game/Client/GameClient.cs:20-30`: the client-side `Random` instance.

## Steps

1. Grep `src/` for `new Random(`, `Random.Shared`, `IRandom`, `CreateRandom`, and `RandomUtility`.
2. For each hit record: `file:line`, the owning type, and a classification of `seeded-logic`
   (flows from `GameLogic.Random`), `seeded-client` (flows from `GameClient.Random`), or
   `uncontrolled` (constructs its own `Random`, or uses `Random.Shared`).
3. For each `uncontrolled` hit, record whether its output can affect simulation state, meaning it is
   read on a path reachable from `GameLogic.Update`, `Scene3D.LogicTick`, or scripting, or whether
   it is presentation-only. Known candidates to classify explicitly:
   `ParticleSystemUtility.Random`, `AudioSystem._random`, `LuaScriptEngine`'s local `Random`,
   `SkirmishGameSettings.Seed`, and `SkirmishSlotDefaults`.
4. Record where the simulation seed originates today and whether it is persisted in saves and
   replays, based only on `SkirmishGameSettings.Seed` and the `IRandom.Seed` property.
5. Create `docs/roadmap/implementation/track-a-a2-reproducibility.md` following the structure of
   `docs/roadmap/implementation/track-g-g1-high-dpi.md` (human-facing summary above the `---`
   divider, agent-context detail below) and write the table into a `## Randomness source audit`
   section below the divider.
6. Add a `## Track A · A2 : Statistical validation` row group for the new plan in
   `docs/roadmap/implementation/README.md`.
7. Recommend nothing beyond the classification. Do not propose a redesign of `IRandom`.

## Files likely to change

- `docs/roadmap/implementation/track-a-a2-reproducibility.md` (new)
- `docs/roadmap/implementation/README.md`

## Acceptance criteria

- [ ] Every `new Random(`, `Random.Shared`, and `CreateRandom` call site in `src/` appears in the
      table with a classification.
- [ ] Every `uncontrolled` entry is marked as simulation-affecting or presentation-only.
- [ ] The seed's origin and its persistence in saves and replays is stated explicitly.
- [ ] No `.cs` file is modified by this card.

## Validation commands

```sh
# Read-only card. Confirm the working tree contains no source changes:
git status --short -- 'src/*'
```

## Out of scope / guardrails

- Do not change any randomness call site, including obvious ones.
- Do not audit non-random nondeterminism such as iteration order or wall-clock reads. That is
  `WI-A2-02`.
- Do not turn the audit into a deterministic-execution requirement.

## On completion

- Set Status above to `Done` (or `Blocked` + reason).
- Append a result line to repo-root `.exchange`, including the count of simulation-affecting
  `uncontrolled` sources.
- Follow-up cards: none new; `WI-A2-03` consumes this table.
