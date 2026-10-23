# The area tools (area overhaul, 2026-10-17)

## What the user asked

> Points is pretty much excellent. Paint bucket gets like a D minus ... it needs more settings. I want like a threshold.
> If an opening is 10 meters across, it should still paint bucket and consider that closed ... it doesn't always avoid
> zones correctly ... in corners it doesn't get to the full corner ... it goes inside tiny little cracks too ... it goes
> onto roads even though I have avoid roads on ... things cut into it that don't make any sense ... paint area has bugs
> too: cul-de-sacs it doesn't work for ... it should have a threshold too ... points should just snap to road nodes, so we
> can get rid of the road nodes one ... points should have all that avoid stuff as well ... paint bucket just has a
> border radius too ... if I paint bucket in an empty flat area it would do that second radius ... painting is a little
> blocky ... things turn red when I move my mouse near them ... the legend could handle that ... lag, of course.

## How it works now

Three tools: **Points**, **Paint bucket**, **Paint area** (Road nodes is folded into Points; a saved `perimeter` mode
opens Points). They share one set of **Stops at** (`Core/Geometry/BrushRules`): roads, paths, bike paths, rail & tram,
water, buildings, lots, my builds, and zoning as one choice (ignore / built / zoned / whole grid; default zoned).

* **Walls** (`Game/Selection/AreaWalls`): road, path and track *surfaces* from the game's own geometry (each edge's
  `EdgeGeometry` and its two junction ends `Start/EndNodeGeometry`, cut like `Game.Zones.CellBlockJobs`), so junction
  corners, roundabouts and turning circles are exact; buildings, lots, zone cells, my builds (`BlockObstacles`); water.
* **The fill** (`Core/Geometry/AreaFill`, `WallField`, `CellTrace`): the walls drawn into a grid around the click, the
  distance to the nearest wall per cell; the click's *core* (farther than half the **Gap** from every wall), the
  *body* within half a gap of it, corners kept where they end within about a gap, cracks and passages left out, the
  **Reach** as a disc. The grid only picks the piece: the outline is the grid region minus the wall polygons, exactly.
  Tests: `Tests/Placemaker.Tests/GeometryAreaFillTests.cs`.
* **Paint bucket** (`PlacemakerToolSystem.Block`): the fill under the cursor, worked out on a worker thread after a
  click (`Game/Selection/AreaFillRunner`; not on hover since 2026-10-21: lag spikes) and taken when it is back; Shift+click adds a fill next to it (the street between two is taken
  along), right-click takes back the last one (not the only one once designing). Outlines along curbs for most of their length are `AreaSource.Block` (no perimeter
  road, junction anchors), like the old block pick.
* **Paint area** (`PlacemakerToolSystem.BlockPaint`): with One block on, the brush is fenced to the fill where the
  stroke starts; off, to a 600 m box with every stop fenced out. 2 m cells (were 4). A stroke waits a few frames for its fill and fence
  (both on a worker; the mouse path is painted when it starts); the box grows and the fence is re-read in the background.
* **Points** (`PlacemakerToolSystem.Points`): corners snap to road nodes over a junction or a dead end; two in a row
  follow the road between them; the outline is cut by the stops (biggest piece kept, the rest shaded grey).
* **Legend** while choosing the area (`Core/Tool/AreaLegend`, `state.areaLegend`): the fill, closed gaps (amber), the
  reach, the stops (red), what Points left out, corners on road nodes.

## Testing it in the game

`suite/scenarios/area-tools.json`: dead ends, paths, a street that stops short of another (gap), open land (reach),
Points on road nodes, a zoned block. Land can carry extra roads and paths (`land.nets`), and the `hover` step rests
the cursor so a picture shows what the tool shows under it (no fill: the tools fill on a click). `suite/scenarios/area-lag.json`:
the worst frame of bucket hovers and clicks and of Paint area strokes (`suite.log`).

```
powershell -File tools/suite/Run-Suite.ps1 -Set suite/scenarios/area-tools.json -Who area-tools
```
