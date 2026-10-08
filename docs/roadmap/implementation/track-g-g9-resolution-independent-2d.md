# G9 Implementation: Resolution-Independent 2D

> Audience: agent. Card sequence and evidence for [G9](../deep-dives/g9-resolution-independent-2d.md).

## Scope

Preserve G1's pixel-space proportional layout; improve text/art sampling and replace HUD chrome.
Source of truth: [design and diagrams](../deep-dives/g9-resolution-independent-2d.md#5-architecture-diagrams).

## Card sequence

| Card | Deliverable | Depends on |
|---|---|---|
| WI-G9-01 | Draw/font/hit-region table and chrome-role manifest | none |
| WI-G9-02 | Native-pixel text/borders and role-aware sampling | WI-G9-01, WI-G1-04, WI-G2-03, WI-G3-03 |
| WI-G9-03 | Dynamic chrome callbacks and state-complete captures | WI-G9-02 |

## Audit evidence to complete

Classify chrome, portraits, minimap, content icons and APT movie roles. Record all faction/state
callbacks, font-cache keys and hit rectangles. Identify exactly which legacy image draws are
forbidden under DynamicHud. No additional DPI transform is required.

## Validation and risks

Scale-1/2 and widescreen bounds tests preserve input and layout. Enhanced draws no legacy chrome
bitmaps; raster content remains allowed. Legacy is exact. Check all button states and faction
variants. ReplaySketch UI changes require screenshot-mode verification of map and model tabs.
