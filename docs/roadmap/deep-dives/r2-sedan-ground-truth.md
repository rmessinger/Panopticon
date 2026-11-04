# Deep Dive: R2 Sedan Vertical Ground Truth

> Operator-approved LiDAR HD ground-truth protocol for the
> [WI-R2-01](../agent/cards/WI-R2-01-sedan-operator-proof.md) Sedan proof, replacing the card's
> levelling-benchmark vertical control; complements
> [Deep Dive: Geospatial Map Generation](r-real-world-terrain.md) §8.1 in
> [Track R: Real-World Terrain](../track-r-real-world-terrain.md).
>
> Audience: human + agent design and execution of the Sedan vertical ground-truth proof.

## 1. Status and operator approval

Operator decision of 2026-11-03, staged and APPROVED: the card's vertical control switches from IGN
levelling benchmarks to blinded ground-truth cells; an approved contract reconciliation between the
card's original vertical-control choice and audited data availability, not a defect fix. No proof
PASS is claimed or implied in this document.

| Limb | Approved content |
|---|---|
| L1 | Roadmap/protocol amendment authorized in principle; this document is that amendment |
| L2 | Isolated `laspy` plus `lazrs` tooling (section 4) |
| L3 | Up to 830532133 bytes of LiDAR HD download (user-corrected six-tile budget, section 3), strictly after the RGE ALTI 2019 / BD ORTHO 2022 archive acquisition finished (it finished 2026-11-03) |
| L4 | First tile `LHD_FXX_0840_6958_PTS_LAMB93_IGN69.copc.laz` (128651625 bytes) passes the verification gate of section 4 before the other five tiles |

Unchanged and binding: the committed tolerance rule `ab30059de` (vertical tolerance = max(stated
DEM accuracy, explicit `tolerances.verticalMeters`)); the approved archive editions RGE ALTI 2-0
1M TIF D008 2019-10-14 and BD ORTHO 1-0 RVB-0M20 D008 2022-01-01; the final numeric tolerance is the
operator-set 1.0 m engineering limit (section 7); the prior +1 m wall-marker allowance is
inapplicable to ground cells and does not carry over.

Protocol version 1.2, user-approved 2026-11-03 as one package: stage-1 gate items c and d are
revised to the reviewed rows in section 4. Item c changes only its population, to the non-empty
10 m cells of the AOI-intersect-observed-first-tile-footprint expected set; the estimator (median
over non-empty cells) and the 4.0 threshold are unchanged, and missing cells stay reported by
item d. Item d becomes the preliminary ground-availability floor: at least 5 eligible cells
across at least 3 full-AOI sectors under the complete unchanged section 6 method, with the 10 m
coverage map and all 718 hole attributions retained verbatim and preliminary candidates never
the final frozen selection; there is no blanket class-6/9/17 hole mask. Provenance of the
approved package: proposal
`ad51c5155614a13c6a809db573768116e6f910ba9407e4782d4b3c621a2acd93`, row diff
`a9a0203b21a009f6ef24d709de6f2b02733f4ebb844ec33ae30dc13482309192`, decision brief
`0a3c2a5d238d6f89b51ce08e967b2ebea80ff2fd7e3f3382d5a4dc182f1121f2`, and the independent delta
re-verification PASS at `~/log/track-r/review-lidar-gate-revision-20261103T1552-0500/delta-reverify.md`.
The approval changes the protocol, not the measurements: the executed v1.1 result stands (items
a, b, c, e, f PASS; d FAIL 718 of 9494), the revised items are not yet implemented or measured,
no new item c median exists, no physical PASS is claimed, and the GATE 3 numeric decision has
not run.

Provenance: approved protocol record `~/log/track-r/sedan-alternative-groundtruth-20261103T0900-0500/`
(protocol v1.1) with independent review `~/log/track-r/review-groundtruth-protocol-20261103T1000/`
(PASS with mandatory corrections, applied); this document owns the method inside the repo.

## 2. Why levelling benchmarks were replaced

The 2026-11-02 audit (`~/log/track-r/sedan-benchmarks-20261102T2120-0500/`) found 34
`IGNF_GEODESIE:rn` marks in the 2 km discovery box, 21 inside the planned 1100 m square, 19 of
those `BON ETAT`, and 0 ground-comparable: every in-square mark is a wall or structure mark (facade
soubassement, bridge or barrage parapet, hydrometric scale, flood mark), and no fiche publishes a
marker-to-ground offset. The WFS levelling marks stay metadata and candidate-exclusion evidence,
never ground elevations. The honesty rules remain binding: never select or prune by DEM residual,
never invent accuracy, never assert unproven lineage independence.

## 3. LiDAR HD source identity

Dataset: Nuages de points LiDAR HD, classified COPC product, lot
`NUALHD_1-0__LAZ_LAMB93_OD_2026-04-29`, URL base
`https://data.geopf.fr/telechargement/download/LiDARHD-NUALID/NUALHD_1-0__LAZ_LAMB93_OD_2026-04-29/`;
tile names follow `LHD_FXX_<E>_<N>_PTS_LAMB93_IGN69.copc.laz`.

| File | Bytes |
|---|---|
| `LHD_FXX_0839_6958_PTS_LAMB93_IGN69.copc.laz` | 119931651 |
| `LHD_FXX_0839_6959_PTS_LAMB93_IGN69.copc.laz` | 146099989 |
| `LHD_FXX_0840_6958_PTS_LAMB93_IGN69.copc.laz` | 128651625 |
| `LHD_FXX_0840_6959_PTS_LAMB93_IGN69.copc.laz` | 130615694 |
| `LHD_FXX_0841_6958_PTS_LAMB93_IGN69.copc.laz` | 153352444 |
| `LHD_FXX_0841_6959_PTS_LAMB93_IGN69.copc.laz` | 151880730 |
| Total | 830532133 |

Actual tile coverage is ultimately verified from each file's own header and WKT geometry at
execution; the filename kilometre convention is not an assumption of coverage, and a separate
catalog review concluded 2026-11-03 with the tile-set correction recorded below. This document
asserts no coverage claim beyond the metadata rectangles proven by that audit; actual per-tile
outcomes are owned by the physical gates (section 7).

Tile-set correction, user decision of 2026-11-03: the previously approved set
E 0839/0840/0841 x N 6957/6958 (total 761133878 bytes) missed the AOI north edge; the AOI
Lambert-93 y maximum 6958167.624 exceeded that union top by 167.624 m, leaving 1887 of 12321 AOI
10 m cells uncovered. The corrected set E 0839/0840/0841 x N 6958/6959 (total 830532133 bytes,
+69398255) is the minimum covering set; the remaining five tiles after the first-tile pin are
701880508 bytes. Provenance: coverage audit
`~/log/track-r/lidar-coverage-correction-20261103T1258-0500/` (six fresh HEAD size checks, cached
WFS metadata, corner-containment geometry; metadata BBOX values are nominal, actual header bounds
come from the physical per-tile gates at execution). The external v1.1 plan record keeps its
historical list; this document supersedes it for the tile list only. Block naming is NW-corner,
so the internal tile seams crossing the AOI remain E 840000, E 841000, N 6958000 and the frozen
E6 list is unchanged. The +50 m window used by that audit and the first-tile probe is a
conservative coverage diagnostic only, not a protocol margin; the protocol's buffers are the
section 5.3 candidate window and the E1-E7 thresholds, and the 1170.432 m rounded render frame is
a preview construct, not the LiDAR AOI.

| Field | Value |
|---|---|
| Mission | 24LHDOD |
| Flight | 2024-11-30 (debut = fin) |
| MOA / MOE | IGN / Eurosense |
| Sensor | RIEGL VQ-780 II-S:H2227189 |
| Classification | IGN_AUTO_V5 (automatic, no manual MOE listed) |
| date_edition | 2026-04-29 |
| Planimetric system | LAMB93 EPSG:2154 |
| Altimetric system | IGN69 normal heights EPSG:5720 |

Documented quality (`DC_LiDAR_HD_1-0.pdf` v1.0 Juillet 2026, section 2.3.1.4): absolute REMQ limits
50 cm planimetry and 10 cm altimetry; strip-to-strip tie-line means 25 cm planimetry and 5 cm
altimetry; local exceedances are possible without invalidating the global quality. Class semantics
(DC section 2.2.8.2): class 2 Sol is natural or artificial ground; water surfaces and bridge decks
are NOT Sol; grass below 20 cm, agricultural deposits, mineral piles, and temporary works are
sometimes Sol; these caveats are why the classification-bias budget line exists (section 6).
Licence Lov2; attribution string `IGN (LiDAR HD), Licence Ouverte 2.0`.

Lineage, with exact epistemics: VERIFIED temporal non-containment only, NOT statistical
independence; LiDAR HD comes exclusively from the 2021+ national program and Sedan was flown
2024-11-30 by Eurosense, so the RGE ALTI 2019-10-14 edition cannot contain the 2024 flight (updates
frozen 2024). NOT known and NOT assumed: the RGE ALTI D008 per-tile source class (LiDAR floodplain
vs photogrammetric), its EMQ, and the actual acquisition date of its content; those are read by the
future guarded extraction/preparation unit of section 7, not by any download helper. The RGE ALTI
1 m figure is resolution, not accuracy; its stated accuracy comes from tile quality metadata.

## 4. Staged fetch, integrity, and the first-tile gate

Tooling (limb L2): isolated virtualenv (no global pip, no root); pinned `laspy` and `lazrs` recorded
with version, license, wheel sha256, and python version into `tools-manifest.json`; chunked point
iteration at a fixed 2,000,000 points per chunk with per-chunk bbox filtering to the AOI plus
margin, never materializing a whole tile, peak memory target below 1 GiB. The extractor is a
proof-local optional helper; the WI-R2-02 provider profiles stay stdlib-only, with any future need
going through a subprocess invocation with JSON I/O.

Transfer discipline: idempotent resumable `curl -fL --max-redirs 5 -C -` with pinned User-Agent
`Panopticon-TrackR-Sedan-Proof/1.0 (LiDAR HD tile fetch)`; after redirect resolution the final
effective URL host must be exactly `data.geopf.fr` over https, otherwise the transfer fails closed.
Per-file completion requires the exact byte count from section 3 AND a recorded sha256, both
written to `sources.txt`; every URL gets a pre-transfer HEAD re-verification, and any URL or
content-length drift from section 3 is STOP and re-verify, never silent substitution. Cache root
`/mnt/pit2/panopticon/geo-proof/sedan/lidarhd/NUALHD_1-0__LAZ_LAMB93_OD_2026-04-29/` under
mountpoint and free-space guards (packed total plus a 2147483648-byte workspace allowance).

| Stage | Content | Condition |
|---|---|---|
| 1 | Tile `0840_6958` only (128651625 bytes) | L3 active; then the first-tile gate |
| 2 | The other five tiles (701880508 bytes) | Gate PASSED on stage 1 |

First-tile verification gate: ALL items must PASS on `0840_6958` before any second tile is fetched;
every outcome is recorded pass or fail; a fail stops the stage and nothing is auto-loosened.

| Item | Check |
|---|---|
| a. File integrity | Byte count exactly 128651625, sha256 recorded, LAS 1.4 header parses, CRS is EPSG:2154 WKT, z in metres |
| b. IGN69 sanity | Class-2 z over the AOI portion within the hard gate 100 to 250 m; the AOI levelling span 149.371 to 156.940 m is recorded as advisory and never gating |
| c. Class-2 density | Median class-2 points per m2 over non-empty 10 m cells of the AOI-intersect-observed-first-tile-footprint expected set at least 4 (median over non-empty cells; all missing cells still reported by item d); Program minimum is 10 pulses per m2; ground yield under canopy and buildings is lower |
| d. Ground availability (stage 1) | At least 5 preliminary eligible benchmark cells under the complete unchanged section 6 method (E1-E7 thresholds, strata, S1-hard, fallback ladder, 100 m spacing) on the canonical full-AOI 3.048 m lattice, evaluation windows fully inside the observed first-tile header footprint, across at least 3 distinct full-AOI sectors of the canonical 3 x 3 sector grid; fewer than 5 cells or fewer than 3 sectors is STOP and escalate; the 10 m coverage map and the full 718-cell attribution remain recorded verbatim; preliminary candidates are availability evidence only, never the final frozen selection. |
| e. Lineage recheck | Bounded WFS re-query of `IGNF_LIDAR-HD_METADONNEE` still reporting mission 24LHDOD, flight 2024-11-30, IGN69, Eurosense; otherwise STOP |
| f. Exclusion data | Classes 9 (eau) and 17 (pont) usable for exclusion rules E2/E3; if class 9 is absent over the AOI portion, the weaker fallback (density and roughness only) is recorded explicitly |

## 5. Blinded pre-DEM ground-truth selection

### 5.1 Blinding boundary

The selection predicate, selection, and manifest are frozen and sha256-hashed BEFORE the literal DEM
height data is opened and before any residual is computed. Pure grid geometry derived from the
extent alone is allowed pre-freeze; RGE archive quality and source masks and text metadata are not
DEM height data and are read only by the future guarded extraction/preparation unit. If any part of
the selection algorithm is found to need DEM samples before the freeze, the protocol STOPS and
reports; it never quietly redefines the boundary.

### 5.2 Frame and evaluation coordinates

Frame EPSG:32631; AOI centre E = 640391.997, N = 5507318.179; square of +/- 550 m in both axes; game
lattice spacing 3.048 m (`metersPerGameUnit` 0.3048); evaluation coordinates are the lattice cell
centres fully inside the AOI minus a one-cell edge margin, about 359 x 359 cells. Conversions
between EPSG:2154 and EPSG:32631 use the system PROJ 9.7.1 `cs2cs` binary, version recorded in the
frozen manifest; local point computations run in the native LAMB93 frame. The measured Lambert-93
vs UTM 31N scale difference at the AOI is +7.77e-4 east and +7.84e-4 north (measured values, not
estimates); per-point cs2cs conversion keeps that difference out of the error chain (worst effect
under 1 m across the full AOI, millimetres inside a 2.5 m window). Game XY per cell is
`(x - 639841.997) / 0.3048` and `(y - 5506768.179) / 0.3048`, anchored to the extent origin;
informational, since the manifest declares lat/lon and leaves horizontal unevaluated.

The grid origin is the exact SW corner of the projected 1100 m extent, E 639841.997, N 5506768.179
(the AOI centre minus 550 m in both axes), not a centred rounded extent: the map extent rounds
outward to whole chunks from that SW anchor, 3 x 3 chunks at this lattice, growing north-east only
(the import rule of the [parent deep dive](r-real-world-terrain.md#4-coordinate-and-elevation-contract)
section 4). The geographic KML extent polygon derives its own projected vertices by transforming the
exact projected square back to EPSG:4326. None of this grid geometry opens DEM height data
(section 5.1).

The truth statistic is a robust plane at the cell centre: the control-point vertical check samples
the generated payload surface, whose lattice vertices are area-weighted averages of the 1 m DEM over
the 3.048 m footprint, then bilinearly interpolated; a robust local plane fitted to class-2 points
in a window matched to the lattice footprint and evaluated AT the evaluation coordinate reproduces
that quantity to first order on any locally planar surface. The median of window points is
explicitly REJECTED as the truth value (slope-dependent median-vs-centre bias); the second-order
term is bounded by the curvature field and gated by E7.

### 5.3 Per-cell computation

Input points: LiDAR class 2 ONLY, within a window of radius 2.5 m of the evaluation coordinate.

| Field | Definition |
|---|---|
| n2, density | Class-2 count in the window; points per m2 |
| truth `z_p` | Robust local plane evaluated at the evaluation coordinate |
| `sigma_fit` | 1.4826 x MAD of the final plane residuals |
| slope | Gradient magnitude of the fitted plane, degrees |
| curvature bound | Max absolute quadratic contribution over the half-footprint |
| class mix | Counts of classes 2, 3, 4, 5, 6, 9, 17 in the window |

Plane fit, exact: ordinary least squares plane `z = a + bu + cv` with u, v relative to the
evaluation coordinate, then exactly two Huber reweighting passes; scale
`s = 1.4826 x MAD(residuals)` with MAD and median using the average of the two central order
statistics; Huber constant 1.345; weights `min(1, 1.345 s / |r|)`; reweighted least squares refit
per pass; fixed constants, no RNG; Huber scale floor `s = max(1.4826 x MAD, 1e-9)`.

Curvature bound, exact: a fixed supplementary full quadratic fit on the same window points with the
final plane Huber weights; quadratic part `q(u,v) = du^2 + euv + fv^2` (d on u squared, e on the
u v product, f on v squared), bounded over the half-footprint square of half-width 1.524 m
(3.048 / 2); the bound is the max of `|q|` over the four square corners plus, per edge, the interior
vertex of the edge's one-dimensional quadratic when it falls inside the edge; the only interior
stationary point is the origin where q = 0, so this bound is exact. Recorded per cell.

### 5.4 Frozen exclusion rules

Values are fixed at protocol v1 before any DEM exists, changeable only by a new protocol version
with a full re-selection, never per-cell; each fired rule is recorded with its trigger value.

| Rule | Condition |
|---|---|
| E1 | n2 below 12 in the window |
| E2 | Any class-9 point within 3.048 m of the evaluation coordinate |
| E3 | Any class-17 point within 3.048 m of the evaluation coordinate |
| E4 | `sigma_fit` above 0.08 m |
| E5 | Slope above 12 degrees |
| E6 | Cell within 3.048 m of the AOI border or of a LiDAR tile seam; seam lines are the LAMB93 kilometre boundaries implied by the tile identifiers E = 840000, E = 841000, N = 6958000 |
| E7 | Curvature bound above 0.05 m |

### 5.5 Deterministic stratified selection

- Strata by slope: S1 = 0 to 4 degrees, S2 = 4 to 8, S3 = 8 to 12; within S1 a hard sub-stratum
  requires `sigma_fit` at most 0.03 m (S1-hard). Sectors: the 3 x 3 arrangement of AOI sectors.
- Ranking key within (stratum, sector), a total order: `sigma_fit` ascending, then n2 descending,
  then distance to sector centroid ascending, then cell row ascending, then cell column ascending.
- Selection floor, REQUIRED: at least 5 valid primaries across at least 3 distinct AOI sectors; if
  fewer than 5 eligible primaries or fewer than 3 distinct covered sectors result after all frozen
  rules, STOP and escalate to the operator; no reduced 3- or 4-point continuation, no relaxation,
  no per-cell exceptions.
- Stratum ladder, a frozen deterministic preference always totaling 5 primaries: preferred
  3 S1-hard + 1 S2 + 1 S3; if S3 is empty, 4 S1-hard + 1 S2; if S2 is empty, 4 S1-hard + 1 S3; if
  both are empty, 5 S1-hard; each ladder step still requires 5 primaries and 3 distinct sectors,
  else STOP. Minimum pairwise distance 100 m, enforced by deterministic greedy in ranking order.
- 2 alternates: the next S1-hard ranks satisfying the same distance rule against the primary set;
  substitution of an alternate for an invalid primary is permitted only BEFORE the DEM-open flag is
  set; after any height or residual inspection, substitution is refused and the situation
  escalates. An exclusion is never relaxed to fill a quota.

### 5.6 Freeze outputs and residual retention

| Output | Content |
|---|---|
| `candidates.tsv` | Every candidate cell with all per-cell fields and the fired exclusion rules (empty when eligible) |
| `selection.tsv` | The 5 primaries plus 2 alternates with truth value, `sigma_fit`, slope, curvature bound, density, class mix, WGS84 lat/lon, game XY, stratum, sector, rank, role |
| `frozen-manifest.json` | Protocol version, all thresholds including the Huber scale floor 1e-9 m, sha256 of both tables, tool versions (python, laspy, lazrs, cs2cs/PROJ), tile sha256 hashes, selection timestamp in ISO 8601 local with offset, the measured Lambert-93 vs UTM 31N scale values +7.77e-4 east / +7.84e-4 north, and the map transform facts the statistic assumes: projected CRS EPSG:32631, extent origin 639841.997 / 5506768.179, `metersPerGameUnit` 0.3048, `verticalExaggeration` 1, `verticalOriginMeters` recorded when the payload is staged, extent-minimum convention assumed, lattice spacing 3.048 |

The frozen hashes are recorded BEFORE the DEM is opened. Afterwards ALL residuals for all selected
cells are retained with no pass/fail pruning and no post hoc exclusion; epoch-related differences
stay visible as residuals. A synthetic prototype with 26 analytic tests (flat, tilted, bowl,
seam-adjacent, empty-stratum, curvature straddling 0.05) validated the determinism of sections 5.3
to 5.5 before any tile was fetched; record
`~/log/track-r/sedan-alternative-groundtruth-20261103T0900-0500/prototype/`.

## 6. Error budget lines (documented inputs, not acceptance thresholds)

Documented inputs only, explicitly NOT numeric acceptance thresholds; the acceptance values are a
separate operator decision (section 7).

| Line | Value / source | Note |
|---|---|---|
| Reference REMQZ | 0.10 m | DC section 2.3.1.4; one reference line of the budget, NOT the total budget |
| Fit scatter | `sigma_fit` and curvature bound | Measured at selection time, recorded per cell; bounds fit scatter only |
| Classification bias | Named channel, UNRESOLVED when no source QA bounds it | Grass below 20 cm classified as Sol sits above bare earth by an unquantified amount; `sigma_fit` does NOT bound systematic grass or misclassification bias; the RGE stated accuracy is NOT a bound on LiDAR auto-classification bias, it bounds the DEM side only; no numeric value is inferred from any of these lines; the numeric decision must either cite a bounding source for this channel or record it explicitly as unresolved, never silently absorbed into a tolerance |
| Raster footprint and kernel matching | Area-weighted 1 m cells into 3.048 m vertices | Plane evaluation matches to first order; second-order term bounded by the curvature line |
| Horizontal coupling | REMQXY 0.50 m x tan(slope) per stratum | S3 bound 0.107 m at 12 degrees |
| Epoch | 2024-11-30 flight vs RGE 2019-10-14 edition | Content acquisition date and EMQ UNKNOWN until the guarded extraction/preparation unit reads the tile quality metadata; real terrain change between epochs cannot be quantified away by an arbitrary allowance or removed by residual-selected exclusions |

The committed max rule `ab30059de` applies unchanged; the +1 m wall-marker allowance is
inapplicable to ground cells and does not carry over.

## 7. Remaining gates and pending decisions

| Gate | State |
|---|---|
| GATE 1: archive acquisition completion | SATISFIED 2026-11-03: all 8 pinned archive parts present, size- and sha256-verified, 29835514326 bytes on disk (acquisition record `~/log/track-r/sedan-download-20261103T0905/`) |
| GATE 2: LiDAR download authorization active | SNAPSHOT 2026-11-03: first tile `0840_6958` acquired (128651625 bytes, local sha256 19465caf3085665f94fd648315896a84cbcf8fd2d9777eca85800c131979d212, no publisher checksum claimed) and its physical section 4 gate executed once under protocol v1.1: items a, b, c, e, f PASS; item d FAIL (718 of 9494 expected cells, the AOI 10 m grid intersected with the observed header footprint, hold no class-2 point); overall physical gate NOT PASS (v1.1 measurement retained as historical; the v1.2 revised items c and d are implemented and the stage-1 gate-only re-run PASSED, see LATEST below); the remaining five tiles (701880508 bytes) are NOT authorized; the numeric tolerance decision stays pending (GATE 3); receipt `~/log/track-r/lidar-firsttile-execution-20261103T1330-0500/probe-outcome-receipt.md` |
| GATE 3: RGE accuracy and epoch, then numeric tolerance | PENDING: RGE actual stated accuracy and content epoch are read from tile quality metadata by a NEW separately specified and separately approved guarded extraction/preparation unit (the committed `sedan_acquire.py` is download-only and owns neither extraction nor quality-mask reads), then the operator numeric tolerance policy is SET below (1.0 m engineering limit) while the accuracy and epoch reads remain |
| GATE 4: this amendment | Lands with the WI-R2-01 card edit before the Sedan proof run |

LATEST (2026-11-03T18:21-05:00): the first tile re-ran under protocol v1.2 as a stage-1, gate-only measurement and ALL physical gates pass: a, b, c_v1_2, d_v1_2, e, f. c_v1_2 median is 11.67 class-2 points per m2 over 8776 non-empty cells of the 9494 expected (v1.1 historical 11.61 over 9351, non-gating; verbatim totals 9494/8776/718 holes retained); d_v1_2 counts 5 preliminary primaries across 4 sectors (s00, s02, s12, s22) under the unchanged section 6 method. One foreground invocation, 26.89 s, MAX RSS 827,440 kB under the frozen 1 GiB guard, exit 0 (receipt ~/log/track-r/lidar-v12-firsttile-execution-20261103T1820-0500/execution-receipt.md; local sha256 only, no publisher checksum). This is a physical-gate pass, not the numeric proof: numeric_proof_pass is FALSE, the final-six freeze is not done, and the remaining five tiles (701,880,508 bytes) are eligible but not downloaded.

LATEST (2026-11-03T21:31-05:00): the FINAL pre-DEM benchmark freeze is complete and independently audited. Canonical digest ea3e5f2e806b836c144dc33ffe6804dafcbad036abf240ffe7967aed589ea2f1: five primary sites across sectors s00, s02, and s22 plus two alternates. Counts: full 130,321; canonical 128,881; merged 127,449; the 1,432 missing cells are all E6 seam-ineligible with no eligibility loss (earlier AOI-boundary explanation withdrawn). Memory: RSS 524,456 KiB is the cumulative child high-water across the run, not six independent 512 MiB peaks. Producer chain: header original 68e... plus worker-only config 67c... consumer chain valid; the producer RE-ENTRY check fails (4) by design constraint, so the final workflow must consumer-reuse outputs and must never claim false producer idempotence. Numeric proof NOT RUN; no prepared MNT exists yet; the frozen science (five plus two selection, Budget A 1.0 m engineering limit, Budget B 0.0390625 game-unit quantum) is unchanged pending the actual proof. Evidence: ~/log/track-r/lidar-final-freeze-execution-20261103T2130/execution-receipt.md and execution-errata.md; independent audit ~/log/track-r/review-final-freeze-runtime-20261103T2230-0500/audit.md and addendum-arithmetic-and-e1-impact.md.

LATEST (2026-11-04T10:25-05:00): accepted source harness batch through 09b118bec (backend, wrapper, freeze repair, native proof harness). First real cached preparation attempt reached PRE-DEM and stopped at the PROJ cs2cs DMS-format defect (exit 2); the narrow fix is in progress and unreleased. All six tiles frozen, digest ea3e5f2e... unchanged; numeric proof not run; native Interactive case listed but untested; no proof PASS.

Numeric acceptance, operator decision of 2026-11-03 (fresh, made BEFORE any DEM comparison; not
the retired wall-marker accuracy-plus-one allowance): engineering acceptance only, never a
certified source accuracy. Budget A: abs(payloadElevationMeters - frozen LiDAR referenceMeters)
<= 1.0 m at EVERY frozen PRIMARY site across the required at least 3 sectors; all residuals and
failures are preserved verbatim, with no residual-site pruning and no failure-based alternate
activation (the 2 alternates are ranked pre-DEM, never a failure rescue). The generated manifest
encodes this as `tolerances.verticalMeters` 1.0 with `source.verticalAccuracyMeters` absent,
because the actual tile-specific RGE accuracy is unknown; the Core max(actual source accuracy,
explicit tolerance) contract `ab30059de` is unchanged, and a genuine documented actual accuracy
above 1.0 m must be escalated before any DEM comparison, never silently raising the operator
limit or fabricating an accuracy value. Carried unresolved and disclosed: the 2024
auto-classification bias and the 2012-2016 versus 2024 epoch mismatch stay non-quantifiable.
Budget B (engine encoding) is independent: abs(E - (P - C)) <= 0.0390625 game units, one quantum,
with no 1.0 m term added; restoredEngineMeters = Z0 + (E + C) x metersPerGameUnit /
verticalExaggeration, where Z0 is the ACTUAL header verticalOriginMeters, never an assumed zero.
The full six-tile final selection and freeze hash before any DEM height comparison remain
mandatory; the current first-tile preliminary 5 primaries across 4 sectors is not final. This
sets the numeric policy layer only: overall quality is not resolved, no proof PASS is claimed,
and the card stays `Ready` with physical gates holding execution only. Provenance:
`sedan-numeric-tolerance-20261103T1855.md`, sha256
`d4e25a8f94da4f3228ed22377bb738881be50f9e20342af10bf595c675d57cbc`.
