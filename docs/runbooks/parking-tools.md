# Runbook: parking tools (P2-1)

What the player can do with a car park beyond picking a style, how it is built, and how to test it. The audit against
Parking Lot Tool is in `docs/refactor/parking.md` ("P2-1 audit"). The general mechanism (hand edits) is in
`docs/ARCHITECTURE.md`, "Manual touches".

## Status

- Branch: `claude/p2-parking-tools-fc16d7`. Not in the user's game.
- Proven in the game runner:
  - `send-parking-touches-save-1031-001049`: four hand edits clicked on the map, built, Edit, two more, rebuilt. The
    receipt kept 4 then 5.
  - `send-parking-touches-load-1031-003505`: a fresh game loads that city and the group record still has 5 hand edits.
    Player.log has 0 exceptions.

## What the player has

| Tool | Where | How it works |
|---|---|---|
| Hand edits | Parking step, "Hand edits" (Essential) | Pick a kind, click a space on the map; click again to take it off; right-click takes back the last; Esc ends. Kinds: accessible, charger, plain, empty, island, "rows line up here" (an outline edge). |
| Row direction | "Row direction": longest side / across / fixed angle / **street** | Street lines the rows up with the nearest city road. |
| Grid turn | "Turn the grid" 0/30/45/60/90/free (+ slider) | Rotates the rows from the chosen direction; hidden for a fixed angle. |
| Picked edge | Hand edit "rows line up here" | Wins over every direction; the first band starts at that edge. |
| Entrances | Marker mode | Snap to an aisle end (5 m along the edge, at least 30°), dashed guide, Shift = free; drag to move, right-click removes; red and refused when closer than PLT's spacing (aisle + 2 stalls) to another car entrance. |
| Fee | Manage → group → "Parking fee" switch + slider 1–50 | Off keeps the lot free (the economy system no longer writes a fee back). |
| Preview strength | Settings, "Preview fill strength" 10–100 % | Makes the ground fills see-through. |
| Existing buildings | Automatic | Stalls and lines that would touch a protected building lot are removed (0.5 m margin); the plan shows an info line. |

## Code map

- `Core/Touches.cs`: the data (`HandTouch`, `TouchTarget`), toggle rules, the "could not be placed" report.
- `Core/Layers/Parking/StallTouches.cs`: applies the stall touches (island/empty/plain, then accessible, then chargers).
- `Core/Layers/Parking/LayoutEngine.cs`: `SetupFrame` (picked edge, street, grid turn), `AvoidBuildings`.
- `Core/Layers/Parking/PlanBuilder.cs`: `EmitTouchTargets` (what can be clicked), feeds roads and buildings in.
- `Game/Tool/PlacemakerToolSystem.Touches.cs`: hover and click. `...Markers.cs`: entrance snapping, drag, spacing.
- `Game/Overlay/PlacemakerOverlaySystem.cs`: `DrawTouches`, the guides.
- `Core/Build/ReceiptCodec.cs` v3: the touches block (saved with the build).

Traps found:
- A plan layer's `StepPath` carries the region (`"0@root"`). Always compare it with `Touches.StepOf()`; the first run
  failed every touch on this.
- Gameface has no flex `gap`; the mock lint fails on it.

## How to test

```
powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/parking-touches-save.json -Who <you>
powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/parking-touches-load.json -Fresh -Who <you>
powershell -File tools/suite/Show.ps1 -Category parking -Type parking -Style standard -Site survive -Views top,45 -Who <you>
dotnet test Tests/Placemaker.Tests --filter "FullyQualifiedName~ParkingTouch|FullyQualifiedName~ParkingTests"
```

The suite step `{"do":"touch","kind":"stall.ev","pick":"far|near|middle|touched","index":n}` clicks a target like a
player; `{"do":"touch","kind":"grid.edge","side":"west"}` picks an edge. Read "N hand edits" in the `groupRecords` lines.

## Open (for P2-5 and the lead)

- Through traffic: PLT's slow internal paths (core request 2).
- Old build visible under the Edit preview (core request 3).
- Sketching aisles and walkways on the world (core request 1).
- Occupancy and a fee column in Manage; zone-grid and guide snaps for the rows.
- Looks (P2-5): the rebuilt vertical layout left a planted strip on the east and sparse median trees; a bay-markings
  toggle.
