# Smarter roads: the rules, the rating, the road lab

The roads type (and the grid zoning that rides on it) places streets by a few hard rules, scores every candidate layout
with one rating, and is tuned offline in the road lab. This file is the map; the code comments carry the detail.

## Hard rules (the planner enforces them; the rating and the all-sites test check them)

| Rule | Where |
|---|---|
| No two junctions closer than **45 m** along a road (`RoadTopology.MinJunctionGap`, 0.5 m slack). A link from our road to a city road (its two ends fixed by the selection and the city) needs **30 m** (`RungGap`). Only junctions with road between them count: two junctions on parallel roads across a strip of land do not crowd each other (`RoadBetween`). | `RoadTopology.EnforceJunctionGap` (give up an arm, re-aim it onto the other junction, meet halfway, or slide the side street's junction along the road), `RoadCityLinks` (`JunctionClear`), `RoadCharacter` (diagonal crossings), `RoadWalkways.Lanes` (`ClearOfJunctions`) |
| Give up rather than squeeze: no block too small or thin to build on (`RoadsPlanner.IsSliver`), no knife-edge junction (< 40°). | `RemoveSlivers`, `GiveUpKnifeEdges` |
| Never join, cross or treat a highway / ramp as a street; never modify an existing road; a small road stays small. | `RoadConnections` (`ConnectTo`, barriers), `RoadClasses` |
| No kink (a sharp bend with no curve), no hook (a street under 150 m turning > 100°; the winding lanes of the organic styles excepted), no stub (a dead end stopping within 22 m of a road it does not join), no crumb (< 6–8 m piece), no two of our streets crossing without a junction, never cut the network off the city. | `RoadDesign.Tidy`: junction gap again → `GiveUpAlongside` → `ExtendTips` → `PruneStubsBesideRoads` → `ExtendTips` → `FilletBends(carOnly)` → `StraightenHooks` → turning circles. Every give-up checks `RoadTopology.Parts` (pieces + pieces no longer on the city). |

Tidy runs at the end of `RoadDesign.Run` and again after `RemoveSlivers` (anything given up late gets the same care).

## Defaults that adapt (the player can override every one)

- Perimeter corners hug the selection (`TightCorner` 15 m); the winding presets round more (`SoftCorner` 30 m).
- No flattening or terracing by default (`LandShape` natural); terraced presets keep theirs.
- Links: streets only (`ConnectTo`), never highways or ramps; "any road" lets the player join a big road on purpose.
- Grid character (`gridCharacter`): strict / auto / lively. Auto bends the grid to hills and lays a diagonal where a
  desire line asks (two city points the grid makes people zig-zag between) and the rating says it pays. When the blocks
  as set leave no room for the diagonal's crossings, `RoadCharacter.FitGrid` offers a grid fitted to it: blocks two
  junction gaps or more each way in the diagonal's proportion (≤ 1.7× the set spacing), lined up so the diagonal crosses
  every street at its middle. No diagonal where a city road already runs along the desire line (`Served`).
- The grid sits on a straight big road through the area (spine) when the rating prefers it; streets line up with the
  city's streets around.

## The rating (`Core/Layers/Roads/RoadsRating.cs`)

Sub-scores: grades, junctions, segments, perimeter, links, fit, directness (weights in `Measure`), plus the counts the
hard rules use (`CrowdedJunctions`, `Slivers`, `AcuteJunctions`, `Kinks`, `Hooks`, `StrayStubs`, `SelfCrossings`,
`Crossings`, `BareDeadEnds`, `HighwayLinks`...) and `Alongside` (a street with no building room beside another road; a
penalty, not a hard rule: a grid corner cut by a diagonal city road can do it). `RoadsPlanner.Plan` plans each candidate
layout (outline / spine / follow / tilted / diagonal / fitted diagonal) and keeps the best rated.

## The road lab (`Tests/Placemaker.RoadLab`)

Real planner, synthetic land (open, city grid, highway with ramps, avenue through, small two-lane road, hills, steep,
irregular, rotated, everything at once, a desire line, a big hill), no game. One SVG per case × design, `index.html`,
`ratings.json`.

```
dotnet run -c Release --project Tests/Placemaker.RoadLab -- --out docs/roadlab/after --variants default,downtown,village
dotnet run -c Release --project Tests/Placemaker.RoadLab -- --out docs/roadlab/after --compare docs/roadlab/before   # side by side
dotnet run -c Release --project Tests/Placemaker.RoadLab -- --cases desire --variants default+gridCharacter=lively --trace
```

`--trace` also writes `<case>~<variant>.nets.txt` (every net and every rating mark) and one picture per candidate layout.
`--dump` writes each plan as text (`PlanText.cs`); `--plans DIR` rates and draws plans another build wrote, which is how
master's plans are scored with today's rating: build a copy of the lab's `Cases.cs` + `PlanText.cs` against master's
`Core` (add `NearbyNet.IsHighway` / `SpeedLimit`), plan every case, write the plans, then `--plans` them here.

On 2026-10-19, 108 layouts (12 sites × 9 designs), both scored with this rating: master had problems in 82 (1,137 crowded
junctions, 100 sliver blocks, 63 knife-edge junctions), mean 76.1; this branch has none of those, mean 90.9 (7 layouts
keep an `Alongside` penalty).

The all-sites test, `RoadsSmartTests.NoCrowdedJunctionsSliversOrKnifeEdgesAnywhere`, runs every lab site × nine designs
and fails on any hard-rule count above zero.
