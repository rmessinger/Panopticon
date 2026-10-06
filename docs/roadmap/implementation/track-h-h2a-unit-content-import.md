# Track H · H2a: Unit-content import layer

> Audience: human overview above the divider; agent-context detail below it.

Implements the H2a row of [Track H](../track-h-storage-formats.md). Design source of truth:
[deep-dives/h2a-unit-content-import.md](../deep-dives/h2a-unit-content-import.md), including its
[architecture diagrams](../deep-dives/h2a-unit-content-import.md#architecture-diagrams).

## Scope

Source adapters emit engine-owned unit content. Shared resolvers, a validator, materializers, and a
scoped publisher turn it into native runtime definitions. Generals INI is the reference adapter; a
bounded EE2 ground-combat slice is the second source.

## Card sequence and staging

Staging branch `staging/track-h-h2a`. Each group is one stacked PR, left open for operator merge.

| Group | Cards | Deliverable |
|---|---|---|
| 1 | `WI-H2a-01` | Loading audit |
| 2 | `WI-H2a-02` | Contract and design |
| 3 | `WI-H2a-03`..`06` | Interchange, inheritance, dependency, validation |
| 4 | `WI-H2a-07`..`10` | Body/movement, combat, abilities, production/upgrade materializers |
| 5 | `WI-H2a-11`, `12` | Visual-state binding, scoped publication |
| 6 | `WI-H2a-13`, `14` | Generals reference adapter, parity evidence |
| 7 | `WI-H2a-18`, `15`, `16` | EE2 unit selection, DDF reader, EE2 adapter |
| 8 | `WI-H2a-17` | Mixed-source P9 proof, M2 handoff |
| Follow-up | `WI-H2a-19` | Staged Generals INI registration |

Groups 3 to 6 do not depend on the EE2 unit selection. D1 to D4 are approved (D2: per-source collection sets).

---

## Agent context

- Interchange location: `src/Panopticon.Simulation/Content/Importing/Units/`, one type per file.
- Materialization target: detached `ObjectDefinition`, `WeaponTemplate`, `ArmorTemplate`, `LocomotorTemplate`, and module data; registration only through the publisher.
- Naming: per-source `AssetStore` collection sets; Generals keeps the existing store; spawn lookups take a source.
- Publication boundary: one `AssetStore.PushScope` per publication, `PopScope` for rollback and unload.
- Legacy preservation: Generals module families outside the typed capability matrix use the adapter-owned legacy binding (D1).
- Tests: synthetic tests need no install; real-install tests declare `CNC_GENERALS_PATH` or `EMPIRE_EARTH_II_PATH` and never count a missing install as success.
