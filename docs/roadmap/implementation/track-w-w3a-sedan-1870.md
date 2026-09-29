# Track W · W3a: Sedan 1870

> Audience: human overview above the divider; agent-context detail below it.

Implements the W3a row of [Track W](../track-w-western-front.md). Design source of truth: [W3a Sedan 1870](../deep-dives/w3a-sedan-1870.md), including its architecture
diagrams.

## Scope

Simulate the Battle of Sedan, 31 August to 2 September 1870, on a B4b-generated payload with a historical order of battle, a historical orders timeline, period content, tactical artillery and cavalry, and emergent surrender. Build the generic geospatial topology builder W3b reuses.

## Sub-phase to card mapping

| Sub-phase | Card | Scope |
|---|---|---|
| W3a-1 | `WI-W3a-01` | Scale probe. |
| W3a-2 | `WI-W3a-02` | Operator input contract. |
| W3a-3 | `WI-W3a-03` | Payload validation. |
| W3a-4 | `WI-W3a-04` | Topology builder. |
| W3a-5 | `WI-W3a-05` | Region and cell grid. |
| W3a-6 | `WI-W3a-06` | Feature mapping. |
| W3a-7 | `WI-W3a-07` | Neutral territory. |
| W3a-8 | `WI-W3a-08` | Order-of-battle schema. |
| W3a-9 | `WI-W3a-09` | French order of battle. |
| W3a-10 | `WI-W3a-10` | German order of battle. |
| W3a-11 | `WI-W3a-11` | Template expansion. |
| W3a-12 | `WI-W3a-12` | Ledger families and sinks. |
| W3a-13 | `WI-W3a-13` | Weapon parameters. |
| W3a-14 | `WI-W3a-14` | INI definitions. |
| W3a-15 | `WI-W3a-15` | Hit-resolution seam. |
| W3a-16 | `WI-W3a-16` | Shells and fuzes. |
| W3a-17 | `WI-W3a-17` | Line of sight. |
| W3a-18 | `WI-W3a-18` | Environment schedule. |
| W3a-19 | `WI-W3a-19` | Tactical artillery. |
| W3a-20 | `WI-W3a-20` | Cavalry. |
| W3a-21 | `WI-W3a-21` | Courier and telegraph. |
| W3a-22 | `WI-W3a-22` | Orders timeline dataset. |
| W3a-23 | `WI-W3a-23` | Order applicability. |
| W3a-24 | `WI-W3a-24` | Local surrender. |
| W3a-25 | `WI-W3a-25` | Army capitulation. |
| W3a-26 | `WI-W3a-26` | Scenario assembly. |
| W3a-27 | `WI-W3a-27` | A5 geospatial rendering. |
| W3a-28 | `WI-W3a-28` | All-tactical reference run. |
| W3a-29 | `WI-W3a-29` | Aggregate and mixed arms. |
| W3a-30 | `WI-W3a-30` | Evidence report. |

---

## Agent context

Code hooks: see the deep dive appendix.

## Dependency notes

`WI-W3a-01` is a read-only investigation and is `Ready` now. All other cards stay Draft until
their prerequisites are Done and the named planned types are reconciled with source. The order of
battle (`WI-W3a-08..11`) and period content (`WI-W3a-12..14`) chains are independent and can run
in parallel once W0 and W1 land.
