# Phase 2: requests for shared core pieces

What a content agent needs from shared code that is bigger than a small general fix. The lead decides who builds each.

## P2-1 Parking tools (2026-10-31)

1. **Sketch lines on the world** (open polylines, snapped like the outline: kerbs, road nodes, the plan's own lines).
   - Why: the user's "letting them draw on the world": a player sketches where aisles, walkways or a row of islands go.
   - How it would fit: the area tools only make closed outlines (`Core/Tool/PolygonEditor.cs`); an open-line editor with the
     same snapping, saved as hand edits (`Core/Touches.cs`, a kind such as `aisle.line` with the points in `Arg`), so it
     survives Edit, rebuilds and reloads like every hand edit. Each layer decides what a line means (parking: an aisle with its
     two rows; parks: a path; plazas: a walkway).
   - Types: parking first, then parks, plazas, shops.
2. **Slow internal paths** (PLT: its lot paths are cloned invisible paths at 25 km/h with a higher pathfind cost, so through
   traffic does not cut across car parks).
   - Why: parity with Parking Lot Tool; traffic in big lots next to busy streets.
   - How it would fit: prefab clones registered before saves load (like the parking companion prefab), `RoadData`/speed and
     `PathfindCostInfo` set on the clone, `ParkingAssets.DrivePath` choosing the clone. Needs an in-game traffic check.
   - Types: parking, the malls' and shops' car parks, anything with drive aisles.
