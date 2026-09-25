# Deep Dive: W1 Aggregate Sector Simulation

> Expands phase W1 of [Track W: Western Front Simulation](../track-w-western-front.md). Builds on
> [W0](w0-charter-fixture.md); charter: [western-front-scale-simulation.md](western-front-scale-simulation.md).
>
> Audience: human design. Cards are written after W0 lands, from this document.

Purpose: run several connected sectors with formation movement, supply flow, delayed orders,
echelon-local information, and aggregate combat only. No tactical region activates in W1; the
scheduler stays in compressed mode.

---

## 1. Decisions applied

| Gate | W1 consequence |
|---|---|
| 3. Clocks | Weather, production, and entrenchment growth run at campaign cadence; everything else at operational cadence. |
| 4. Combat | Stochastic Lanchester: square law for aimed fire, linear law for area and artillery fire, seeded; a mean-field variant runs the same equations on expected values. |
| 5. Conservation | Every system mutates conserved fields only through `WesternFrontLedger`; per-frame residuals must be empty. |
| 6. Command | Echelon-local information; per-link order and report latency by communication type with disruption. |
| 7. Logistics | Artillery ammunition, SAA, and replacements flow through capacitated links with travel time; links owned by the tail node's region. |
| 12. Envelopes | Measure only; no performance or accuracy targets. |

## 2. Systems

All systems implement `IWesternFrontSystem` and register with the W0 scheduler. Order within one
operational tick is fixed:

| Order | System | Cadence | Reads | Writes |
|---|---|---|---|---|
| 1 | `WeatherSystem` | Campaign | parameters, seeded stream | `theater` weather fields, `region` ground effects |
| 2 | `ProductionSystem` | Campaign | parameters | `pool` via `Produce` |
| 3 | `CommunicationSystem` | Operational | message queue, link disruption | delivers orders and reports due this tick |
| 4 | `IntelligenceSystem` | Operational | contacts, observation assets, reports | per-headquarters `IntelligencePicture` |
| 5 | `CommandSystem` | Operational | `EchelonView` only | new orders and reports into the message queue |
| 6 | `MovementSystem` | Operational | orders, road graph, region capacity | formation region, fatigue |
| 7 | `LogisticsSystem` | Operational | demands, link capacity, stocks | shipments, stocks via ledger |
| 8 | `ConsumptionSystem` | Operational | activity, strength | stocks to `ledger` sinks |
| 9 | `AggregateCombatSystem` | Operational | contested region pairs | casualties, equipment loss, cohesion, morale, displacement |
| 10 | `EntrenchmentSystem` | Campaign | holding formations, region terrain | `entrenchment`, `trench`, `wire` |

Communication runs before command so decisions use the freshest delivered information; combat runs
after logistics and consumption so it sees the stock available this tick.

## 3. Command and information

### 3.1 Orders and reports

`FormationOrder` carries issuer, recipient, issue time, expected arrival, priority, validity window,
objective (hold region, attack region, move to region, relieve formation, bombard region), permitted
initiative, and the issuer's information snapshot id. `FormationReport` carries sender, recipient,
observation time, own strength estimate, and observed enemy contacts.

### 3.2 Communication network

Each parent-child headquarters pair has one or more `CommLink` entries:

| Type | Base latency | Disruption |
|---|---|---|
| Runner | region distance / runner speed | casualty chance per region crossed under fire |
| Telephone | fixed short delay | line cut probability per tick from artillery in either endpoint region; repair time |
| Wireless | fixed delay plus encoding time | availability flag per headquarters; interception adds to enemy intelligence |

A message uses the fastest available link; a cut or unavailable link falls back to the next.
Each echelon adds a staff processing delay before relaying downward. Delivery, acknowledgement,
expiry, and loss are recorded as events and as `order-latency` and `order-loss` metric series.

### 3.3 Echelon-local information

Each headquarters holds an `IntelligencePicture`: known enemy formations or aggregates with
estimated strength, region, observation time, and confidence. Sources: own-region contact,
observation assets (aircraft, balloons, observation posts) with per-tick detection probability
by weather, subordinate reports, and wireless interception.

`CommandSystem` passes each policy an `EchelonView`: the headquarters' own state, its
subordinates' last reported state, its picture, and its received orders. Policies receive no
reference to `WesternFrontState`, so omniscient reads are a compile-time impossibility. W4
verifies the rule with an information-audit metric.

### 3.4 Policies

`IFormationCommandPolicy` per echelon, selected like A4 AI policies. W1 ships rule-based
references: army and corps allocate reserves and artillery by objective priority; division and
brigade assign battalions to front regions and issue attacks at scheduled times; battalion holds
or attacks per orders and withdraws at a cohesion breakpoint. Learned policies stay behind the
[A4 seams](a4-pluggable-ai.md).

## 4. Movement

Formations occupy one region. Movement orders follow the road-link graph between regions at an
echelon march rate reduced by `ground_state` and fatigue. Region stacking capacity limits
occupancy per side. Aggregates move as a unit. Movement into an enemy-occupied region is an
attack and resolves through combat, not movement.

## 5. Logistics

A per-tick allocation over the directed link graph:

1. Formations post demand per commodity to their supplying dump (target stock minus current
   stock, bounded by carrying capacity).
2. Dumps post aggregated demand to the railhead; railheads draw from `pool`.
3. Each link ships up to its capacity per commodity, adjusted by `damage`. Allocation among
   competing demands uses priority tiers from orders (attack > hold > reserve), then proportional
   shares with largest-remainder rounding, so allocations are exact integers.
4. Shipments move stock to the link's `in_transit_*` holding and arrive after `travel_time`.
5. Interdiction (artillery on a link's regions) raises `damage`; engineers repair per tick.

Metrics: `unmet-demand` per commodity and formation, `link-utilization` per link,
`stock-days` per formation.

## 6. Consumption

Per operational tick, per formation: SAA and artillery rounds consumed by activity (quiet, holding
under fire, attacking, bombarding) scaled by personnel or guns; replacements absorbed from the
supplying dump up to establishment strength. Consumption sinks to `ledger` `consumed_*`.

## 7. Aggregate combat

### 7.1 Engagement

A region pair is contested when opposing formations occupy adjacent regions across the front, or
one side attacks into the other's region. Artillery in range bombards any target region under a
bombard order. Each contested pair resolves once per operational tick at the authority of the
defending region's controller (the W0 authority rule).

### 7.2 Stochastic Lanchester

For a tick of length `dt`:

- Aimed fire (square law): expected casualties inflicted by side A on B are
  `lambda = alpha * (rifles_A * w_rifle + mg_A * w_mg) * s_A * c_A * (1 - p_B) * dt`, where `s_A`
  is SAA sufficiency (0 to 1), `c_A` cohesion, and `p_B` protection from B's entrenchment and
  region trench level.
- Area fire (linear law): expected casualties from A's artillery on B are
  `lambda = beta * rounds_fired_A * density_B * (1 - q_B) * dt`, where `density_B` is B's personnel
  per region area and `q_B` protection from entrenchment and dugouts. `rounds_fired_A` is limited
  by guns, rate of fire, and stock, and is consumed through the ledger.
- Stochastic variant: casualties are drawn from `Poisson(lambda)`, capped by current strength.
- Mean-field variant: a per-formation real accumulator adds `lambda`; integer casualties are the
  accumulator's whole part, and the fractional remainder carries to the next tick.

Casualties split into killed, wounded, and missing by parameter fractions and sink through the
ledger. MG and gun losses are drawn per casualty with parameter probabilities. Crater level rises
with rounds fired into a region; wire falls with rounds fired at wire targets.

### 7.3 Random streams

Each draw uses `SageRandom` seeded from `hash(runSeed, regionPairKey, tickIndex, streamName)`, so
a draw does not depend on iteration order or on how many other regions resolved first. The
aggregate model is therefore a pure function of state, parameters, and seed. W1 claims same-seed
repeatability for this model only; the host and other domains make no such claim.

### 7.4 Morale and outcome

Cohesion falls with the tick's casualty fraction and fatigue and recovers while in reserve. Morale
moves slowly at campaign cadence. Below the breakpoint a battalion withdraws one region toward its
rear; an attacker takes a region when the defender withdraws or is destroyed. Outcomes are recorded
as `front-line` metric samples per sector.

## 8. Parameters

`WesternFrontParameters` is one JSON document: combat coefficients, weights, protection curves,
consumption rates, march rates, link capacities, latency and disruption values, and breakpoints.
Every value is synthetic in W1 and carries a unit. W4 sweeps it.

## 9. Schema additions

W1 raises the W0 schema to version 2 so command and logistics state reconstructs:

| Kind | Layer | Key | Content |
|---|---|---|---|
| `message` | operational | `message:<seq>` | order or report fields, due time, status (pending, delivered, acknowledged, expired, lost) |
| `commlink` | operational | `commlink:<from>-><to>/<type>` | latency, cut flag, repair remaining |
| `contact` | operational | `contact:<hq>/<target>` | estimated strength, region, observation time, confidence |
| `shipment` | operational | `shipment:<seq>` | link, commodity, amount (Holding), arrival time |

Shipments replace the W0 `in_transit_*` link fields, which become derived metrics. Delivered and
expired messages drop from state after one campaign tick; their history remains in the recorded
deltas.

## 10. Scenario

The W0 fixture extended with a scripted blue offensive in `sector:0`: a five-day bombardment
followed by an attack order. Red holds with its reserve aggregate. The run lasts seven theater
days (1008 compressed host frames) and stops early if a side's front line collapses.

## 11. Evidence (phase milestone)

- Conservation residuals are empty at every frame for all families.
- Order delivery, acknowledgement, and loss metrics are present per echelon pair.
- Front, formation, and logistics state reconstructs from the domain-state artifact at any frame.
- Two runs with one seed produce identical state frames under both combat variants.
- An A2 experiment over 32 seeds reports distributions of casualties, ground taken, and unmet
  demand per variant.

Failures of the same-seed assertion follow the repository determinism-test rule: preserve the
artifacts and report before fixing.

## 12. Sub-items

| Item | Deliverable |
|---|---|
| W1-1 | Parameters document and loader; schema version 2 (§9). |
| W1-2 | Message queue, comm links, orders, reports (`CommunicationSystem`). |
| W1-3 | Intelligence pictures, `EchelonView`, rule-based command policies. |
| W1-4 | Movement over the road graph with stacking. |
| W1-5 | Logistics allocation, shipments, interdiction, consumption. |
| W1-6 | Aggregate combat (both variants), morale, terrain effects. |
| W1-7 | Offensive scenario, evidence tests, A2 experiment report. |

## 13. Architecture diagrams

Conceptual until W1 cards are written; names follow the W0 types.

```mermaid
classDiagram
  class IWesternFrontSystem {
    <<W0>>
    +Cadence SystemCadence
    +Step(context)
  }
  class CommunicationSystem
  class IntelligenceSystem
  class CommandSystem
  class MovementSystem
  class LogisticsSystem
  class ConsumptionSystem
  class AggregateCombatSystem {
    +Variant CombatVariant
  }
  class IFormationCommandPolicy {
    <<interface>>
    +Decide(EchelonView) Decisions
  }
  class EchelonView {
    +Self
    +SubordinateReports
    +Picture IntelligencePicture
    +ReceivedOrders
  }
  class MessageQueue {
    +Enqueue(message, dueTime)
    +DueAt(time)
  }
  class CommLink {
    +Type CommType
    +Latency
    +Disrupted bool
  }
  class WesternFrontLedger {
    <<W0>>
  }
  class WesternFrontParameters

  IWesternFrontSystem <|.. CommunicationSystem
  IWesternFrontSystem <|.. IntelligenceSystem
  IWesternFrontSystem <|.. CommandSystem
  IWesternFrontSystem <|.. MovementSystem
  IWesternFrontSystem <|.. LogisticsSystem
  IWesternFrontSystem <|.. ConsumptionSystem
  IWesternFrontSystem <|.. AggregateCombatSystem
  CommunicationSystem --> MessageQueue
  CommunicationSystem --> CommLink
  CommandSystem --> IFormationCommandPolicy
  CommandSystem ..> EchelonView : builds
  LogisticsSystem ..> WesternFrontLedger
  ConsumptionSystem ..> WesternFrontLedger
  AggregateCombatSystem ..> WesternFrontLedger
  AggregateCombatSystem ..> WesternFrontParameters
```

```mermaid
sequenceDiagram
  participant S as WesternFrontScheduler
  participant M as CommunicationSystem
  participant I as IntelligenceSystem
  participant C as CommandSystem
  participant V as MovementSystem
  participant L as Logistics and Consumption
  participant K as AggregateCombatSystem
  participant G as WesternFrontLedger

  S->>M: operational tick
  M->>M: deliver due orders and reports
  S->>I: update pictures from contacts and reports
  S->>C: per headquarters, Decide(EchelonView)
  C->>M: enqueue new orders and reports
  S->>V: move formations along ordered routes
  S->>L: allocate, ship, arrive, consume
  L->>G: Move and Sink
  S->>K: resolve contested region pairs
  K->>G: casualties and rounds fired
```
