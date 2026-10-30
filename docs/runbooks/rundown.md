# Ground runbook: Rundown (abandoned lot, construction site, urban ruins)

The city's in-between and forgotten places, a Make-row category of their own since 2026-10-29 (`Recipes/shelf/rundown.json`).
Read `docs/GROUND-RUNDOWN.md` first; this page is what was done for the rundown types and what the pictures taught.

## Status

- Grade from the user: not graded yet. Before: abandoned lot and construction site were in Industrial, urban ruins a Sports
  type; the user wanted them "brought back into the limelight of the awesomeness of A+".
- Branch: `makeover-rundown` (see git log). In the user's game: no.

## The user's direction (2026-10-29)

"Leftover city spaces, rundown areas, just empty areas." And: "I don't think one setting of neglect covers it, because neglect
is more than just stripping away features or adding features. It changes the entire vibe of everything ... it should be its
own entire version, with a whole subsection of things inside of it. It's not just a simple slider." So every kind of
rundown place is its own style with its own story, contents and ground; the Overgrowth/Rubble/Junk sliders only nudge
the balance inside a style.

## What is on the shelf (round 6, 2026-10-30)

| Type | Styles shown | Hidden (code kept, buildable by id) |
|---|---|---|
| Abandoned lot | Rubble field (now the standard style), Demolished block, Squatter camp, Fenced wasteland, **Old car park** (new), **Burnt-out lot** (new) | Overgrown lot and **Vacant lot** (new): after four rounds the overgrown lot still read as a park from 45 degrees and the vacant lot as a lawn with a billboard (thickets, forest-floor paint, a rank-grass carpet all tried; the carpet read as yellow blotches and was removed) |
| Construction site | Foundation dig, Site compound, Demolition site, Earthworks | Road works (a plus of road strips across a fenced block reads as nothing real) |
| Urban ruins | **Building ruins** (new, now the standard style, "Ruined building"), BMX dirt jumps | Broken highway (no placeable deck: rows of broken columns in a field), DIY skate spot and Parkour yard (skate parks on a lawn), Graffiti park (needs decal packs) |

## How the ground is made (different from the sports park, on purpose)

The sports park designs its ground after composition by reading the plan back (`Weathering.cs`). A rundown type knows its
story while it plans (where the building came down, which slab cracked where, where the squatters sleep, which way the
lorries drive), so it lays its decals right there, from its own causes, through one shared kit:

- `Core/Layers/Types/Rundown/RundownGround.cs`: the kit. Passes: `Ground` (the base mosaic: bare soil, weed tufts, dry
  grass, grit, stones, moss, litter, each with its own field, weeds thick at the edges and thin where feet go), `Slab` (an
  old slab: tone, cracks in the shards, weedy cracks along its real crack lines, moss at the joints, grime at the rim, old
  oil), `Rubble` / `RubbleField`, `Fire` / `BurntArea`, `Junk`, `Shortcut`, `Ruts`, `MudOut` (tread carried out through
  the gate onto the street), `Puddle` / `Pool` (stagnant water), `FenceLine`, `Line` / `Exact` (painted lines laid exactly).
  Budget per hectare like `Weathering.PerHectare`, thinned evenly on huge sites, a hard cap at `Weathering.MaxInstances`.
- Per type: `AbandonedLotGround.cs` (a look per lot type + paint + causes read from the placed objects),
  `ConstructionGround.cs`, `UrbanRuinsGround.cs`. Nothing in the shared `Weathering.cs` changed (so the four other agents'
  hooks there don't collide with this).
- **Paint does the big variation** (free, permanent, the city's). The test map's paint slot 1 ("dry grass") is almost the
  lawn's colour; the earth is slot **3**. The lots and the construction site defaulted to slot 1, which is why their
  "dirt ground" was invisible in every earlier picture. Now: dry grass over the whole lot, a thin film of earth (0.2-0.4)
  through it, earth in blobs of blobs where the story puts it, the dark forest floor (slot 2) under every thicket. The
  construction site is painted earth instead of a grey surface sheet over everything (that read as plastic).

## Library kinds added (`Recipes/decals/library/rundown.json`)

| Kind | What |
|---|---|
| `rubbleSpread` | broken concrete and brick chunks, brick ends, grit over a thin dust film |
| `brickDust` | red-brown dust and mortar grit, eroded edges (quiet: it read as pale smudges at first) |
| `crackWeeds` | chain crack with soil in the gap and weeds along both lips |
| `weedTufts` | clumps of rough grass and weeds, olive and straw, almost no film |
| `burntPatch` | char core, ash flecks, charred sticks, a scorched halo breaking up |
| `fadedLine` | a worn, flaking painted line (car-park bays, lane lines) |
| `soilSpill` | soil and clods off heaps, buckets and tyres |
| `stagnantWater` | dark brown-green still water with a scummy rim and silt |
| `oilStainOld` | a big old sun-faded oil stain |

## Rules the pictures taught

- **Check what each paint slot looks like on the map before using it.** Slot 1 is invisible on the vanilla terrain.
- **A rundown lot reads as a lawn until the earth goes in.** Decals alone cannot change a hectare of green; paint can.
- **Evenly spread bushes read as a park.** Scrub grows in thickets with open ground between (`Overgrowth(thicket)`), with
  the dark forest-floor paint under each thicket.
- **Big soft decal patches are smudges** (bare patch, dry grass, brick dust at full size): smaller, fainter, the paint carries
  the big tone.
- **The game's vehicle props are not placeable** (`Prop_CarTrailer01`, `Prop_FrontendLoader01` ... match nothing in a lineup):
  no abandoned cars, no diggers. Rubbish heaps (`Trashpile01-08`), bags, an old sofa stand in.
- **`EU_Wall01Long` is a white balustrade.** Of the garden walls only `EU_Wall01`, `EU_Wall05`, `EU_Wall08` (and their
  `Low` versions) are dark plain masonry; they are 0.9 m (0.6 m low), scaled up to 1.8 m for the taller corner remnants.
  `RubblePylonLarge01-04` are 5 m broken concrete columns: the best ruin piece the game has.
- **A slab broken into a grid of shards reads as paving.** The old car park is one slab with wandering cracks (random walks in
  from the edges) and weeds along them.
- **Concrete Surface 02 is bright white.** Old slabs use Pavement Surface 01 (dark grey).
- Lab scenarios: `suite/scenarios/lab-rundown-props.json`, `lab-rundown-walls.json` (lineups of candidate props).

## Runs

| Run | What it shows |
|---|---|
| `show-17-builds-1029-232943-f816` | BEFORE: every style of the three types, plain green ground, no decals (vanilla has no dirt decals), white walls |
| `show-6-builds-1029-235436-5714` | first designed grounds: paint slot 3 found, rubble field reads |
| `show-9-builds-1030-000649-d2af`, `show-6-builds-1030-000649-1925`, `show-4-builds-1030-000713-903d` | round 2 |
| `show-19-builds-1030-002631-720a` | round 3: car park reads (dark asphalt, faded bays, weedy cracks), dark masonry walls |
| `show-18-builds-1030-005232-5281` | FINAL round 4: every shown style, 45 / close / low (the low view frames too far to judge) |
| `send-ground-rundown-1030-005242` | frame time and close-ups by kind (rubble, bays, weedy crack, burnt, basement, mud out) |

## Numbers

- Planning (offline, `RundownGroundTests`): 20-350 ms a style on 146 x 136 and 216 x 196 m; every style lays 170-580
  instances a hectare; huge sites (700 x 600 m) stay under `Weathering.MaxInstances`.
- Frame time, re-measured after every texture was made (`send-ground-rundown-1030-014837`, rubble field 220 x 200, 2549
  instances): with the ground 7.18 ms, switched off 7.14 ms (+0.04 ms; before the build 6.88 ms). First measure
  (`send-ground-rundown-1030-005242`, rubble field on 220 x 200, 2610 instances): before the build 6.69 ms,
  with the ground 7.17 ms, ground switched off 7.05 ms (+0.12 ms). Caveat: the log shows the variants still being made
  when "with the ground" was measured; worth one re-measure.
- GPU: the rundown family is 9 kinds, 49 variants, 9.8 MiB if every variant is on the GPU (`RundownFamilyStaysLean`, cap 14 MiB).

## Round 5-6 (the coordinator's list)

- **Construction machines: a bug older than the makeover.** The shared machine set (`GritAssets` ConstructionMachine) and the
  heap set name only vehicle props (`Prop_FrontendLoader01`, `Prop_CoalTruck01`, `Prop_CoalTruck01_StonePile01_*`); the lab
  `suite/scenarios/lab-rundown-machines.json` (run `send-lab-rundown-machines-1030-012753`) matched none of them, so no
  construction site ever had a machine. The game has no placeable plant vehicles. The construction site now overrides both sets
  with things that exist (generators, skips, gas pallets; the real stone loads). The shared set is left for the lead.
- No frame stage: the game has no scaffold or building-frame pieces that stand alone.
- Mud out of the gate: tread tracks run from 18 m inside the gate 8 m out over the pavement, wider, with clods.
- Building ruins: the floor slab stops at the basement (it covered the pit), half the building fell in, the basement has its own
  low walls with a stair gap, two layers of stagnant water and fallen rubble on its floor.
- Leftover strips (`show-3-builds-1030-014846-228c`, lots on a 220 x 26 m strip): the rubble field reads well as a dumped verge;
  a separate strip type would be the same lot in another shape. Medians and roundabout islands need the road's own space, which
  Placemaker does not draw in. Under-the-overpass needs an overpass the game draws above; there is no placeable deck, so it
  cannot be faked. Not done, on purpose.

## Left to do (honest)

- Overgrown and vacant lot: hidden; they would need a tall-grass or meadow prop the game does not have.

## Final runs (round 6)

| Run | What it shows |
|---|---|
| `show-15-builds-1030-020140-8786` | every shown style, 45 / top / close |
| `show-2-builds-1030-020820-0241` | building ruins on a block and a 5 % slope: the basement with its walls and standing water |
| `send-ground-rundown-1030-014837` | frame time after the textures were made; close-ups by kind |
| `send-lab-rundown-machines-1030-012753` | which construction props exist (no `Prop_` vehicle does) |

## Round 7 (2026-10-30): real machines, walls the right way round

- **Machines.** `AutoAssets.IsOwn` (the narrowed version from makeover-industrial: only `VehicleProps.Prefix`, test
  `OnlyOurVehiclePropsCountAsVanilla`) lets Placemaker's own vehicle props pass the vanilla rule. They are made at load from
  the game's vehicles (`Game/Assets/VehiclePropSystem.cs`, slots in `IndustrialAssets.VehicleProps`): Loader 1 = FrontendLoader01,
  Truck 1 = CoalTruck01 (a dump truck), Tractor 1 = OreMiningTractor01. No excavator: the mining excavator (25 m) is not made.
  The construction site passes them as its machine list, as the industrial quarry does (lab `send-lab-rundown-vehicles-1030-024155`:
  all five matched; game `show-4-builds-1030-031235-d988`: loaders, dump trucks and mining tractors on every site).
- **Why the category path never worked:** the game's catalog has no `construction.machine` items, so `SiteKit.Cat(ctx, null,
  ConstructionMachine)` returned nothing and no machine was planned at all, whatever the shared set named. The shared `GritAssets`
  sets now name things that place (our vehicle props, real heaps; no `Prop_` names), but they only apply where a category has items.
  The scrapyard's wreck and machine sets go the same way (`show-2-builds-1030-022802-a671`: no cars): the industrial agent's
  car yards pass explicit lists and show cars (`show-carYard-salvage-1030-025923-1817`).
- **Walls stood across their line.** The game's garden walls run along their local Z (lab: 0.2 x 0.9 x 3.0 m); the lot's wall
  remnants (code older than the makeover) and the building ruins turned them 90 degrees, a comb of short pieces. Fixed; the
  basement wall is two skins of the dark masonry at 1.5-1.85x height.

## Rounds 8-12 (2026-10-30): the coordinator's 45-degree review

- **Lawn everywhere was a bug:** `DecayKit.Inset` moved the vertices of the filled area in; a filled area has rounded corners of
  many short edges, the moved ring folded over, the helper returned null and the whole-lot earth paint was silently skipped
  (only the small blobs landed). It now falls back to a true polygon offset. Earth now covers the lot to within 2 m of the fence.
- **No teal bushes:** `RundownPlants` (green wild bushes and the dead bush only; FlowerBushWild01/02/Elderly01/Child02 render
  teal). Every rundown planting uses it; the automatic weed pool is never used.
- **Rubble field:** tipped loads (runs of 4-6 big heaps, raised mounds under them) in a fan from the way in, a trail of spills,
  dumped concrete blocks, scrub on the older mounds, muddy ruts along the tipping line.
- **Building ruins own the site:** footprint 0.4 x the plot, broken concrete yards either side, rubble thrown wide from the
  collapse, no laid paths, thickets.
- **Construction:** spoil (rubble heaps; the PileStone/PileSand resource piles draw empty, the ore loads are orange) banked along
  both long sides of the dig with machines at them, packed laydown yards (one material per row, a lane between), large compounds.
- **Hidden after judging every style from 45 degrees:** demolished block, squatter camp, BMX dirt jumps, construction compound and
  earthworks (with overgrown, vacant lot, broken highway, roadworks and the skate styles from earlier rounds).
- Frame time after the master merge (true decal colours) and the retune (`send-ground-rundown-1030-042444`): rubble field 220 x 200
  with the ground 6.75 ms, switched off 6.77 ms, before the build 6.61 ms: no measurable cost.
