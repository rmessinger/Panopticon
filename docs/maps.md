# Generals Maps

55 maps ship in `maps.big`. Sizes are world units: playable area first, total heightmap grid (including the unbuildable border) second. Data read with `MapFile.FromFileSystemEntry` via a scratch tool; re-run to regenerate.

## Skirmish and multiplayer

| Map | Playable | Total grid |
|---|---|---|
| Alpine Assault | 239x259 | 380x400 |
| Armored Fury | 355x355 | 496x496 |
| Bridge Busters | 189x189 | 330x330 |
| Cliff | 199x199 | 260x260 |
| Dark Mountain | 399x399 | 520x520 |
| Desert Fury | 249x249 | 390x390 |
| Fallen Empire | 279x249 | 400x370 |
| Final Crusade | 199x249 | 340x390 |
| Fortress Avalanche | 349x349 | 470x470 |
| Golden Oasis | 349x349 | 470x470 |
| Heartland Shield | 249x269 | 390x410 |
| Lone Eagle | 349x349 | 470x470 |
| Mountain Fox | 349x349 | 490x490 |
| Rogue Agent | 274x274 | 415x415 |
| Sand Serpent | 209x209 | 350x350 |
| Scorched Earth | 189x189 | 330x330 |
| Silent River | 319x299 | 460x440 |
| SwissMP | 199x239 | 340x380 |
| Tournament Desert | 199x249 | 270x320 |
| Tournament Island | 349x349 | 490x490 |
| Tournament Lake | 279x279 | 420x420 |
| Tournament Tundra | 249x249 | 390x390 |
| Twilight Flame (8 players, largest) | 449x449 | 590x590 |
| Wasteland Warlords | 249x199 | 390x340 |
| Whiteout | 349x349 | 470x470 |
| Woodcrest Circle | 199x249 | 340x390 |

## Campaign

| Map | Playable | Total grid |
|---|---|---|
| USA01 | 399x299 | 480x380 |
| USA02 | 249x249 | 350x350 |
| USA03 | 249x199 | 390x340 |
| USA04 | 219x279 | 360x420 |
| USA05 | 299x219 | 400x320 |
| USA06 | parse failure | unknown |
| USA07 | 249x249 | 390x390 |
| USA08 | 299x149 | 360x210 |
| GLA01 | 319x319 | 400x400 |
| GLA02 | 149x199 | 210x260 |
| GLA04 | 199x199 | 300x300 |
| GLA05 | 219x179 | 280x240 |
| GLA06 | 274x249 | 375x350 |
| GLA07 | 249x249 | 370x370 |
| GLA08 | 449x299 | 610x460 |
| CHI01 | 274x269 | 415x410 |
| CHI02 | 259x269 | 400x410 |
| CHI03 | 244x289 | 385x430 |
| CHI04 | 169x249 | 290x370 |
| CHI05 | 274x224 | 425x375 |
| CHI06 | 249x299 | 390x440 |
| CHI07 | 229x429 | 370x570 |
| CHI08 | 249x349 | 350x450 |
| CHIfinal | 229x429 | 370x570 |

(GLA03 is not shipped in `maps.big`.)

## Shell and misc

| Map | Playable | Total grid |
|---|---|---|
| ShellMap1 | 124x124 | 185x185 |
| ShellMap1Low | 124x124 | 185x185 |
| Homeland Alliance | parse failure | unknown |
| Training01 | parse failure | unknown |
| Winter Wolf | parse failure | unknown |

## Known parse failures

USA06 and Homeland Alliance fail with end-of-stream errors; Training01 and Winter Wolf fail in `PolygonTriggers` version 3 (12-byte offset mismatch). These are parser gaps, not corrupt data; the maps load in the original game.
