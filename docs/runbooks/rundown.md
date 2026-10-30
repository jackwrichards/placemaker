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

## What is on the shelf

| Type | Styles shown | Hidden (code kept, buildable by id) |
|---|---|---|
| Abandoned lot | Overgrown lot, Rubble field, Demolished block, Squatter camp, Fenced wasteland, **Old car park** (new), **Burnt-out lot** (new), **Vacant lot** (new) | none |
| Construction site | Earthworks (levelling), Foundation dig, Site compound, Demolition site | Road works (a plus of road strips across a fenced block reads as nothing real) |
| Urban ruins | Broken highway (now without the skate park), BMX dirt jumps, **Building ruins** (new) | DIY skate spot, Parkour yard (skate parks on a lawn: sport, not a forgotten place), Graffiti park (needs decal packs) |

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
- Frame time (`send-ground-rundown-1030-005242`, rubble field on 220 x 200, 2610 instances): before the build 6.69 ms,
  with the ground 7.17 ms, ground switched off 7.05 ms (+0.12 ms). Caveat: the log shows the variants still being made
  when "with the ground" was measured; worth one re-measure.
- GPU: the whole category's kinds 56 MiB after five builds (budget 200 MiB).

## Left to do (honest)

- Overgrown lot still reads half like a park from 45 degrees (lawn green under the scrub; paint slot 2 under thickets is
  subtle). Needs a meadow look: a straw-coloured weed kind laid densely, or fewer, bigger thickets.
- Vacant lot is plausible but plain.
- Construction sites have no machines (the game's vehicles are not placeable props); the mud tracked out of the gate does
  not show in the close-up yet; "frame" stage (a building going up) has no vanilla pieces.
- Building ruins: the basement pit is not obvious in the pictures (the floor slab covers it); the camera never found it.
- Broken highway: pillars and fallen spans only (no vanilla deck to stand on the pillars).
- Not done: under-the-overpass spaces and leftover strips (verges, medians, rail edges) as types.
