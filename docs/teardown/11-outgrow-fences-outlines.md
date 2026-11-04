# Teardown 11: Outgrow fence gaps, crossovers and flickering outlines

The user, 21 Oct (four screenshots): with Outgrow on, fences have gaps along roads and turning circles and at corners, hedges
of two gardens cross each other, and "all blue outlines flicker when you pan and hover over something", since a trick that tried
to hide the blue outline. Measured in the game runner and offline; **[P]** = proven by a measurement below.

## 1. What was wrong

* **[P] Endless re-plans (the flicker).** In the user's city Brecon, a garden's plan could lie over a neighbour's lot (on a curve
  the middle line between two side neighbours ran 92 m² into the next lot). Every pass that saw it planned that garden again
  (`GardenClashes`), which made the same garden again, for ever: 620 re-plans in 150 s with the city paused, about 45 fence and
  hedge pieces deleted and made again every second. A hovered (lit) house's pieces kept being replaced, so its outline jumped
  (runner: 20 jumps and 27 of 240 frames under half the outline, panning over house 48800).
* **[P] The Real-preview outline trick never hid anything.** It left the Outline layer out of the camera, but the game's outline
  pass asks for that layer itself (`OutlinesWorldUIPass.AggregateCullingParameters`), so HDRP culled the scene a second time
  every frame for it (`CustomPassVolume.Cull`) and still drew every ghost tree and bush in blue and red.
* **[P] Gardens over each other and over houses (the crossovers).** (a) The plan's own gardens could cover another house's lot
  (above). (b) A re-plan only looks 160–400 m around, but a garden can reach 500 m: two big gardens planned in different passes
  lay over each other, and a garden grew straight over houses outside its pass ("houses are the plan's own", never obstacles).
  Fences of one garden then stood inside the other; the game hid pieces standing in another lot (holes). Runner, every reach at
  500 m: 17 gardens over each other, 281 fences inside another garden, 134 Outgrow pieces hidden.
* **[P] Gaps in fence runs.** Each garden edge was closed alone: one piece centred in its open stretch, 20 cm short of both ends
  (a notch at every corner); edges under 1.5 m and stretches under 2 m left out (a curve's or turning circle's short edges were
  gaps); a piece whose middle touched a driveway or road left out whole (a 4 m gap); a run that met another fence offset by up to
  0.9 m stopped there (a step); a line that crossed another was cut 1.5 m short of it.

## 2. What changed

* `YardStitch.Separate`: no garden a plan writes lies over another house's lot or another of its gardens.
* `NeighbourPolishSystem`: a pass also includes (fixed) every house whose saved garden reaches into it, keeps the lots of houses
  it does not have off limits (barriers), keeps simultaneous passes apart by their reach, and never plans one garden again for
  lying over a lot more than three times a minute (`ClashAgain`).
* `YardStitch.CloseRuns` / `LayRun` / `JoinEnds`: closing pieces laid as continuous runs across the plot's corners (pieces end at
  corners, follow bends chord by chord, stop at driveways and houses, end on the fence they meet, finish with a 2 m piece where the
  kind has one), then loose fence ends close together along a garden edge joined. `Dedupe`: a line that crossed another ends on
  it (a T). Knobs `separate`, `closeRuns`, `joinEnds`, `dedupeT` (1 by default) switch them off for a side-by-side check.
* `ShowHiddenPieces`: a garden's own overlapping run pieces never count as a doubled fence. 2 m pieces share the garden's look.
* Real preview: the game's outline pass draws no layer while it shows (`OutlinesWorldUIPass.m_OutlineLayer`), put back after.

## 3. Measurements

| | before | after |
|---|---|---|
| Brecon, lit house panned 240 frames: outline jumps / frames under half | 20 / 27 | 0 / 0 |
| Brecon, 10 s after settling: pieces deleted / made | 477 / 432 | 0 / 0 |
| Brecon, Outgrow busy after the load | > 150 s (never settled) | 3 s |
| Brecon, every outgrown building re-planned (613): gaps / crossings / fences inside a garden / gardens over each other | 354 / 5 / 14 / 8 (as saved) | 188 / 4 / 0 / 0 |
| Real preview: game outlines on ghost trees and bushes | all | none |
| Offline replay, Brecon passes (59): gaps / crossings / gardens over a lot | 116 / 0 / 133 | 0 / 0 / 0 |
| Offline replay, test town passes (45 + 43 + 39): gaps / crossings | ? / 0, 80 / 0, 53 / 0 | 2 / 0, 3 / 0, 2 / 0 |
| Runner, test town at 500 m reach, first fixes only: gaps / overlaps / fences inside a garden / pieces hidden | 26 / 17 / 281 / 134 | 2 / 0 / 0 / 0 |
| Runner, test town at 500 m reach, final, old layout → new layout (same game) | 5 / 1 / 5 / 3 | 0 / 0 / 0 / 0 |

Gaps between a house's own fence lanes (its gate, its driveway's opening: always 3.41 m or 7.3 m) are counted apart. Most of
the 188 left in Brecon are openings by design the check still counts (street fronts at corners, shop-yard gates, fence ends
against a house's side buildings). Left to do: a few short zig-zag pieces where a garden's edge follows a cut-out lot corner
at a turning circle; existing cities keep the fences an older build saved until their buildings are planned again (the
options' Redo outgrown buildings).

## 4. Zoning under a car park never grew (22 Oct)

The user: "I'm zoning new areas inside of outgrown areas, and they don't spawn in". **[P]** Every zoned cell under an Outgrow
car park was taken: the stall marks (`ParkingLotDecal01`) have a parking lane of their own, so the game gives the prefab
`OccupyZone` instead of `Overridable` (`ObjectInitializeSystem`, objects with sub-lanes), and `CellOccupyJobs` marks every cell
under it taken. Fences, hedges, trees and props are overridable: a new building hides them. Not a regression of this round:
car parks have had stall marks since 20 Oct. Fix: a piece whose prefab takes zone cells is laid with the game's negative
`Elevation` (−0.01 m; `CellOccupyJobs` skips objects below the ground, the mark is drawn the same), and a loaded city's are
sunk once after the load (`NeighbourPolishSystem.SinkOldPieces`). A building that grows there is planned around like any
other: the car park shrinks back behind it.

| runner (`outgrow-zone-inside.json`), 108 cells zoned inside shop car parks | free | houses grown |
|---|---|---|
| before | 0 (108 under `ParkingLotDecal01`) | 0 (9 on the same zoning outside) |
| after, car parks laid now | 108 | 9 |
| after, car parks laid the old way, saved and loaded (427 marks sunk) | 108 | 9 |

Car parks also reach in front of their lots now where the street curves away (`frontReach`, 15 m, car parks and paved yards
only: gardens' side fences carried forward met the neighbours' with gaps, 18 → 32 in a replay).

## 5. Grass triangles where back gardens meet a street at an angle (22 Oct)

The user, a row of long back gardens running down to a second street at about 40°: "WHERE DO THESE COME FROM?", a triangle of
plain grass between every back fence and the sidewalk, the back fences stepping in a sawtooth. **[P]** The gardens did reach
the street (the back hugs it, `Hug`, e 2: a line along the kerb). Then the no-spikes rule (`NarrowsAt`, made for the inside of
a curve, where a garden's sides close in behind it) measured each garden's width across its depth: a garden ending along a
slanted street gets narrower toward that end, under half its lot's width before it reaches the far corner, and was cut square
there. The part past the cut was nobody's: one triangle per lot. Fix: the spike test measures the garden as if the street it
backs onto were not there (that hug line, and a free side hugging the same street: a row's end lot meets it twice), no further
back than the garden really reaches; its sides still count, so the inside of a curve keeps its rule. Knob `streetEnd` (1; 0 is
the old cut, for comparisons). Test: `NeighbourStitchTests.Back_gardens_reach_a_street_behind_them_at_an_angle_...` (20°,
30°, 40°: the kerb within 1 m of a garden all along, fences along it, no gaps or crossings). Runner: `outgrow-back-street.json`
(houses on one street, a second at 25° laid behind them after the growth):

| runner | right after the back street: the polish says | `streetEnd` 0 / 1 in the same town |
|---|---|---|
| before the fix (`send-outgrow-back-street-1022-151925`) | "3 gardens end where they get narrow" (3 passes) | a grass triangle at every lot's back corner, both |
| after (`send-outgrow-back-street-1022-152503`) | no garden ends narrow | 0: three triangles; 1: fences along the sidewalk all the way, none |

Fence map, 0 → 1: 0 gaps, 0 crossings, 0 gardens over each other both times; doubles 8 → 5.

Then the user, on that picture: two hedges side by side from the houses down to the back street, a grass strip between ("who
would want two fences running parallel to each other?"). A walkway (`alleys`, 45% of the neighbours with an even 3–9 m gap):
each garden kept to its own lot line all the way back, the paved walkway only reaching the shallower lot's back. Now the walkway
runs from the street to the shallower lot's back and stops there; behind it the two gardens grow into the strip and meet on its
middle line (`walkwayMeet`, 1; 0 the old way): a fence across the walkway's end, one down the middle, the walkway's own fences
end with it. Test: `NeighbourStitchTests.Behind_a_walkway_two_gardens_meet_on_one_fence_...` (equal and different depths). **Not
seen in the game yet**: the runner's towns (`send-outgrow-back-street-1022-155634`) grew no walkway; the scenario needs a way to
leave an 8 m gap between lots (a dezone strip) to show one every time.

## 6. Tools

* `{"do":"lab","op":"fenceMap"}` (`Game/Suite/SuiteLab.FenceMap.cs`): every fence and hedge, garden, road and building near the
  build (or `"street"`, or `"box": "gardens"` in a loaded city); gaps, crossings, doubles, fences inside a garden, gardens over
  each other; close-ups of the worst spots, of where gardens meet (`junctions`) and of edges along roads (`edges`), the same spots
  again in a later map (`spotsFrom`). `node tools/suite/FenceMap.js <fencemap.json> [--at x,z --size m]` draws it.
* `{"do":"lab","op":"outlineFlicker"}` (`SuiteLab.OutlineFlicker.cs`): a house lit as hovered, the camera panned every frame, each
  frame's outline pixels counted.
* `Tests/Placemaker.Tests/FenceContinuity.cs`: the same checks on a plan; `FenceContinuityReplay` replays a run's
  `polishPassDump` passes (`PM_FENCE_PASSES`, `PM_POLISH_KNOBS`, `PM_FENCE_AT`, `PM_FENCE_PROBE`).
* Scenarios: `outgrow-fence-ab.json` (old and new side by side), `outgrow-fence-map.json` (a growing suburb),
  `outgrow-fence-map-city.json` and `outline-flicker-city.json` (the user's city Brecon, read-only, `-Fresh`), `outline-flicker.json`.
