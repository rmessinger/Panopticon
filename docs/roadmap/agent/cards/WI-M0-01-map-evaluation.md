# Task Card `WI-M0-01`: Official map evaluation against the battle-map requirements

| Field | Value |
|---|---|
| Card ID | `WI-M0-01` |
| Roadmap phase | [M0](../track-m-mass-battle.md): battle map selection |
| Status | `Done` |
| Type | `investigation` |
| Effort | `S` |
| Prerequisites | none (P9 and A1 are merged) |
| Diagrams | none (read-only audit; grounds the M0 entries of deep dive section 9 if needed) |

## Goal

Evaluate all `.map` files in the install's `maps.big` against the [map requirements](../../deep-dives/mass-battle-1k.md#4-map), producing a per-candidate record of size, slots, waypoints, passability screening, load result, and provenance, without committing any map.

## Context to read (and only this)

- `docs/roadmap/deep-dives/mass-battle-1k.md` section 4: the map requirements and sourcing order.
- `docs/roadmap/track-m-mass-battle.md` M0 row: the milestone this feeds.
- `src/Panopticon.Tools.ReplaySketch.Core/Services/MapMetadataService.cs`: reference water-polygon semantics (`IsWater || IsRiver`, surface z from `Points[0].Z`).

## Method

Scratch probe (not committed), referencing current `src`:

- Enumerate `maps/**/*.map` through `GameInstallation.CreateFileSystem()`; `MapFile.FromFileSystemEntry` per map; failures recorded with exception.
- Slots = `ObjectsList` waypoint objects named `Player_<n>_Start` (`MPPositionList` is empty in all Generals maps).
- Water = `PolygonTriggers` where `IsWater || IsRiver` (>= 3 points); a cell is submerged when its center is inside the polygon and its height is at or below the polygon surface z.
- Dry flat components = 4-connected cells, elevations within a 2.5-unit band, submerged cells excluded; top two by area reported with cells, z, bounding box, centroid.
- Screening thresholds: a zone needs 9,216 cells (96x96, 1000 units at twice footprint); the two leading components need centroid separation >= 400 world units (est. longest weapon range).
- Provenance = `maps.big:<path>` for every record. Real per-locomotor reachability is deferred to M0-3 inside the engine.

## Result records

55 map files found; 51 parse, 4 fail. Ranked by the smaller of the top-2 dry components (zone bottleneck). `gap` = bounding-box gap, `ctr` = centroid separation, both in world units. Verdict: `cand` passes both screens; `fit`/`sep` names the failing screen.

| Map | Size | Slots | Wp/paths | Water | Sub% | Zone 1 | Zone 2 | gap | ctr | Verdict |
|---|---|---|---|---|---|---|---|---|---|---|
| Fortress Avalanche | 350x350 | 8 | 78/46 | 3 | 5.9 | 32183@z20 350x144 | 28107@z20 350x126 | 800 | 2413 | cand |
| Twilight Flame | 450x450 | 8 | 80/46 | 0 | 0 | 36281@z99 450x228 | 17320@z100 250x118 | 1040 | 2753 | cand |
| GLA08 | 450x300 | 0 | 386/232 | 0 | 0 | 30270@z62 251x193 | 20626@z125 304x131 | 0 | 1700 | cand |
| USA01 | 400x300 | 0 | 279/214 | 2 | 2.6 | 26191@z12 209x220 | 18269@z25 159x196 | 0 | 1626 | cand |
| CHI08 | 250x350 | 0 | 0/0 | 0 | 0 | 25509@z157 250x273 | 21232@z0 250x338 | 0 | 1128 | cand |
| CHIfinal | 230x430 | 0 | 0/0 | 0 | 0 | 21043@z12 230x133 | 10601@z154 158x151 | 210 | 1919 | cand |
| CHI07 | 230x430 | 0 | 279/12 | 0 | 0 | 20707@z12 230x132 | 10123@z154 158x150 | 220 | 1914 | cand |
| Wasteland Warlords | 250x200 | 2 | 73/73 | 1 | 1.5 | 19085@z58 250x200 | 12877@z13.8 199x139 | 0 | 681 | cand |
| USA08 | 300x150 | 0 | 325/185 | 1 | 3.2 | 18121@z10 172x150 | 12706@z11.9 126x150 | 20 | 1569 | cand |
| Golden Oasis | 350x350 | 4 | 58/43 | 5 | 6.9 | 12406@z24 177x249 | 11977@z62.5 312x120 | 0 | 1562 | cand |
| Mountain Fox | 350x350 | 0 | 31/17 | 1 | 6.4 | 33565@z31 350x350 | 9402@z46.9 81x144 | 0 | 1760 | fit |
| Whiteout | 350x350 | 8 | 400/392 | 1 | 1.8 | 67710@z20 350x350 | 21560@z41.2 211x204 | 0 | 165 | sep |
| USA04 | 220x280 | 0 | 483/325 | 2 | 15.3 | 15642@z69 184x225 | 9104@z12.5 162x172 | 0 | 1048 | fit |
| Sand Serpent | 210x210 | 2 | 20/12 | 1 | 17.0 | 12942@z52 190x169 | 9075@z51.2 103x190 | 0 | 1214 | fit |
| SwissMP | 200x240 | 0 | 80/76 | 3 | 3.7 | 10212@z51 68x240 | 8732@z48.1 152x124 | 0 | 1345 | fit |
| Scorched Earth | 190x190 | 0 | 14/14 | 3 | 0.6 | 9762@z62 96x190 | 8487@z63.1 80x190 | 140 | 1355 | fit |
| GLA07 | 250x250 | 0 | 828/828 | 2 | 0.4 | 12745@z68 155x250 | 8471@z65 123x98 | 0 | 1510 | fit |
| Fallen Empire | 280x250 | 0 | 12/12 | 1 | 1.2 | 11699@z21 280x122 | 8470@z60 131x88 | 0 | 1314 | fit |
| Silent River | 320x300 | 0 | 7/7 | 1 | 11.5 | 26524@z26 320x300 | 8392@z8.1 123x99 | 0 | 1506 | fit |
| CHI01 | 275x270 | 0 | 47/47 | 1 | 9.4 | 28698@z31 238x231 | 8377@z31.2 271x269 | 0 | 994 | fit |
| Armored Fury | 356x356 | 2 | 204/204 | 1 | 21.3 | 40206@z31 354x356 | 8295@z43.8 103x105 | 0 | 1194 | fit |
| USA05 | 300x220 | 0 | 0/0 | 1 | 7.2 | 29730@z19 199x220 | 8258@z68.1 201x114 | 0 | 981 | fit |
| USA07 | 250x250 | 0 | 64/64 | 0 | 0 | 19260@z44 166x250 | 7708@z43.8 69x182 | 150 | 1715 | fit |
| Woodcrest Circle | 200x250 | 0 | 6/6 | 1 | 8.9 | 12687@z33 117x211 | 7599@z30.6 126x116 | 0 | 1483 | fit |
| GLA06 | 275x250 | 0 | 305/305 | 1 | 42.6 | 20104@z11 172x250 | 7560@z7.5 133x139 | 0 | 1039 | fit |
| Heartland Shield | 250x270 | 0 | 13/13 | 1 | 4.7 | 9025@z94 107x120 | 7547@z47.5 144x209 | 0 | 685 | fit |
| Rogue Agent | 275x275 | 0 | 17/17 | 6 | 2.1 | 23805@z44 275x275 | 7476@z61.9 170x86 | 0 | 1161 | fit |
| Tournament Desert | 200x250 | 0 | 20/20 | 2 | 2.2 | 8885@z38 107x109 | 7382@z60 56x250 | 0 | 1061 | fit |
| Desert Fury | 250x250 | 0 | 17/17 | 1 | 0.5 | 7770@z62 226x198 | 6783@z31.2 109x116 | 0 | 1093 | fit |
| USA03 | 250x200 | 0 | 43/43 | 1 | 0.9 | 7123@z10 250x180 | 6153@z73.8 164x153 | 0 | 754 | fit |
| CHI06 | 250x300 | 0 | 352/352 | 2 | 7.9 | 6774@z114 102x177 | 5931@z85.6 171x81 | 0 | 1313 | fit |
| GLA04 | 200x200 | 0 | 301/301 | 2 | 9.0 | 16622@z32 161x200 | 5913@z60.6 80x130 | 0 | 1093 | fit |
| Final Crusade | 200x250 | 0 | 22/22 | 1 | 6.5 | 12047@z31 114x208 | 5538@z31.9 85x115 | 10 | 1594 | fit |
| GLA01 | 320x320 | 0 | 151/151 | 1 | 5.2 | 13467@z32 158x243 | 4971@z1.9 92x238 | 220 | 1747 | fit |
| USA02 | 250x250 | 0 | 263/263 | 1 | 0.0 | 44998@z19 250x250 | 4869@z69.4 73x89 | 0 | 1127 | fit |
| Tournament Lake | 280x280 | 0 | 36/36 | 1 | 2.8 | 47065@z6 280x280 | 4525@z143.8 116x95 | 0 | 120 | fit+sep |
| CHI03 | 245x290 | 0 | 445/445 | 1 | 9.9 | 7548@z159 70x197 | 4429@z159.4 155x43 | 539 | 2201 | fit |
| Bridge Busters | 190x190 | 0 | 11/11 | 1 | 19.1 | 5922@z49 98x178 | 4071@z50 85x75 | 70 | 1228 | fit |
| CHI04 | 170x250 | 0 | 169/169 | 4 | 10.7 | 3758@z12 137x103 | 3671@z10 101x101 | 40 | 1176 | fit |
| GLA05 | 220x180 | 0 | 104/104 | 0 | 0 | 12307@z11 158x180 | 3653@z93.1 43x158 | 120 | 1217 | fit |
| GLA02 | 150x200 | 0 | 961/961 | 1 | 3.8 | 14734@z10 150x200 | 3233@z42.5 109x51 | 0 | 869 | fit |
| ShellMap1 | 125x125 | 0 | 56/56 | 0 | 0 | 4771@z11 81x80 | 3163@z0 125x125 | 0 | 635 | fit |
| ShellMap1Low | 125x125 | 0 | 56/56 | 0 | 0 | 4771@z11 81x80 | 3163@z0 125x125 | 0 | 635 | fit |
| CHI02 | 260x270 | 0 | 462/462 | 3 | 47.4 | 30463@z19 247x208 | 3154@z60 88x80 | 0 | 1100 | fit |
| Dark Mountain | 400x400 | 2 | 20/20 | 1 | 52.4 | 47247@z8 296x293 | 1924@z35 35x85 | 0 | 1265 | fit |
| Alpine Assault | 240x260 | 0 | 243/243 | 0 | 0 | 28561@z21 240x260 | 1418@z55.6 65x59 | 0 | 1178 | fit |
| Cliff | 200x200 | 0 | 27/27 | 0 | 0 | 12344@z29 200x170 | 1307@z33.8 84x73 | 0 | 703 | fit |
| Tournament Tundra | 250x250 | 0 | 163/163 | 1 | 0 | 46669@z40 250x250 | 1228@z68.1 50x31 | 0 | 618 | fit |
| Tournament Island | 350x350 | 0 | 158/158 | 1 | 46.8 | 52392@z8 278x279 | 1155@z11.2 69x67 | 0 | 1098 | fit |
| Lone Eagle | 350x350 | 0 | 195/195 | 3 | 6.5 | 70299@z14 323x316 | 457@z11.2 36x19 | 0 | 1386 | fit |
| CHI05 | 275x225 | 0 | 215/215 | 1 | 8.9 | 49431@z31 275x225 | 146@z25.6 38x29 | 0 | 1524 | fit |
| Homeland Alliance | - | - | - | - | - | - | - | - | - | load: EndOfStreamException |
| Training01 | - | - | - | - | - | - | - | - | - | load: PolygonTriggers v3 position mismatch |
| USA06 | - | - | - | - | - | - | - | - | - | load: EndOfStreamException |
| Winter Wolf | - | - | - | - | - | - | - | - | - | load: PolygonTriggers v3 position mismatch |

All provenance `maps.big:maps/<name>/<name>.map`.

## Reading of the records

- Twilight Flame (450x450, 8 slots, no water) and Fortress Avalanche (350x350, 8 slots) pass every screen with clear margins: two large dry flat regions each, 1040 and 800 world units apart. Twilight Flame offers the most total space for airfields behind the zones and airspace; Fortress Avalanche is the symmetric layout (two full-width z20 bands split by a ridge).
- Mission maps (GLA08, USA01, CHI07/08/final) pass the geometry screens but have no player-start waypoints; zones are scenario data anyway, so they stay listed as fallbacks.
- Whiteout fails separation (its two components are terraces of one plateau, centroids 165 apart). Dark Mountain and Tournament Island are majority-submerged islands after water masking; Lone Eagle, Armored Fury, and the other top playable-area maps lose their second zone once water and the 2.5-unit band are applied.
- The 4 load failures are pre-existing parser gaps (2x EndOfStream, 2x PolygonTriggers v3), out of scope here.

## Files likely to change

- None (read-only investigation). No map is committed.

## Acceptance criteria

- [x] Per-candidate record of size, slots, waypoints, passability screening, load result, and provenance for every map file found.
- [x] No map committed.
- [x] Recommendation recorded; final map selection and headless/windowed load proof land with M0-3.
- [x] Read-only; no standard validation applicable.

## Validation commands

Read-only; probe invocation documented under Method.

## Out of scope / guardrails

- No map edits, no deployment-zone scenario data, no in-engine reachability proof (M0-3).
- No community-map ingestion: the deep dive's sourcing order prefers operator-supplied community maps; this official batch is the baseline. The operator may supply community candidates to extend this record before M0-3 locks zones.
- Parser fixes for the 4 failing maps belong to a separate card if those maps ever matter.

## On completion

- Status set to `Done`; M0 phase status set to `In progress`; `.exchange` line appended.
- Follow-ups: `WI-M0-03` deployment zones + reachability (next); `WI-M0-02` authored fallback only if the operator rejects all candidates.
