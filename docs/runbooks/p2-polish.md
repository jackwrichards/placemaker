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
| Meadow | `Biome.cs` | 520 tussocks a hectare: read as bushes | open long grass: the new `meadowSward` decal (leafy tussock texture, seed tufts) over the whole area, long grass faint under it, 50 tussocks at the rim, 4 shrubs, 3 solitary trees |
| Mulch | `Biome.cs`, `BiomeBuilder.IslandBeds` | noise patches on half the area, wild bushes | island beds: round beds (5 m ±35 %, wobbly edge) on a jittered grid spaced for 36 % of the area, mown lawn between, specimen trees only in beds (`TreesInBeds`), the city's clipped garden shrubs packed inside |
| Heath | `Biome.cs`, new `heathSand` decal | 700 mixed wild bushes incl. the pale lilac one | a mauve-brown heather carpet, low dark heather mounds in tight clumps (Drift 4 m), gorse drifts, birch-led trees, grey-brown sandy trails (all trail passes on the same lines: `BiomeGround.Trails(0)`) |
| Sports park plaza | `SportsParkComposer.EntrancePlaza`, `GrandPlaza` | fixed ~28 x 20 m | grows with the site (span / 150 m, up to 76 x 46 m), slides along the edge off a corner; a big one gets tree groves in a grid on both flanks, benches facing the middle, a fountain each side on the biggest, lamps |
| Sports designs' walks | `SportsDesigns.Walks` | the five designed grounds laid paving surfaces only: **no path nets at all** | a loop on the perimeter promenade, a spur from every gate, from the loop the shortest clear spur to each facility (square off its side, one turn, never across another facility); whatever stood there goes, the tree rows planted after keep off |
| Sports designs on big sites | `CageCourts`, `CentreCourt` | 420 x 360 m: ~80 cages, 48 clay courts | a block of up to 16 cages (4 rows), at most 4 practice-court blocks; the rest is the design's gardens |
| Sports tests | `SportsDesigns`, `SportsParkComposer.Tree`, `SportsParkTests` | 4 failing | athletics gets a 70 x 40 m training track on a hectare; a design gives way to reserved space (classic park plans round it); grove crowns keep off reserved space; the terrace-stands and design-signature tests updated to the 2026-10-29 designs |
| Ruins' basement | `BuildingRuins.cs` | a continuous double wall 1.5-1.85 m tall round the hole: a border fence | broken low stretches (corners kept, about a third of the rest, clumped), one skin |
| Landfill | `LandfillLayer.Active` | daily cover over the whole tip on the vanilla fallback "Oil Surface 01" (pale sand): a bleached disc with a pie slice | brown forestry ground as the cover, only on the top and the working side, the far flanks green (older capped cells), a ragged edge |
| Decal memory | `DecalTexture.DetailSize/Resample`, `SharedDecals`, `DecalVariants.GpuBytes` | every category at once 203 MiB (game budget 200; in-use variants are never freed) | normal and mask maps at half size, colour full: 102 MiB (whole library 252 -> 127). Cache format PDC2. `DecalBudgetTests` measures it (plans every shelf type and style on flat and rolling land) |

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

## Open

- Classic sports styles on a very big site: the facilities do not multiply with the site (the counts are the style's), so
  15 ha is a few fields in a wood of groves. Needs "auto" counts that fill a big site (a controls change).
- Hillside sports ground on a big site: its terrace rows run the full width (10 courts a row).
- `ParkPathHierarchyTests.SmallPathsTrailsAndPavedShortcutsAddNarrowJoinedPaths` fails on master too (9 loose ends; 7 now).
- Rundown lots (rubble field, fenced wasteland, burnt out) read sparse and flat from 45°.
