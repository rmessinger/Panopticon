# Track M · M2: EE2 combat units

> Audience: human + agent.

Scope: [M2 checklist](../implementation-checklist.md#m2-ee2-combat-units).
Architecture: [M2 pipeline](../deep-dives/mass-battle-1k.md#9-architecture-diagrams).
Import policy boundaries: [H2a handoff](../deep-dives/h2a-unit-content-import.md#m2-handoff).

## Card sequence

| Card | Deliverable | Initial gate |
|---|---|---|
| [WI-M2-01](../agent/cards/WI-M2-01-ddf-roster-resolution.md) | Complete DDF inventory, inherited dependencies, authored reference roster | Ready |
| [WI-M2-02](../agent/cards/WI-M2-02-combat-aircraft-materialization.md) | Typed movement, combat, aircraft, and visual materialization | Reconcile roster evidence after 01 |
| [WI-M2-03](../agent/cards/WI-M2-03-scoped-roster-registration.md) | Scoped roster publication beside Generals | Reconcile after 02 |
| [WI-M2-04](../agent/cards/WI-M2-04-p9-contract-validation.md) | P9 contract results, aircraft rearm, phase report | Reconcile after 03 |

One card per commit; one M2 phase PR left open for operator merge. Draft cards become Ready only after prerequisite names and runtime contracts are reconciled with landed code.

## Reference roster and inventory

Pending WI-M2-01: exact source unit IDs, source paths, epoch, regional model bindings, ability/property census, supported archive layering, and explicit capability exclusions. Required classes: infantry, light tank, heavy tank, artillery, fighter, bomber, helicopter. No guessed IDs or invented authored values.

## Runtime gate

Existing path: `Ee2UnitAdapter` emits shared records; `UnitImportResolution` validates them; `UnitRuntimeMaterializer` constructs native module data; `UnitContentPublisher` installs source-qualified definitions and Track C visuals.

Planned extension: typed aircraft construction using `JetAIUpdateModuleData`, not non-Generals legacy module passthrough. Fixed-wing parking is evidenced by M1; helicopter return/rearm remains an explicit proof obligation. Unsupported required runtime behavior blocks affected reference entries.

## Validation evidence

Pending implementation. Record per-card commands, test counts, source-value assertions, publication rollback checks, P9 contract reports, aircraft ammunition/refill observations, and visual evidence here. Headless measurements do not establish windowed rendering success.
