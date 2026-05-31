# Deep Dive: Economy & Combat Verification (P4 & P6)

> Expands phases **P4** (economy loop) and **P6** (combat & command) of
> [Track P — Playable Baseline](../track-p-playable-baseline.md). Part of the
> [Large-Scale Simulation Roadmap](../README.md). First-pass design — refine as it lands.
>
> P5 (construction/production *mechanism*) is covered in [`skirmish-ai.md`](skirmish-ai.md) §1.4/§2.4,
> since the AI drives it. This doc covers the **economy loop (P4)** and **combat/command (P6)**, both
> of which the audit found **mostly present but unproven** — so these phases are largely
> *verify-and-close-gaps*, not greenfield.

---

## P4 — Economy loop end-to-end

**Goal:** verify and complete the full money loop: harvest/supply collection → deposit to
`BankAccount` → fund construction and production → spend; close gaps in supply-truck/worker/dozer
cycles and power.

### Current code (grounded) — mostly present
- **Money state:** `BankAccount.Withdraw(...)` (`Logic/BankAccount.cs:22-38`) and `Deposit(...)`
  (`:41-60`); `Deposit` records income via `Player.AcademyStats.RecordIncome()`.
- **Wiring:** `Player` holds `public readonly SupplyManager SupplyManager;` and
  `public readonly BankAccount BankAccount;`, initialized in the ctor (`Logic/Player.cs:22,73,185-186`).
- **Gather → deposit:** `SupplyCenterDockUpdate.cs`, `SupplyWarehouseDockUpdate.cs`,
  `SupplyTruckAIUpdate.cs` (full harvester loop in `SupplyAIUpdate.cs:8-258`); money credits through
  `BankAccount.Deposit(...)` (no separate economy service).
- **Power model:** `Player.GetEnergy(allGameObjects)` sums owned `EnergyProduction` (`Player.cs:92-104`);
  a `_hasInsufficientPower` field exists (`Player.cs:41`), but **no full power loop is confirmed**.
- **Funding gap (the key unknown):** the audit **could not confirm** that construction/production
  *debit* `BankAccount` — this must be verified in `ProductionUpdate` and the build/queue path, and
  added if missing.

### Design (verify & close)
1. **Prove the full cycle** end-to-end with a [P0 harness](playable-baseline-harness.md) scenario:
   harvester gathers → `BankAccount` balance rises → a queued unit/structure **debits** the balance →
   spend is blocked when funds are insufficient. Assert money is **never negative** (a P7 invariant).
2. **Confirm/implement the debit:** ensure `ProductionUpdate.QueueProduction/QueueUpgrade`
   (`ProductionUpdate.cs:472-525`) and the build path (`OrderProcessor.BuildObject`,
   `OrderProcessor.cs:96-121`) withdraw via `BankAccount.Withdraw(...)`, with refund on cancel
   (`CancelProduction`/`CancelUpgrade`, `ProductionUpdate.cs:497-549`).
3. **Power loop:** confirm `_hasInsufficientPower` is driven by `GetEnergy` vs. consumption and that
   low power has the intended effect (production slowdown / disabled structures) — implement if absent.

### P4 sub-phases
| Phase | Goal | Status |
|---|---|---|
| **P4-1. Gather→deposit verification** | Prove harvest/supply credits `BankAccount` for the baseline factions (harness scenario + assertions). | Not started |
| **P4-2. Spend/debit verification** | Confirm construction/production/upgrade **debit** `BankAccount`, with cancel refunds; implement if missing. | Not started |
| **P4-3. Power loop** | Confirm/implement energy production vs. consumption and its effect (`_hasInsufficientPower`). | Not started |

---

## P6 — Combat & command verification

**Goal:** confirm the player can select units and issue move/attack/build/ability orders through the
control bar end-to-end, and that weapon/damage-type/death paths cover the units a baseline match uses.
Fill gaps surfaced by P7's match run.

### Current code (grounded) — mostly present, with gaps
- **Selection & command path:** `SelectionSystem.cs`, `OrderGeneratorInputHandler.cs`,
  `Mods.Generals/Gui/GeneralsControlBar.cs` — the UI/control-bar → order-generation → dispatch path.
- **Order generation:** `OrderGenerator` base (`Logic/OrderGenerators/OrderGenerator.cs:12-33`);
  command generators read selection + world position before creating `Order`s; dispatch via
  `NetworkMessageBuffer.Tick` → `OrderProcessor.Process` (`OrderProcessor.cs:42-566`).
- **Combat model:** `Logic/Object/Weapon/Weapon.cs`, `WeaponTemplate.cs`,
  `Logic/Object/Damage/DamageModule.cs`, `Logic/Object/Body/ActiveBody.cs`,
  `Logic/Object/Update/FireWeaponUpdate.cs`, `Logic/Object/Update/DamageFieldUpdate.cs`.
- **Death:** `GameObject.Kill()/OnDie()/Destroy()`; death behaviors invoked from `OnDie(...)`.
- **Gaps the audit flagged:** no central "combat result" event bus; the full
  command-button → `OrderType` → processor path is **not verified for every control-bar action**; and
  damage/health edge cases (selection invalidation on death, ownership cleanup) are unverified.

### Design (verify & close)
1. **Command coverage matrix:** enumerate the control-bar actions a baseline match uses (move, attack,
   force-attack, build, produce, sell, repair, ability/special-power, rally point) and verify each
   maps button → `OrderType` → a **real** `OrderProcessor` handler (the handled set is known —
   `OrderProcessor.cs:42-566`). Author each as a [P0 harness](playable-baseline-harness.md) command
   and assert the effect.
2. **Combat coverage:** verify weapon fire → `DamageModule` → `ActiveBody` health → `Kill/OnDie` for
   the unit/damage-type set the baseline factions field; assert kills, death cleanup, and
   **selection invalidation on death** (a flagged gap).
3. **Death cleanup:** ensure dying objects are removed from selection and from owner tracking — this
   ties directly into the [victory-defeat](victory-defeat.md) death→defeat event (P3-4), so the two
   should share the same death notification.

### P6 sub-phases
| Phase | Goal | Status |
|---|---|---|
| **P6-1. Command coverage matrix** | Map every baseline control-bar action to a verified `OrderType` handler; assert each via the harness. | Not started |
| **P6-2. Combat coverage** | Verify weapon→damage→health→death for baseline units/damage types; assert kills + cleanup. | Not started |
| **P6-3. Death cleanup** | Selection invalidation + owner-tracking removal on death; share P3-4's death event. | Not started |

---

## Integration surface
- **P4:** `BankAccount.Deposit/Withdraw`, `Player.SupplyManager`, `Supply*DockUpdate`/`SupplyAIUpdate`,
  `ProductionUpdate.QueueProduction/QueueUpgrade/Cancel*`, `OrderProcessor.BuildObject`,
  `Player.GetEnergy`/`_hasInsufficientPower`.
- **P6:** `SelectionSystem`, `OrderGeneratorInputHandler`, `GeneralsControlBar`, `OrderGenerator*`,
  `OrderProcessor.Process`, `Weapon`/`WeaponTemplate`/`DamageModule`/`ActiveBody`/`FireWeaponUpdate`,
  `GameObject.Kill/OnDie/Destroy` (+ the shared death event with P3-4).

## Sequencing & dependencies
- **P4 and P6 both depend on P1** (spawned, related players). **P4 gates P2/P5** (the AI needs a
  working economy to spend). **P6** is largely verification and can proceed once units exist.
- Both feed **P7**: P4 supplies the "money never negative" invariant; P6 the "no lost/duplicated
  units" invariant; both asserted through the [P0 harness](playable-baseline-harness.md).
- P6-3 shares the death notification introduced for [P3-4](victory-defeat.md) — coordinate.

## Related
- Track overview: [`../track-p-playable-baseline.md`](../track-p-playable-baseline.md)
- Production mechanism + AI that spends: [`skirmish-ai.md`](skirmish-ai.md) (P2/P5)
- Death→defeat event shared with: [`victory-defeat.md`](victory-defeat.md) (P3)
- Harness for the assertions: [`playable-baseline-harness.md`](playable-baseline-harness.md) (P0)
- Architecture snapshot: [`../current-state.md`](../current-state.md)
