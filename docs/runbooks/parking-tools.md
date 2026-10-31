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
| Sketch lines | Hand edits "Draw aisle", "Draw walkway" | Click points (snapped; Shift free), right-click or the last point finishes, Esc drops the line, a click on a line removes it. A drive lane (with a row of stalls on each side where there is room) or a footpath through the rows; stalls in the way go. |
| Zone grid | Row direction "Zone grid" | Rows follow the zoning blocks of the streets round the lot (then the grid turn); outline corners snap to the zone grid as firmly as to roads, up to 64 m from the nearest zoned cell. |
| Space markings | Spaces, "Space markings" (More) | Off: invisible stalls (cars still park), no chargers. |
| Slow lanes | Automatic | Every drive path is a 25 km/h copy that costs more to drive through: through traffic keeps to the streets (PLT values). |
| Occupancy | Info panel, Manage | "x of y spaces taken"; Manage with nothing picked lists every car park with its fee. |
| Edit | Shared | The old build's objects and areas hide while editing (nets stay); Cancel shows them. |
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

- Looks (P2-5): walkways are paved like the aisles (they need a look of their own: pavers, paint); sparse median trees.
- Rows along a drawn aisle are always square stalls (90 degrees), whatever the stall angle.
- A bucket-filled outline follows the streets, not the zone grid (its corners are where the streets are).
- Through traffic was not watched with real traffic (the test world has none); the lanes carry PLT's speed and cost.
