# Deep Dive: W3a-W6 Integration Experiments

> Expands phases W3a-W6 of [Track W: Western Front Simulation](../track-w-western-front.md).
> Detailed phase designs now live in per-phase deep dives.
>
> Audience: human + agent navigation overview.

Purpose: index the later Western Front demonstrations and keep the original section anchors stable
for inbound links. Each section points to the owning deep dive, implementation plan, and draft
cards.

---

## 1. W3 Geospatial battles

### W3a Sedan 1870

Detailed design: [W3a Sedan 1870](w3a-sedan-1870.md).

Implementation plan: [Track W · W3a](../implementation/track-w-w3a-sedan-1870.md).

Card range: [`WI-W3a-01..31`](../agent/cards/).

Scope: simulate the Battle of Sedan, 31 August to 2 September 1870, all-tactical at about 320,000
soldiers on a B4b-generated payload, with a historical order of battle and orders timeline. W3a
owns the generic geospatial topology builder.

### W3b Somme sector

Detailed design: [W3b Somme sector](w3b-somme-sector.md).

Implementation plan: [Track W · W3b](../implementation/track-w-w3b-somme-sector.md).

Card range: [`WI-W3b-01..06`](../agent/cards/).

Scope: run W1 and W2 evidence on a B4b generated Somme sector. The sector uses lat 49.90-50.17 N,
lon 2.52-2.80 E, UTM 31N, true scale, `metersPerGameUnit` 0.3048, a 1000 m region grid, and 8 x 8
cells per region.

## 2. W4 Command and logistics experiment

Detailed design: [W4 command and logistics experiment](w4-command-logistics-experiment.md).

Implementation plan: [Track W · W4](../implementation/track-w-w4-command-logistics-experiment.md).

Card range: [`WI-W4-01..06`](../agent/cards/).

Scope: execute the full 216-cell experiment grid through A5-2, 64 seeds per cell, and audit every
policy call for echelon-local information access.

## 3. W5 Distributed regional simulation

Detailed design: [W5 distributed regional simulation](w5-distributed-regional-simulation.md).

Implementation plan: [Track W · W5](../implementation/track-w-w5-distributed-regional-simulation.md).

Card range: [`WI-W5-01..08`](../agent/cards/), plus shared B6 cards.

Scope: apply B6 to the Western Front domain first. Shards are frontage strips perpendicular to the
front so contested pairs never cross shards. The first implementation is in-process and proves
memory and capacity for all-tactical runs.

## 4. W6 Long-run campaign experiment

Detailed design: [W6 long-run campaign](w6-long-run-campaign.md).

Implementation plan: [Track W · W6](../implementation/track-w-w6-long-run-campaign.md).

Card range: [`WI-W6-01..08`](../agent/cards/).

Scope: run July-November 1916, about 140 days and 201600 one-minute operational ticks, with
domain-state artifact snapshots, host saves, optional historical OOB investigation, and A5 replay
scrubbing.

## 5. Cross-phase evidence

| Phase | Primary evidence |
|---|---|
| W3a | Exact conservation with 1870 families, 60 h all-tactical reference, aggregate and mixed error, order metrics, and a measured divergence table against the historical record. |
| W3b | B4b control-point tolerances, generated topology source keys, W1/W2 evidence on real geography. |
| W4 | Matrix distributions, sensitivity ranking, information-audit failures when policies read outside their `EchelonView`. |
| W5 | Shard ownership, conservation, boundary-message, handoff, and comparison metrics against the single-process reference. |
| W6 | Memory and artifact bounds, save/load reconstruction, replay scrub latency, provenance, and declared approximation error. |
