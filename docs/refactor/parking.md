# Parking: category review (rf-parking, 2026-10-16)

The user's grade: **D-**. "It does its job ... the trees go away ... the fences should be working perfectly ... it's kind of
boring." The brief: no new features; make it work and look tidy. Trees, bushes and lamps stay visible; fences perfect
(never across an entrance, no farm fences); no dips. Keep the styles that are really different, hide near-duplicates.

Runs (all under `C:\Users\jack\cs2-area-tool\.forensics\suite\runs\`):

| What | Run |
|---|---|
| BEFORE: every style, flat 150 x 140 block, plan + 45° + top + terrain (master `a71a842`) | `show-5-builds-1016-062605-103a` |
| it1: tidy lines, every style | `show-5-builds-1016-064107-d6ce` |
| it1: L 170 x 130 on a 7 % slope | `show-3-builds-1016-064133-a020` |
| it1: small 70 x 45 | `show-3-builds-1016-064134-ac9e` |
| it1: strip mall + mall district lots | `show-2-builds-1016-065534-5cbc` |
| it2: caps, fill off the lines, entrance tie | `show-4-builds-1016-075429-c23c` |
| it2: L on a 7 % slope (shows the entrance funnel, reverted) | `show-3-builds-1016-075449-973a` |
| it2: pinch 180 x 110 on hills | `show-2-builds-1016-075449-3979` |
| it2: strip mall + mall district lots | `show-2-builds-1016-075449-e540` |
| latest AFTER (commit `6c1e211`): every shown style, flat block | `show-4-builds-1016-075429-c23c` |

## What was wrong

The core refactor had already fixed the invisible trees (the vanilla farm "field" lot), the farm fences and the
lot-entrance dip. The BEFORE logs confirm it for parking: no vanilla lot, no fence object anywhere, "survivors: no object
hidden" (2-3 bushes hidden by the game and shown again), "pit check: no bowl deeper than 1.0 m", terrain -0.25 to 0 m.

What was still wrong was the **planting**: trees and bushes in clumps over the aisles and the stalls, gaps elsewhere.

* Cause 1: the spacing came from the catalog's plant bounds, which are the game's shrunk collision core (2.5 m for the
  London plane), so trees were spaced 4 m apart while their crowns are 7-10 m across.
* Cause 2: the fill was a jittered grid with value-noise clumping, anywhere in a 2.5 m median (trunks 0.15 m from the
  edge), then thinned at random by the density setting.

## What changed (only `Core/Layers/Parking/`)

* **Planting lines** (`Vegetation.PlanRows`, edges gathered in `PlanBuilder`). The strips behind stall rows (medians, the
  edge strip behind a perimeter row) are found by probing straight back from every stall's back corners while the ground
  is green; the middle of the strip is a planting point. Points of one strip become one straight line, split where a cross
  lane cuts it, and planted symmetrically: a tree on every k-th point (k from the crown and the density: one tree every
  3 stalls at the default 50 %), one species and one growth stage (adult) per line, shrubs between the trees and off the
  trunks. The row-end caps get a line of shrubs down their middle (no trees), and a free strip no stall faces (a strip
  between two lanes, in `compact`) gets its own line of trees and shrubs. Long lines are planted first and lines that meet
  never double up.
* **The old jittered fill** only adds to what is left (the corners of the lot, wide islands, the wedges of angled rows),
  keeps 4 m off any line plant, and its trees need 1.4 m of green round the trunk.
* **Density** sets the spacing of the lines (20 % wide gaps, 50 % a fifth of a crown apart, 100 % a closed canopy) instead
  of deleting random plants.
* **Spacing from the crown**: half the height x 0.56, as `DressingWorld.CrownOf`, at least 7 m between trees; shrubs 2-4 m
  from their own size and height.
* **No plant inside a stall** (a stall corner can reach into the planting on odd outlines; a shrub stood in one on an L).
* Speed: a 25 ha lot plans in about 3 s (the quality test's budget is 5 s); stalls and plants are looked up by grid cell.

Shared code: none. The commercial types plan their parking lots with this layer (`Mall.cs` ->
`GeneratorRegistry.Get("parking")`), so the strip mall's and mall district's lots get the same tidy lines (seen in the
runner, no new pit, no hidden objects). That changes the strip mall fingerprints
(`Tests/Placemaker.Tests/Fixtures/stripmall-fingerprints.txt`), regenerated on purpose with `PM_UPDATE_STRIPMALL=1`;
the integrator may have to regenerate them again after merging the Shops & squares branch.

Tried and **reverted**: tying the lot edge at a car entrance to its street wherever the street is (`LotGrading`). On the
runner's sloped land the street is ~10 m off the outline and the graded lot stood 2.6 m above it at the entrance, so the
connector climbed a bank in its first 6 m ("net sag ... humps 1.1 m", `show-3-builds-1016-064133-a020`). The tie fixed the
connector but pulled a 30 m half-bowl into the lot round the entrance (terrain map of `show-3-builds-1016-075449-973a`),
which reads as a dip. Back to master's grading: lots drawn up to the street (the normal case, the edge within 6 m of the
road) are tied to it as before.

## Types and styles

One type, `parking` (Make row: Parking), four styles.

| Style | What it is | Before | After | Keep / hide | Why |
|---|---|---|---|---|---|
| `standard` | 90° stalls, 2.5 m planted medians, cross lanes every 9 stalls, lane around the lot | D | B- | keep | the default and the reference lot: tree-lined medians and a tree avenue round the edge |
| `compact` | 90° stalls, planting only at the edges, no cross lanes: the most stalls (414 vs 341 on the test block) | D | B- | keep | clearly different: one big open field of stalls ringed by trees, for a stadium or a mall |
| `angled` | 60° stalls, medians | D- | C+ | keep | different geometry (slanted rows, wedge islands); the wedges still carry some loose shrubs |
| `green` | `standard` with 4 m medians, 80 % planting, cross lanes every 8 | D | (hidden) | **hide** | a near-duplicate of `standard` now that every style plants its medians in lines; the planting density slider gives the same leafy look |

`Recipes/shelf/parking.json`: `hiddenStyles: { "parking": ["green"] }`. The style keeps its code and still builds by id.

## Checks (logs, every run)

* Trees, bushes, lamps: "survivors" 0-3 objects hidden by the game per build, all shown again; the build audit leaves out
  1-3 bushes (declutter) where it left out 8-16 before.
* Fences: none in any parking build (no fence object, no vanilla lot).
* Dips: "pit check (after the build): no bowl deeper than 1.0 m" on every build, flat, L on a 7 % slope, pinch on hills;
  "net sag: none" except the known slope entrance below.

## Done

* Parking planting rebuilt in `Core/Layers/Parking/` only (commits `6c1e211`, `c7c85f0`, `d2b6d97`): trees and shrubs in
  straight lines down the medians and edge strips, spaced from the real crown, one species per line, row-end caps and
  free strips planted, no plant in a stall, density sets the spacing instead of deleting random plants.
* The strip mall and mall district lots get the same lines (fingerprints regenerated on purpose).
* Entrance-to-street tie tried and reverted (it made a funnel); grading is master's.
* Kept: `standard`, `compact`, `angled`. Hidden: `green` (near-duplicate of `standard`; the density slider gives its
  look), in `Recipes/shelf/parking.json`.
* Latest before/after: BEFORE `show-5-builds-1016-062605-103a`, AFTER `show-4-builds-1016-075429-c23c`. Pictures (under
  `.forensics\suite\runs\`): `<run>\shots\02-parking-standard.05-45-clean.png` and
  `<run>\shots\03-parking-compact.06-top-clean.png` in both runs.

## Not done / known problems

* The last commit `d2b6d97` (free-strip lines, tie revert) was never run in the game: the work was stopped while the
  shared game queue was backed up. It has no AFTER pictures; the next runner pass should build every parking style and
  the strip mall from this head. The grades above are from `075429-c23c`.
* On a slope, when the street is more than 6 m beyond the lot's edge (the runner's land, not a lot drawn up to the road),
  the entrance connector still climbs the bank between the graded lot and the street (as on master). A gentle ramp would
  need the lot's lowest terrace to step down toward the entrance without a funnel; not attempted further.
* The `angled` style's wedge islands still get a few loose shrubs from the jittered fill.
* Every build logs ~470-550 "OverlapExisting" temporaries (stall decals, bushes, perimeter trees) that the default "ignore
  collisions in my builds" keeps; with that option off they would be left out. Unchanged from master; not investigated.
* The strip mall fingerprints may need regenerating again after the Shops & squares branch merges.
* Lamps were only checked through the "survivors" log line, not looked at closely in the pictures.

---

# P2-1 Parking tools: parity with Parking Lot Tool (2026-10-31)

The creator's review: no management tools (fees turned off elsewhere), hard to change a placed lot (accessible stalls could
not be moved), entrances and alignment worse than Parking Lot Tool (PLT snaps to aisles, its preview is see-through), and
overlapping buildings handled worse. The user: lose nothing from PLT, user customization is key, pick angles and what the
grid lines up with, maybe draw on the world.

Source read: `C:\Users\jack\ParkingLotTool` v1.0.10 (full inventory with file:line refs in the session notes,
`p2-parking-plt-inventory.md`). Legend: **have** (Placemaker had it), **new** (added by P2-1), **partial**, **open** (not
done yet, why), **declined** (deliberately not, why).

## Audit table

| # | PLT feature | Placemaker | Status |
|---|---|---|---|
| **Drawing and shape** ||||
| 1 | Draw a polygon, click the first point to close | Points tool, block pick, paint bucket, paint area (`docs/AREA-TOOLS.md`) | have (more ways) |
| 2 | Drag a corner, drag an edge (parallel), Shift slides along neighbours, Ctrl+click inserts, Alt+drag extrudes, right-click deletes | `Core/Tool/PolygonEditor.cs` (ported from PLT's EdgeDrag) | have |
| 3 | Outline snapping (roads, building lots, other lots, zone grid, axis, guides), six toggles | `SnapResolver`/`SnapCollector`: kerbs, road nodes, other builds; no zone-grid or guide-line snap, no toggles | partial (open: zone-grid and guide snaps) |
| 4 | Undo/redo with named steps | one history for outline and design, named steps | have |
| 5 | Split area into sub-areas, a reference line per sub-area | one grid per lot; two lots side by side give two grids | declined for now: per-lot alignment covers the common case; sub-areas need a split editor (core request if wanted) |
| 6 | Large-lot warning | "too big" warning (`SizeGuide`) | have |
| **Layout** ||||
| 7 | Edge setback 0–6 m | `setback` | have |
| 8 | Row direction: Edge / Across / Fixed angle / Align to a picked line | Along the longest side / **Along the street** / Across / Custom angle, plus **Turn** 0°, 30°, 45°, 60°, 90°, free on top of the reference, plus the **Line up here** hand edit (pick any outline side: the rows run along it and start at it) | new (beats PLT: street, turn presets, rows start at the picked kerb) |
| 9 | Aisle width 3–12 m, cross road width/every/on-off | aisle width 4–10 m, cross lanes every N (0 = none); cross lane width fixed | have (cross lane width declined: the drivable invisible path is 3 m whatever the paint) |
| 10 | Median green on/off and depth, caps, perimeter roads on/off | greenery none/edges/medians, median depth, row-end planting, perimeter road | have |
| 11 | Stalls fixed 3.0 × 5.5 m, 90° only | stall width/length sliders; 90°, 60°, 45° stalls | have (more) |
| 12 | Accessible stalls: automatic block(s) near the footpath entrance | automatic share near the entrances, **plus hand edits**: click a stall to put the group there (it moves the automatic one); "Plain" keeps a spot free of automatic ones | new (the creator's "could not move accessible stalls") |
| 13 | EV pairs with a charger, 4 % | share slider, charger asset; **plus the Charger hand edit** | new |
| 14 | Result readout | summary: spaces, EV, accessible, area, aisle length, trees | have |
| 15 | Save as my default / reset per control | presets, Save as default, reset | have |
| 16 | Through traffic: internal paths at 25 km/h with a higher pathfind cost | vanilla invisible paths | open (needs cloned path prefabs with speed limit and pathfind cost: engine work, core request) |
| **Entrances** ||||
| 17 | Seven kinds + bus stop, max 15, build needs two-way or in + out | same seven kinds, max 15, same rule; bus stop hidden (the user's call) | have |
| 18 | Snap to the lot's own aisles within 5 m along the edge, oblique ≥ 30°, nearest line wins, Shift = freehand | **same rule** on the step's planned drive aisles (footpaths snap to paths), a dashed guide shows the aisle it joins, Shift = freehand | new |
| 19 | Drag a placed entrance; right-click the one under the cursor | **drag** (white ring on the one under the cursor); right-click removes the one under the cursor, else the last | new |
| 20 | Corner clearance, 13 m spacing, red when blocked | the planner merges or drops entrances that do not fit and says so | partial (open: red hover when too close) |
| 21 | LocalConnect paths, alleys split the road | same, plus a crosswalk option | have (more) |
| **Preview** ||||
| 22 | See-through fills at ~22 % alpha, outlines by role | site-plan fills at **50 % of their tone by default** (asphalt about 0.44), lines, stalls and entrances full strength; options slider **Plan preview fill** 10–100 %; plus the Real preview (the game's ghost) | new (adjustable) |
| 23 | Live background preview | preview worker, latest wins, planning progress | have |
| **Overlaps** ||||
| 24 | Trees/props under the lot hidden (Overridden), never deleted | removed inside the outline when "Clear existing trees and props" is on (default), kept otherwise; gardens of buildings untouched | have (different by design: nothing old poking through) |
| 25 | Buildings: no check at all | always kept, the panel says how many; **the car park now plans round them** (no stall or aisle on a building lot, the rest reachable, a note with how many spaces were left out) instead of the safety pass cutting aisles after planning | new (beats PLT) |
| **Editing after placement** ||||
| 26 | Click selects the whole lot; Edit in the info panel and the list | click panel "Part of …": Edit, Rename, Ungroup, Remove; Manage list | have |
| 27 | Edit restores everything, rebuilds, keeps the fee | Edit restores outline, design, entrances **and hand edits**; rebuild replaces; fee and name kept | have + new |
| 28 | Unchanged road pieces keep their entities on edit | full rebuild | open (matters for grown buildings on PLT's zoning roads, which Placemaker does not build) |
| 29 | Lost receipt restore from a build journal | receipts live on the group record in the save | declined |
| **Management** ||||
| 30 | Fee per lot on `ParkingLane.m_ParkingFee`, comfort 0.5 | same mechanism (`ParkingEconomySystem`) | have |
| 31 | Fee in the info panel: switch (off keeps the amount) + slider 1–50 | **same, on the click panel**, applies at once, no rebuild; the design's fee slider is now up front with 0 = free | new (the creator's main complaint) |
| 32 | Default fee for new lots | `DefaultParkingFee` option; the design's fee wins | have |
| 33 | Upkeep via a hidden companion building, 4 workers, noise | companion ported, experimental and off by default (it crashed natively during PLT's development) | partial (open: verify in the game, then default on) |
| 34 | Capacity and occupancy in the vanilla panel | spaces on the click panel | partial (open: occupancy) |
| 35 | Lot list: fee inline, filters, sort, insights | Manage list | partial (open: fee and occupancy columns; insights declined, out of scope for a placement tool) |
| 36 | Economy master switch, upkeep scale | `ParkingEconomy` switch, upkeep factor | have |
| 37 | Bulldoze confirmation | Remove asks; bulldoze question | have |
| **Objects** ||||
| 38 | Bay markings on/off | always marked; **Empty** hand edit for single spaces | partial (open for P2-5: a "no markings" look) |
| 39 | Road + decoration surface pickers | asphalt and green surface pickers | have |
| 40 | Surface to road at entrances | kerb apron at entrances | have |
| 41 | Lanterns: sets, spacing, light range display | lamp asset, spacing, on/off | have (light range display declined) |
| 42 | Vegetation: free/line, density, ages, sets | lines down medians, density, tree/bush pickers; **Island** hand edit | have |
| 43 | Arrows on one-way entrances | arrows | have |
| **Zoning and roads** ||||
| 44 | Zoning patches, outer bands, edge zoning, road sides, bus stops | frontage zoning (edge zoning, zone type, depth), road sides; patches and bus stops hidden | declined (the user, 22 Oct: "a bus stop or a building plot for a parking lot just don't make sense") |
| 45 | Auto power and water | none | declined (lot lamps are props that need no power; nothing in a lot uses water) |
| **Terrain** ||||
| 46 | No terraforming | optional grading into level tiers with ramps ≤ 8 % | have (more) |
| **Options, input, save** ||||
| 47 | Hotkey, Esc, Enter builds, Ctrl+Z/Y | own hotkeys, Esc, Enter, undo/redo | have |
| 48 | Sync/repair, orphan and missing-asset repair, uninstall cleanup | missing-asset repair, prepare uninstall, old saves migrate | have |
| 49 | Report a problem, dev tab | game runner, logs, build audits | declined (developer tooling) |
| **Drawing on the world** ||||
| 50 | (not in PLT) | hand edits draw on the world for single spaces (islands, empty spaces, accessible groups, chargers) and the grid's edge | partial (freehand aisle sketching: below) |

### Drawing aisles on the world (brief item 7)

Clicking stalls and edges (hand edits) is the clean part: the planner keeps a layout that works (every stall reachable,
entrances joined), and the player changes what they want. Freehand aisles would replace the band planner for that part of
the lot (a sketched aisle defines its rows, cross lanes and reachability) and must survive outline changes; the area tools
make only closed outlines (`PolygonEditor`), no open lines. Closest clean thing today: two lots side by side, each lined up
with its own picked edge, plus islands and empty spaces by hand. A sketch tool (open polylines snapped like the outline, kept
as hand edits of kind `aisle.line`) is proposed in `docs/refactor/PHASE2-CORE-REQUESTS.md`.
