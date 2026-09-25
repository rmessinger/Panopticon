# Deep Dive: W3-W6 Integration Experiments

> Expands phases W3-W6 of [Track W: Western Front Simulation](../track-w-western-front.md).
> Builds on [W1](w1-aggregate-sector.md) and [W2](w2-fidelity-promotion.md).
>
> Audience: human design outline. Each phase gets its own deep dive and cards when its
> prerequisites land.

Purpose: fix the scope, inputs, and evidence of the later demonstrations so earlier phases do not
close off their options.

---

## 1. W3 Geospatial sector

Region: the Somme 1916 sector, the Allied attack frontage of 1 July 1916 between Gommecourt and
Maricourt with its rear areas, about 30 x 20 km, inside UTM zone 31N (EPSG:32631). The exact
bounding box is fixed at W3 planning.

Inputs, all operator-supplied per the [B4b source-boundary Decision](../open-questions.md):

- DEM: a georeferenced elevation raster covering the box.
- Imagery: optional georeferenced basemap or georeferenced historical trench-map scans.
- Vectors: KML/KMZ of 1916 roads, railways, rivers (Somme, Ancre), villages, woods, and the
  1 July front lines, digitized by the operator.

Design:

- B4b generates the map package; W3 adds a `WesternFrontTopologyBuilder` that derives sectors,
  regions, cells, depot nodes, and road/rail links from the package's vector layers and records
  the source feature id on every key.
- Regions follow a regular projected grid clipped to the box (default 2 km), with front sectors
  aligned to the digitized line. Cells follow B4a terrain chunks, so tactical and aggregate
  representations share one map transform.
- Order of battle stays synthetic, placed on real geography. A historical order of battle is a W6
  input, not a W3 requirement.

Evidence: B4b control-point tolerances hold after packaging; every link and region key maps to
source features; W1 and W2 evidence repeats on the generated sector.

Depends on: W2, B4b.

## 2. W4 Command and logistics experiment

Sweep, through the A5-2 experiment matrix:

| Axis | Values (initial) |
|---|---|
| Order latency scale | 0.5, 1, 2, 4 |
| Reconnaissance detection probability scale | 0.25, 0.5, 1 |
| Link capacity scale | 0.5, 1, 2 |
| Activation policy | never, contact, operator interest |
| Combat variant | stochastic, mean-field |

Outputs per cell: outcome, casualty, supply, command-latency, and cost distributions with
confidence intervals from A2/B1; sensitivity ranking per axis. An information audit samples each
policy call's `EchelonView` provenance and fails the run if any value originates outside the
echelon's received information.

Depends on: W2, A5 (A5-2 sweeps).

## 3. W5 Distributed regional simulation

Shards own sets of region keys (B6 partition input). Rules carried from earlier phases:

- A link's shipments resolve at the tail node region's shard (W0 authority rule).
- A contested region pair resolves at the defending region's shard.
- Formations hand off with their `formation` record, holdings, and pending messages; a handoff
  is a ledger move between shard-local holdings, so conservation is checked across shards.
- Tactical regions never span shards in W5; a region promotes only inside its owning shard.

Evidence: ownership and conservation invariants across shards; exactly-once cross-boundary
combat and logistics effects; handoff, halo, network, and shard-imbalance metrics; aggregate
outcome comparison with the single-process run, not byte identity.

Depends on: W2, B6.

## 4. W6 Long-run campaign experiment

Parameterized campaign on the W3 sector, July to November 1916 (about 140 theater days, about
20000 operational ticks plus tactical windows). Adds:

- save/load of Western Front state through `StatePersister`, starting at schema-matched version 1
  (Gate 10);
- an optional historical order of battle at battalion level, loaded from an operator-supplied
  dataset with provenance per formation;
- replay scrubbing over the domain-state artifact in the A5 visualizer.

Evidence: bounded memory and artifact size; measured wall-clock scaling; reconstruction latency;
complete provenance; the declared approximation error of every active fidelity policy against the
W2 envelope.

Depends on: W3, W4, W5.

## 5. Architecture diagrams

Deferred. Each of W3-W6 gets a dedicated deep dive with grounded class and sequence diagrams
before its first source-edit card is marked `Ready`. The charter's
[conceptual diagrams](western-front-scale-simulation.md#12-architecture-diagrams) cover the
cross-phase flow until then.
