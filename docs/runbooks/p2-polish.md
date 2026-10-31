# Runbook: P2-4, the polish pass (parks, sports, industrial, rundown)

Phase 2 (`docs/refactor/PHASE2.md`, "P2-4"). Branch `claude/p2-polish-d5b8ca`. In the user's game: no.

The user's notes (through the brief; `GRIPES.md` itself is in no branch, the brief quotes it): parks on big hills and rugged
land "shouldn't break", paths that "double back on each other and snap"; sports parks lack paths to the facilities and their
plaza does not grow; no border round the industrial and rundown holes, no piles over the edge, no floating cars; the active
landfill could be better; the decal memory across categories; the three failing sports park tests. The lead, on Biome mix:
meadow, mulch beds and heath all read as scattered bushes. Colours: leafy greens, brown not rust, subtle.

## What changed

| Piece | Where | Before | Now |
|---|---|---|---|
| Paths on hills (every park, garden, playground and memorial type that meanders) | `Core/Layers/Park/ParkPathStyles.cs` `Meander`, `Chains`, `Straighten` | a bend on every roadmap link: on a hill the router's walk is many short links, so the walks kinked every 6-10 m (the sawtooth in the hill matrix) | each run between junctions is straightened first (Douglas-Peucker, 3.5 m; a leg never climbs faster than the walk grade or the steepest piece it replaces, so the router's switchbacks stay as long legs), then only legs 1.4 wavelengths or longer bend, gentler the steeper they are. `ParkPathKinkTests`: botanical gardens 246-271° of turning per 100 m -> 108-133, garden park 160-195 -> 46-86 |
| Walks on rugged land (every type the site harmony grades) | `SiteHarmony.Profile` (`ProfileLength`) | the profile was smoothed over ~12 m (a fixed 2.5 % pull to the ground on 2 m vertices): 30-40 m bumps stayed in the walk | smoothed over ~24 m whatever the vertex spacing, each vertex capped by the border bank at its level band's far edge (the band stays level near the outline). `WalksOnRuggedLandAreGraded`: walks climb 1.7 m per 100 m over ground climbing 17 (2.7-3.3 before) |
| Meander shape | `ParkPathStyles.AddMeander` | alternate corners every wavelength: with short waves (botanical: 5.5 m every 16 m) a zig-zag on any ground | one smooth wave of the same period, sampled every eighth, swing at most 7 % of the wave, fading at the leg ends |
| Drawn path styles on steep land | `ParkLayer.SteepSite`, `DrawnPathStyles` | urban spokes 408 steep path pieces on mountains, picnic circuit 322 | on a site where over a third climbs faster than 12 % the figure gives way to slope-following curvy walks, with a panel note (all 12 languages; i18n panel test 12 of 12) |
| Meadow | `Biome.cs` | 520 tussocks a hectare: read as bushes | open long grass: the new `meadowSward` decal (leafy tussock texture, seed tufts) over the whole area, long grass faint under it, 50 tussocks at the rim, 4 shrubs, 3 solitary trees |
| Mulch | `Biome.cs`, `BiomeBuilder.IslandBeds` | noise patches on half the area, wild bushes | island beds: round beds (5 m ±35 %, wobbly edge) on a jittered grid spaced for 36 % of the area, mown lawn between, specimen trees only in beds (`TreesInBeds`), the city's clipped garden shrubs packed inside |
| Heath | `Biome.cs`, new `heathSand` decal | 700 mixed wild bushes incl. the pale lilac one | a mauve-brown heather carpet, low dark heather mounds in tight clumps (Drift 4 m), gorse drifts, birch-led trees, grey-brown sandy trails (all trail passes on the same lines: `BiomeGround.Trails(0)`) |
| Sports park plaza | `SportsParkComposer.EntrancePlaza`, `GrandPlaza` | fixed ~28 x 20 m (and 14 m on a very big site: the layout scale) | grows with the site in real metres (span / 150 m, up to 76 x 46 m), slides along the edge off a corner; a big one gets tree groves in a grid on both flanks, benches facing the middle, a fountain each side on the biggest, lamps |
| Sports designs' walks | `SportsDesigns.Walks` | the five designed grounds laid paving surfaces only: **no path nets at all** | a loop on the perimeter promenade, a spur from every gate, from the loop the shortest clear spur to each facility (square off its side, one turn, never across another facility); whatever stood there goes, the tree rows planted after keep off |
| Sports designs on big sites | `CageCourts`, `CentreCourt` | 420 x 360 m: ~80 cages, 48 clay courts | a block of up to 16 cages (4 rows), at most 4 practice-court blocks; the rest is the design's gardens |
| Sports tests | `SportsDesigns`, `SportsParkComposer.Tree`, `SportsParkTests` | 4 failing | athletics gets a 70 x 40 m training track on a hectare; a design gives way to reserved space (classic park plans round it); grove crowns keep off reserved space; the terrace-stands and design-signature tests updated to the 2026-10-29 designs |
| Ruins' basement | `BuildingRuins.cs` | a continuous double wall 1.5-1.85 m tall round the hole: a border fence | broken low stretches (corners kept, about a third of the rest, clumped), one skin |
| Landfill | `LandfillLayer.Active` | daily cover over the whole tip on the vanilla fallback "Oil Surface 01" (pale sand): a bleached disc with a pie slice | brown forestry ground as the cover, only on the top and the working side, the far flanks green (older capped cells), a ragged edge |
| Game runner land | `Core/Suite/SuiteLayout.Grow.cs` `PatchOutside` | land laid out after the 11th plot of a session was left flat (0 texels): the old fixture's bounding box was kept whole, and the second row of plots lies inside it. **Every hill test with more than ~10 lands in one game ran later builds on flat ground** (the first P2-4 matrix too) | only the grid and each earlier plot are kept; `SuiteLayoutGrowTests.LandInASecondRowGetsItsGround` |
| Decal memory | `DecalTexture.DetailSize/Resample`, `SharedDecals`, `DecalVariants.GpuBytes` | every category at once 203 MiB (game budget 200; in-use variants are never freed) | normal and mask maps at half size, colour full: 102 MiB (whole library 252 -> 127). Cache format PDC2. `DecalBudgetTests` measures it (plans every shelf type and style on flat and rolling land) |

## Round 2 (the lead's review: everything below A-)

| Piece | Where | Now |
|---|---|---|
| Construction pit barriers | `ConstructionSiteLayer` foundation | no ring round the hole (the user's rule); short runs either side of the ramp mouth, cones across it |
| Park car park on steep land | `ParkCarPark` | the flattest spot on any frontage; none (panel note, 12 languages) where even that climbs over 4 m. Wild park on mountains: cut 11.5 -> 3.5 m |
| Ruins' walls | `BuildingRuins` | stubs of every height (0.5-1.2x, corners to 2x), some leaning, broken masonry heaped at their feet with rubble decals |
| Rundown lots | `AbandonedLotLayer`, `AbandonedLotGround` | twice the dumped heaps; scrub thickets reaching 30 m in on the wasteland and burnt lot (rubble field stays rubble-led), self-seeded trees, 2-5 dumped car wrecks, scorches beside rubbish, more puddles, ruts on the burnt lot |
| Landfill | `LandfillLayer` | loaders and refuse lorries on the face and queuing on the haul road (our own vehicle props: the clean game has no machine pool), a ragged working face |
| Sports park on a big site | `SportsParkComposer.Count`, new control `growWithSite` (12 languages) | the repeatable facilities multiply with the site (x(area/4.5 ha)^0.8, at most 4): community on 15 ha 4 -> 12 facilities |
| Arena on a hill | `SportsDesigns.Arena` | banks the hill rises behind become seating terraces with benches |
| Hairpins (tried, reverted) | `ParkPathStyles.WidenTurns` | arcs at turns over 80 degrees pushed walks onto steeper ground (wild on mountains 164 -> 325 steep pieces) and still read as Vs from above |
| Tried, reverted | `ParkPathPlanner.SlopeFactor` | a stiffer climb penalty (6 -> 14) made natural walks more convoluted (112 -> 179 steep pieces): the roadmap limits it |

## Round 3 (the lead: the mountain arena and real switchbacks)

| Piece | Where | Now |
|---|---|---|
| Arena on a hillside (cross slope 6 % and up) | `SportsDesigns.Arena`, `ArenaFit`, `HillFloor`, `Rake`; `PlazaDesigns.Levels.AddCore` (planar banks) | the pitch lies along the contour (the design turns a quarter when the hill runs along it); the uphill long side has no grandstand: it is seating cut into the hill (concrete terraces, benches, four aisles of stairs, a plinth at the foot, a railing on top), the floor as deep as the 40 % rake lets it go; the other banks run at one grade out to the hill and are wooded. Banks on a 22 % mountain 237-331 % before (cliffs) -> 42-50 %; the pitch format shrinks until the banks fit |
| Switchbacks | `ParkPathStyles.Switchbacks` (`ParkPathSwitchbacks.cs`), called by `ParkLayer` and `LeisureLayer` | every run of a walk that climbs over 3 m with a 12 m stretch steeper than 10 % is retraced: legs at 6.5 % along the contour, a round hairpin (5.5 m) at the corridor's side or an obstacle, straight to the end only where the ground on the way is gentle. Four tries per run (up or down, first leg either way), the shortest kept; a trace that would cross a walk, come within 7 m of one, or split the network is dropped (the run stays as it was) |

Open: on the game runner's mountains only 1-3 runs per park trace (the others dead-end in the corridor); the walks that are not traced still climb as the router laid them.

## Verified in the game (pit checks)

Run `show-56-builds-1030-232328-ebb3` (every industrial and rundown style, 220 x 200 flat and a 12 % slope; before the ruins
and landfill changes): pit rims cut 49-50 fence pieces round each quarry, removed 1-10 straddling piles at quarries,
foundations and ruins; 146 parked vehicles set onto their wheels; no floating cars in the yards; 0 game errors.
Construction-site foundations still have the orange barrier flags along the pit edge (barrier props, left on purpose by
p1-ground): the user decides.

## Runs

| Run id | What it shows |
|---|---|
| `show-100-builds-1030-231107-1ea7` | BEFORE: every park style on a 10 % slope, 20 % hills, mountains, rugged (45, top) |
| `send-p2-biomes-r1-1030-232325` | meadow, mulch, heath at 100 % and a mix after the first change |
| `show-45-builds-1030-232426-5eca` | BEFORE: every sports style on the four hard lands and 420 x 360 |
| `show-56-builds-1030-232328-ebb3` | pit checks (above) |
| `show-12-builds-1030-234333-9686` | parks after the path fix (plots 1-11 on real land; showed the runner bug from plot 12) |
| `send-p2-biomes-r2-1030-235003` | meadow after round 2, the decal half-size maps drawing in the game |

## Lessons

- Check the terrain map (`-Modes clean,terrain`) before judging a hill picture: a flat plot looks like a hill test from 45°.
- A bend per graph edge is a sawtooth once the router makes many short edges: bend runs between junctions, not edges.
- The plaza kit's designs lay surfaces only: anything a citizen should walk needs path nets of its own.

## Open

- Classic sports styles on a very big site: the facilities do not multiply with the site (the counts are the style's), so
  15 ha is a few fields in a wood of groves. Needs "auto" counts that fill a big site (a controls change).
- Hillside sports ground on a big site: its terrace rows run the full width (10 courts a row).
- Designed sports grounds on mountains level their box with 40 % banks: the arena stands on a big green plinth.
- Designed sports grounds on a big site: the leftover is plain rough lawn (no groves or paths beyond the spurs).
- `ParkPathHierarchyTests.SmallPathsTrailsAndPavedShortcutsAddNarrowJoinedPaths` fails on master too (9 loose ends; 7 now).
- Rundown lots (rubble field, fenced wasteland, burnt out) read sparse and flat from 45°.
