# Sketch view study (25 Oct)

The user: the sketch "doesn't really tell me what the build is going to look like", the legend doesn't line up with
what is drawn, and Real is hard to read too. Research and mock-ups first; nothing shipped yet.

Report with pictures and live mock-ups: https://claude.ai/artifact/29Qhnnr3AxnZdqLa9Yomv5 (private to the user).
`compare-parking.png`: today's sketch, the proposed sketch (mock-up), the built car park.

## Seen in the game

Runs `send-sketch-research-1025-153259` (sketch and built) and `send-sketch-research-ghost-1025-153844` (Real, panel
with legend), from `suite/scenarios/sketch-research*.json`: parking standard, park natural, strip mall classic on a
grid block and 300 x 240 m land; roads suburb on 360 x 300 and 700 x 520 m open land. The preview switch is sticky
per session, so the second file sets Real explicitly before its pictures.

- Car parks and strip malls read as green fields: the asphalt tint (dark grey at low alpha) disappears on grass;
  aisles are blue dashed centre lines. Roads and paths are dashed centre lines in role colours, not surfaces; the
  perimeter road is a faint orange hairline; cul-de-sac caps are lilac squares. Trees are faint rings with a dot.
- Parks: the ground-paint preview draws as a chessboard of tinted squares over the whole park.
- One colour, several meanings: orange (features, terrain fill, warnings), yellow (paths, entrances, lamps), blue
  (aisles, commercial plots, outline, parking swatch).
- Widths scale with camera distance (`LineWidth()`), not real size.
- Legend: flat bars while the map draws dashes and rings; objects are drawn in per-category colours under one
  lilac "Benches, lamps" swatch; bays drawn white under a blue "Parking" swatch; park "Surfaces 2.5 ha" on a 1.56 ha
  site (overlapping surface pieces summed in `PreviewLegend.Compute`); "Paths 442 m" in the legend vs "448 m paths"
  in the panel's chips.
- Real: parks read well; car parks show no asphalt and a cyan web of surface outlines (the game draws Temp areas as
  outlines only); new roads are faint grey dashes.

## ParkingLotTool (C:\Users\jack\ParkingLotTool, 1.0.10; installed 1.0.12)

`Tools/ParkingLotOverlay.cs`, `ParkingLotOverlay.Draw.cs`, `ParkingLotFlaechennetz.cs`, `ParkingLotPreviewStyle.cs`:
fills the merged surfaces that are built (not overlapping plan pieces) with its own mesh and the game's
`m_SolidObjectMaterial` (`DrawMeshInstancedIndirect`, linear colour); one fill per material, all at 22 %; no road
bands at all; bays as 0.3 m painted lines per row, open to the aisle, none when bay markings are off; plants as
instanced discs (tree 2 m, shrub 0.9 m); every size in metres.

## Mock-ups

`tools/sketch-mockups/render.js` draws a plan as SVG in two styles (site plan: real colours, fills only; line
drawing: dimmed area, white linework). Plans come from the gallery:

```
dotnet run -c Release --project Tests/Placemaker.Gallery -- --out <dir> --only parking,park,stripMall --presets standard,natural,classic --sites square --no-features --areas 1.7,4 --plan-json
node tools/sketch-mockups/render.js <dir>/parking/square-preset-standard-1.7ha.plan.json site > parking.svg
node tools/sketch-mockups/png.js parking.svg
```
