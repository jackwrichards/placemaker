# Ground runbook: Nature (wetland, lookout hill, nature reserve, hiking trail, greenway, campground)

The nature makeover (2026-10-29, `docs/refactor/MAKEOVER-BRIEF.md`). Read `docs/GROUND-RUNDOWN.md` and
`docs/runbooks/sportsPark.md` first; this page is what was done for the six nature types and what was learnt.

## Status

- Grades before: Nature A; wetland A+ ("the best in the pack"), lookout hill A/A+ ("some of the roads get a little
  weird"), campground shelved (to come back "into the limelight of A+").
- Branch: `makeover-nature` (see git log). In the user's game: no.

## What the user asked (2026-10-29)

- "Nature can do a complete overhaul too. Land o' lakes, marshlands, whatever. Now that we have access to creating
  decals ... we can go crazier."
- "Different tree types, reeds ... a lot of vanilla assets we kind of skimped on, that now combined with decals and with
  less surface painting, rather surface detailing, would be amazing."
- "Lots of rock assets themselves along with decals and dirt decals and water runoff."

## The pieces (all new, all in their own files)

| Piece | Where | What it does |
|---|---|---|
| The design | `Core/Decals/Dynamic/NatureGround.cs` | the designed ground of all six types (hooked in `Weathering.Wants` / `Apply`, same budget loop as the sports park) |
| The land grid | `NatureGround.Design.Land` | the built ground (after the type's terraform, `SiteHarmony.FinalGround`) on ~6000 cells whatever the size: slope, steepest way down (D8), drained area (flow accumulation), hollow depth (priority flood) |
| Library family | `Recipes/decals/library/nature.json` | 12 kinds: `runoffRill`, `gravelFan`, `marshMud`, `sedgeLitter`, `shoreMud`, `screeApron`, `dryStreamBed`, `heathPatch`, `animalTrail`, `ashScorch`, `trampledGrass`, `exposedRoots` |
| Generator | `DecalGenerator.Weight` | a negative `edgeBias` keeps a scatter's stamps toward the middle (pebbles down a rill's channel); positive unchanged, no kind used negative before |
| Planting and rocks | `Core/Layers/Common/NatureKit.cs` | groves (`Plant`), boulder groups with their spilled stones (`Rocks`), fallen logs (`Fallen`) |
| Candidates | `Scatter.Candidates(..., shuffle: true)` | a true seeded shuffle of the cells (opt-in; the default order is unchanged for every other type) |
| Offline look | `Tests/Placemaker.Tests/NatureGroundTests.cs` (`PM_WRITE_GROUND=1 PM_GROUND_ONLY=lookoutHill/hilltop`) | each style from above, hill-shaded, ponds blue, close-ups per cause, in `.forensics/ground/nature-*` |

## Causes and what each lays down

| Cause | Where it is read from | Kinds |
|---|---|---|
| Runoff | flow lines of the land grid: a rill starts where ≥ max(120 m², 12 cells) drains on ≥ 5 % slope, runs down the receivers, stops on the flat (< 1.8 %), at water or where it joins a rill already laid; cut 1.2 m short of every trail | `runoffRill` (strip), `dryStreamBed` where 14× the start area drains, `gravelFan` where it spreads out |
| Marsh | basins of the priority flood (cells that would hold ≥ 6 cm) | `marshMud` in the deep part, `sedgeLitter` and `dampPatch` on the rim |
| Banks | the type's ponds (regions tagged `place:water`, water surfaces) and water already there (`WaterDepthAt`) | `shoreMud` strip on the bank (heavy side to the water), `sedgeLitter` within 5 m |
| Scree | a steep cell (≥ 32 %) whose way down flattens (≤ 20 %) | `screeApron` facing downhill |
| Boulders | every boulder in the plan | `screeApron` downhill, `mossPatch` on the north (+z) side |
| Logs | fallen logs (scale ≥ 0.5) | `mossPatch`, `barkMulch` |
| Trees | species from the asset (summer leaves), thinned as the wood gets denser (80 trees/ha = 1, 220 = 0.36) | leaves, `pineNeedles` / `woodlandFloor` and moss under groups, `twigs`; twigs and bark under dead trees |
| Feet | trails (nets that follow the ground; boardwalks left alone) | `pathEdgeWear` both sides, `exposedRoots` where a trail passes within 2.6 m of a big tree, puddle and mud in the trail's dips, trodden gates, bare ground at benches |
| Animals | from a grove to water (else grove to grove), a wandering walk | `animalTrail`, `pawPrints` at the water |
| Open ground | drained / sunny / convex vs gathering ground | `groundMottle`, `dryGrass`, `dampPatch`; `heathPatch` on the heath |
| Camp | tents, fire rings (`place:fire` regions), tables, pads | `trampledGrass`, a `wornPath` to the trail, `barePatch` and `ashScorch`, pebbles on the pad |

## Rules the pictures taught

- The trail is where the eye goes first: the game's pale paved footpath read as a park path in a forest. `trailDirt` (a
  decal on the ground layer, over the path) makes it packed earth with grit and a ragged grassy rim; it is laid along whole
  chains of pieces and never thinned.
- Heather as a soft patch read as pink smudges on the grass; a dark peat base with crisp mauve and olive stamps reads as heath.
- The summer leaf set read as a yellow carpet in the wild's bright light: broadleaf trees drop `leafLitterWild` (dark brown,
  olive, no gold) at the quiet colour level.
- Dry grass at the old strength read as orange blotches on every open slope: 0.08 cover, 0.15-0.3 opacity, quiet.
- Blue-flowered garden bushes (`FlowerBushWild01`) read as a garden on a wild hill and a teal carpet on the heath: the wild
  types use the green wild bushes, a few dead ones on the heath.
- The game draws its logs (`Log01-18`) 10 m long whatever the scale: logs lie only in the wood (two trees within 9 m) and
  camp benches are the seats by a fire.
- A ring of small stones (`Rock01-30`, half sunk) with `ashScorch` in it reads as a campfire; vanilla has no campfire prop.
- Close-up cameras in a forest look at the canopy: the woods' ground detail is for the 45° view and walking.

## Runs that prove it

| Run id | What it shows |
|---|---|
| `show-23-builds-1029-232928-6f4e` | BEFORE: every nature style on 220 x 200, 45 + close |
| `show-23-builds-1030-000255-0969` | first pass: groves, rocks, fire rings, the nature ground |
| `show-4-builds-1030-002240-54f1`, `show-4-builds-1030-002240-474b` | lookout hill and hiking trail on 300 x 260 hills (why ridge, canyon and kite hill are hidden) |
| `show-20-builds-1030-004327-bf19` | dirt trails, heather, quieter leaves on every visible style |
| `show-3-builds-1030-004328-d340` | wetland grown 60 s (water filled), 45 / close / low |
| `send-ground-nature-1030-004429` | frame time (below) |

## Frame time and memory (`send-ground-nature-1030-004429`, camera on the whole site)

| Build | Instances | With the ground | Switched off | Cost |
|---|---|---|---|---|
| campground forest, 220 x 200 (the heaviest) | 1433 | 12.44 ms | 11.98 ms | +0.46 ms (busy machine) |
| lookout hill hilltop, 300 x 260 hills | about 1300 | 10.57 ms | 10.54 ms | +0.03 ms |

Library in use after every nature style: 261 variants, 60.5 MiB on the GPU (budget 200). The whole library with the nature
family: 692 variants, 191.4 MiB; the nature kinds use 6-8 variants and 384 px sheets to stay under the budget.
Planning (`NatureGroundTests`): 220 x 200 sites 90-600 ms whole plan; 700 x 600 on hills 0.5-1.4 s (the trees pass was 1.8 s
before the groups went into one brush call per kind). In the game a 220 x 200 nature plan logs 125-840 ms on the worker.

## Shelf (`Recipes/shelf/nature.json`)

Shown: wetland (all five), lookout hill (hilltop, sledding, terraces), nature reserve (forest, heath), hiking trail
(forestTrail, meadowWalk), greenway (leafy), campground (forest). Newly hidden: kiteHill (a bare dome with a cairn),
ridgeHike and canyonTrail (the ridge and gorge do not read; with the groves and dirt trail they were the forest trail).

## What failed and why

- The trees of every nature type stood on a lattice. Two causes: `Scatter.Candidates` visits the cells as an arithmetic
  progression (a caller that stops early keeps diagonal bands: the heath's bushes and the ridge hike's trees in rows),
  and a random acceptance saturates (every candidate gets its turn, the gaps fill, the wood is even). `NatureKit.Plant`
  shuffles, sets the groves as the share of the ground above a quantile of the clump field (wanted count against what the
  ground holds), and varies each tree's spacing.
- The climb reached the summit plaza going round it and turned 90° in the last step (the "weird road"). `HillPaths.Arrival`
  redraws the last stretch as one curve from the walker's heading onto the plaza.
- A second entrance joined the climb at its nearest point: on a big site a straight line up the hillside. It now joins at
  the nearest point it can reach at walking grade, as a gentle curve.

## Round 2 (the coordinator's six points, 2026-10-30)

- Rills: drained-area rills start lower (70 m²), are ordered by open ground, laid only where they show (off paths, out of
  water, out from under canopy), never thinned; slope rills on open convex hillsides (where water spreads and no channel
  gathers); washouts (grit, a fan) where a rill crosses a trail; the kind darker and wider (1.4-2.4 m). Run
  `send-ground-nature-1030-025021` `02-ground-nature-hill.09-rill.png`.
- Marsh: the wetland digs its own shallow hollows (25-60 cm, `MarshHollows`, sloped terraform) in the wet ground round the
  ponds for reedMarsh, wetlandPark and birdReserve, so the designed ground fills them with marsh mud and sedge on flat land.
- Close looks under the trees: `suite/scenarios/ground-nature.json` (woods, deadwood, log moss, roots). The runner's cameras
  sit south of their target and the canopy fills every close and low shot in a forest; the woodland floor was judged from
  the 45° view and the offline renderer.
- Hilltop: the arrival curve is longer (up to 3 plaza radii), the last piece points at the summit (worst turn near the top
  2-4°, tightest bend 10-12 m, `ClimbArrivesHeadOn`); a second entrance with no walking-grade join nearby takes a foot path
  round the bottom of the hill (`FootPath`) instead of a straight cut or a dead end.
- Lake country (`wetland` style `lakeland`, 12 languages): three big clear lakes, pine/spruce/birch groves to the shore,
  boulders and stones at the waterline, few reeds, one boardwalk. `show-7-builds-1030-025011-5b1e` `06-wetland-lakeland.06-45-clean.png`.
- Library: the nature family is 17.6 MiB if every variant were made (`NatureFamilyStaysLean`); 6-8 variants per kind,
  coarser texels on the speckle kinds.

## Round 3 (2026-10-30)

- Runner camera keys (`Game/Suite/SuiteShots.cs`, additive): `eye` + `height` (an eye at a ground point), `clearView`
  (turns round the target to the line of sight with the fewest trees and bushes of the last built plan). In
  `suite/schema.json` and `suite/README.md` (Camera). Scenario `suite/scenarios/ground-nature-close.json`. Lesson: eye
  height (2-3 m) in a wood still sees crowns; looking along a trail at 12-30° is what shows a wood's ground.
- Marsh: the hollows are dug before the woodland, planted (a ring of reeds, tussocks on the rim, the wet middle kept
  open) and laid as `place:marsh` regions that the designed ground fills (peat, standing water, sedge). Reed marsh at 45°
  (`show-21-builds-1030-034143-4058` `18-wetland-reedMarsh.06-45-clean.png`) and close
  (`send-ground-nature-close-1030-035729` `02-close-marsh.10-marsh-mid.png`) read as marsh.
- Merged master's linear-albedo fix: heather went pink and was browned; trail dirt, rills and wild litter retuned.
- Removed `FlowerBushWild01`/`FlowerBushWildElderly01` (render pale teal) from every nature palette.

## Round 4 (2026-10-30)

- Polka dots: reeds on the pond shores and the marsh rims now stand in drifts (a clump field with a cut, open wet margin
  between), in sizes 0.6-1.5, with willow scrub (a young alder or a tall wild bush) now and then; vanilla has no reed or
  water-plant props (the reeds are the narrow wild bushes). Heath: 230 bushes/ha instead of 340, every bush its own size
  (big in a thicket's heart, small at the edge), bare peaty patches between. `show-3-builds-1030-044246-02fd`,
  `show-natureReserve-heath-1030-044246-0b4f`.
- Lake country fills all three lakes with the latest code (grown 60 s, same run).
- Footpaths: vanilla has no dirt or gravel path net; the dirt decal is the only way to make a trail earth (it reads well
  at 45°, paler at eye level on the path mesh).

## Left to do

- The user has not graded the new ground yet.
- Wetland: only the shared ground changed (banks, sedge, trails, leaves); its layout is untouched.
- Runoff rills and stream beds are faint from 45° on a 300 m site and mostly sit under the woods; marsh mud only shows
  where the built ground holds water outside the ponds (rare on a flat block).
- Dead snags (`NatureKit.Plant` `dead`) not checked close in the runner.
- Campground meadow and caravan stay hidden (vanilla has no caravans).
