# Phase 2: requests for shared core pieces

What a content agent needs from shared code that is bigger than a small general fix. The lead decides who builds each.

## P2-1 Parking tools (2026-10-31)

All three were built by P2-1 itself on the lead's word (2026-10-31): sketch lines (`Core/Tool/LineSketch.cs`, docs/AREA-TOOLS.md),
slow drive paths (`Game/Assets/SlowPathPrefabSystem.cs`), the old build hidden during Edit
(`Game/Tool/PlacemakerToolSystem.EditHide.cs`). Kept below for the record.

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
3. **Hide the old build while editing it** (PLT hides the visible parts of the lot being edited; nets stay live; Cancel
   shows them again).
   - Why: during Edit the old car park shows under the new preview (game-runner run `send-parking-touches-save-1031-001049`,
     picture edit-plan-touched): two layouts on top of each other, hard to read.
   - How it would fit: `GroupRegistrySystem` marks the group's objects and areas `Hidden` on Edit (not nets: traffic keeps
     flowing) and un-hides them on Cancel; the rebuild deletes them anyway.
   - Types: every type (Edit is shared).
