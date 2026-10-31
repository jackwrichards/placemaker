# Runbook: Biome mix (natural areas inside a place)

A shared core feature (2026-10-30): a site split into natural-looking areas of different character, tuned by the player as a
mix of percentages. Parks first; Phase 2 agents (farms, beaches, golf, nature) offer it on their types and add biomes.

## Status

- Grade from the user: not yet.
- Branch: `claude/biome-mix-206a9b`. In the user's game: no.

## What the user asked (2026-10-30)

> Parks are good, but even when I crank rocky outcrops up all the way, it's not as rocky as I want. I want almost biomes
> inside of a park that I can tweak, or maybe just a spread … percentages of each … it would use Perlin noise or something …
> a super rocky area or a super forest area or just a mulch or a flower field … each subtype could tweak them to their own
> desire or be a split or just be one or the other.

## The audit: what existed and why it fell short

| Piece | Where | What it did | Why it fell short | Now |
|---|---|---|---|---|
| Places with moods | `Core/Layers/Park/ParkMoods.cs` | 1-14 places per park, one per lawn room: gully, rocky knoll, wet hollow, meadow, copse, standing stones, shaped and planted | fixed set picked by the land, no player control over how much of each; one mood per room at most; read as small accents from 45° | retired behind the mix: a park, botanical garden, memorial park or adventure playground with anything natural in its mix plans biomes instead; `moods` / `moodGround` hidden (still read: a mix turned down to lawn alone plans moods as before) |
| Rocky areas (rocky outcrop) | `Core/Layers/AddOns/RocksAddOn.cs` via `LookSettings` `lookRockyAreas` | at "many": 8 clusters of up to 12 rocks on a painted patch | 2-3 % of a park; the before run `send-biomes-before-1030-211200` shows "many" as a handful of grey stones | hidden on the park (`ControlsShown`): the mix's Rocky ground does it (100 % = 40 outcrops, 400 boulders, 1500 stones, ground lifted 3.6 m on a 220 x 200 site). Saved designs keep their rocks child |
| Water none / pond / lake | `LookSettings` `lookWater` | the park's designed lake (islands, boathouse, promenade) | a facility-like lake, not natural ponds | kept (it is the designed lake); the mix's Ponds are natural ponds in the low ground |
| Nature groves, glades, rocks, logs | `Core/Layers/Common/NatureKit.cs` | clumped planting, boulder groups, fallen logs | nature types only | reused by every biome (`NatureKit.Plant` / `Fallen`) |
| Ground shape and patches | `GroundSection`, `GroundPatchesAddOn`, `TerrainShaper` | whole-site landforms, painted patches | not areas of character | unchanged; with the Terrain feature on the biomes plant but leave the ground alone |

## How it works

- `Core/Layers/Common/Biomes/Biome.cs`: the library. A `BiomeDef` is a complete ground design as numbers (where it likes
  to be, terrain lift/dip/roughness, paint, trees, bushes, outcrops, boulders, stones, logs, ponds, pools, decal passes).
- `BiomeField.cs`: the split. Grid of 2.5-8 m cells over the natural ground (off the paths' 3 m verges, the lake, the plaza,
  the facilities' clearings, the entrances). Per biome: domain-warped fBm (wavelength from Patch size: 18 m .. the site's
  size) plus the land (ridge/hollow from the height against a 25 m mean, steep, slope, flat, near a path, near the outline).
  Biases per biome are solved so each gets its share; a majority filter rounds the patches; specks under 250 m² join their
  neighbour. Water and marsh keep off paths (a wandering distance) and steep ground.
- `BiomeBuilder.cs`: lays each biome: one Sculpt (rocky knolls up to 3.6 m, marsh dips, roughness; every change fades
  across the area's edge and off the paths, ≤ 28 % grade), ponds and marsh pools (terraform + water source), trees first,
  then outcrops/boulders/stones (boulder fields gather by a clump field and on the knolls, thin to the edge: rock, scree,
  grass), then bushes (the rim band gets more: forest edge scrub), logs. Paint as soft grid fields. Regions `biome:<id>`.
  The host keeps off the areas (their coarse outlines are taken in its occupancy).
- `Core/Decals/Dynamic/BiomeGround.cs`: each area's decal passes (All, Core, Rim, Downhill, Shade, Things, Spill; Fill).
- `BiomeMix.cs` / `BiomeHost.cs`: the controls (a slider per offered biome, group `biomes`, + `biomeScale`) and the two-line
  hookup for a type.
- Panel: `UI/src/controls/BiomeMix.tsx`, the Landscape card between Parameters and Ground: a bar of the mix (drag the
  dividers), a line per biome in the mix (slider; the others give or take in proportion), "Mix in" chips, Patch size.

## Biomes (starters)

| Biome | Where it likes | Ground | Paint | Planting and rocks (per ha) | Decals |
|---|---|---|---|---|---|
| lawn (host) | flat, by the paths | the type's own | the type's own | the type's own | the type's own |
| meadow | flat | – | earth 0.22 | 6 solitary trees, 28 scrub (rim ×2.2) | longGrass (new), dryGrass, meadowFlowers, molehills |
| flowers | flat, by paths | – | forest floor 0.4 | 2 trees, 45 low shrubs | wildflowerDrift (new), meadowFlowers |
| mulch | by the paths | +0.25 m | forest floor 1.0 | 30 small trees, 230 shrubs | mulchBed (new), barkMulch |
| glade | slopes, flat | – | forest floor 0.45 | 55 trees in groves (30 m clumps), 55 scrub, 3 logs | dryGrass, meadowFlowers, leaf litter at the rim |
| forest | slopes, the outline | +0.45 m, rough | forest floor 1.0 | 260 trees (9 m clumps, 6 % dead), 150 undergrowth (rim ×2.6), 9 logs | woodlandFloorCarpet, leafLitterWild, twigs, moss, exposedRoots |
| rocky | ridges, steep | knolls to 3.6 m, rough 0.6 m | earth 0.7, core 1.0 | 36 conifers (15 % dead), 12 outcrops of 5-9 boulders, 60 boulders, 200 stones | rockOutcrop, stonesCarpet, screeApron, screeFan, gravel spill, moss |
| heath | ridges, slopes | +0.6 m | forest floor 0.6, sand speckle | 12 pines/birches, 230 heather | heathPatch, sandSpill, pebbles |
| wetland | hollows | dips 0.55 m, pools of water (22 % of its heart) | forest floor 1.0 | 22 wet trees, 300 reeds | marshMud (fill), sedgeLitter, damp, puddles |
| water | hollows, away from paths | ponds 1.2-2.4 m deep, banked | forest floor 0.9 | reeds 0.3-5 m and wet trees 2.5-9 m round the water | shoreMud, sedge on the bank |

## Lessons (the pictures taught)

- The terrain paint slots: 1 "dry grass" is invisible on vanilla maps; 3 earth reads light tan; 4 sand reads pale, near
  white from 45° at strength. Dark grounds (mulch, marsh, heath) use slot 2 (forest floor).
- Boulders spread evenly read as props on a lawn: gather them (a clump field, the knolls' tops) with open ground between.
- Lifting a whole area reads as a plateau: a low swell (12 %) and knolls toward the heart.
- Plant trees before rocks, or the rocks take all the ground (rocky: 39 of 100 trees before the reorder).
- The style card keeps the player's edits on a style (like a file): a test that sets a mix on "natural" leaves the next
  "natural" build with that mix. Runner checks put custom mixes on a hidden style (greenway).
- A type's own ponds stay dry in the runner until the simulation runs: water checks `grow` 45 s after the build.

## Runs

| Run id | What it shows |
|---|---|
| `send-biomes-before-1030-211200` | BEFORE: natural default, rocky areas at "many" (barely different), wild, rocky max on hills |
| `send-biomes-r1-1030-213254` | first look: 100 % rocky (even spread), forest (a real forest), flowers |
| `send-biomes-r2-1030-213815` | every biome at 100 %, natural, wild, garden |
| `send-biomes-r3-1030-214510` | ponds fill (grow 45 s), meadow with long grass, mulch, heath; botanical, memorial, adventure (nature), woodland |
| `send-biomes-r4-1030-215002` | 400 x 300 natural, city block, hills (natural, wild), mountains (rocky park), marsh pools hold water |
| `send-biomes-perf-1030-215319` | frame time and removal (below) |
| `send-biomes-r5-1030-222507` | after the speed-ups: 400 x 300, wetland, rocky, rocky park on hills; the Landscape card in the panel |

## Frame time, memory, planning, removal

- `send-biomes-perf-1030-215319`, natural park 400 x 300 (5 854 decals; 25 800 city-wide with the earlier builds): median
  frame 8.89 ms with the ground, 8.72 ms switched off (+0.17 ms). GPU: 429 variants, 96.5 MiB of the 200 MiB budget.
- Removal: 2 934 pieces built, 1 left after bulldozing (the entrance path's node at the street, the same joint every type
  leaves; not a biome piece). Rocky park 220 x 200: the same.
- Planning in the game (220 x 200): 0.7-1.7 s on the worker, the biomes 90-440 ms of it. 400 x 300: 2.5-2.9 s.
  Tests: 36 ha park compose 3.7 s (was 11.2 s before `BrushArea.Contains` checked Keep and ring boxes first); adventure
  playground 5 ha 200-390 ms (budget raised to 450 ms: it plans a landscape now).

## Open

- Not graded by the user yet. Wetland pools read grey-blue from 45° (shallow water over dug ground); a darker marsh water
  decal could help.
- Mulch reads warm orange-brown; flower field reads mostly yellow (purple/red drifts are small at 45°).
- Paths are planned before the split: the areas adapt to the paths (verges, water keeps off), the paths do not route
  round the areas yet.
- Zen garden and amphitheatre have no mix (their grounds are designed: raked gravel, terraces).

## Round 2 (the lead's review and the user's colour notes, 2026-10-30)

The lead: wetland read as beige sand blobs; meadow, flowers and mulch as paint noise; the mixed park as confetti; ponds as
canals between paths; heath and glade not A. The user: B+; the yellow ground "like someone peed on it", the orange like rust.

- **Wetland**: the pools' beds were sand (the water role's default surface pool starts with sand): ponds and pools now lie on
  the dark forest-floor surface; pools dug 1.3 m (deeper reads as dark water); 2200 reeds/ha massed 0.3-5 m round the water,
  tussocks, alder carr at the rim; shore mud strips along the water.
- **Plant mass**: biomes have plant layers (`BiomePlants`: ids, per ha, scale range, where: all, core, rim, water, beds,
  drift). Vanilla has no tall-grass or wildflower props (lab `send-lab-biome-plants-1030-223356`): tussocks and heather are
  wild bushes at 0.45-0.9 scale, flower clumps the city flowering bushes in drifts.
- **Flowers**: five single-colour drift kinds (`flowerDriftYellow/Purple/Red/Blue/Cream`, cream never white), each laid in
  its own noise drifts (`BiomeWhere.Drift`).
- **Mulch**: real beds (`BiomeDef.Beds`: half the area in organic beds, lawn between) on a crisp mulch surface (the modded
  mulch, else the vanilla forest floor), shrubs only inside them.
- **Glade**: trees only in a ring (at most 45 % of the area's depth) round an open heart.
- **Heath**: heather mounds, gorse drifts (the light city shrub), birch and pine, sandy trails (`BiomeWhere.Trails`).
- **Bigger areas**: default patch size 72-78 for the natural styles, fewer small shares per style, specks under 1.2 % of
  the natural ground merged.
- **Paths**: the park plans a first split before its paths; the path planner pays 25× to cross a pond and 3× a marsh's
  heart (`ParkPathPlanner.Options.Avoid`), so walks loop round the water; the final split keeps those areas
  (`BiomeField.PriorBonus`). Rock fields are crossed (avoiding them pushed walks onto the fall line on mountains:
  `ParkPathSlopeTests` wild 228 -> 266 steep pieces, 125 without it).
- **Colours (shared kinds, every type)**: 17 ground kinds recoloured: yellows toward a leafy olive green, oranges toward a
  real brown, all less saturated (leaf litters, woodland floor, bark mulch, dry grass, mottle, lawn tone, meadow flowers,
  heath, long grass); flower hues kept, yellow quieter.
- Runs: `send-biomes-r6-1030-224523`, `send-biomes-r7-1030-225604` (lake with islands, marsh pools), `send-biomes-r8-1030-230147`
  (after the colours), perf `send-biomes-perf-1030-225959` (10 parks city-wide, 27 000 decals: +0.6 ms a frame, 93 MiB GPU;
  removal: 1 leftover, the street-joint node).
- Still open: no boardwalk or bridge where a walk must cross water (at 100 % water a few walks keep a lawn causeway); heath's
  trails are faint at 45°; glade on narrow rooms is more woodland than clearing.

## P2-8 (2026-10-31): full means full, per-biome settings, plain grass and the field

The user: "even when I turn it to full meadow, I still get some trees"; "sometimes I just want a park to be a field ... even a
field should have good looking decals"; "maybe one that is just simpler, literally grass with some leaves, more vanilla";
"the zone tool is a much better one ... maybe each biome can be customized itself ... it needs to be really good UX".

### Full means full (every source of the stray trees)

`FullBiomeTests` (park, botanical garden, memorial park, adventure playground; every offered biome at 100 %, flat and hills)
lists every tree of the plan; all must be the biome's own. Before: 100 % meadow, natural park, 216 x 196: 25 trees.

| Source | Before | Now |
|---|---|---|
| the meadow's own solitary trees (3/ha) and the flower field's (2/ha) | 9 | 0 by default; the biome's **Trees** setting adds them |
| the host's own planting on what the areas leave (verges, entrances, clearings) | 4 trees, ~90 bushes | scales with the lawn's share of the mix (`BiomeResult.HostShare`); 0 at 0 % lawn (park, botanical, memorial, adventure) |
| Big trees (look setting) | 8 | keep off natural areas; none when the lawn has no share (`ReservedSpace.Natural`, `NaturalOnly`, filled by `Composition.Reserve`) |
| a picnic's shade trees, flowerbeds | 4 | same rule |
| memorial allées, a room's specimen tree | kept | the memorial's architecture: their own visible settings (Allées, Room content) |

The rule for any new feature that plants something decorative: check `ctx.Input.Reserved.InNatural(p)`.

### Per-biome settings (`BiomeKnobs.cs`)

Each biome in the mix has 2-4 settings of its own (`BiomeDef.Knobs`, else made from what it has): control
`biome<Biome><Knob>`, group `biome:<id>`, label `Placemaker.Biome.Knob.<key>` (shared). Read only while the biome is in the
mix (otherwise "not used": greyed, skipped by `ControlEffectTests`). `BiomeKnobs.Apply` tunes a copy of the definition; the
field, the builder and the decals (`BiomeGround` rebuilds it from the facts `biomeKnob.<id>.<key>`) all see it.

| Biome | Settings |
|---|---|
| plain grass | Leaves, Trees |
| field | Wear, Long edges, Daisies, Edge trees |
| meadow | Trees, Wildflowers, Grass (short/long/tall), Shrubs |
| flower field | Colours (mixed/warm/cool/purple/yellow/white), Wildflowers, Shrubs |
| mulch | Beds, Trees, Shrubs |
| glade | Trees, Wildflowers, Shrubs |
| forest | Trees, Kind (mixed/broadleaf/conifers/birch), Undergrowth, Dead wood |
| rocky | Rocks, Height, Trees |
| heath | Heather, Trees, Height |
| wetland | Open water, Reeds, Trees |
| ponds | Reeds, Trees |
| farm and beach biomes | made from their data (Trees, Shrubs, Rocks, Height as they have them) |

The park's own planting (tree and bush density, species, the planted edge) is its lawn's: shown in the Lawn box
(`BiomeOffer.LawnControls`), and it no longer scales or replaces the natural areas' trees (each biome plants its own species).

### Plain grass and the field (`BiomeDef.Plain`)

- **Plain grass** (`grass`): the game's own grass, a light scatter of summer leaves, a few lone trees if asked. Nothing else.
- **Field** (`field`): a mown field, an unmown long-grass margin round its edge, faint trodden lines across it, trampled and
  bald patches in its heart, daisies (new decal kind `daisyLawn`) and clover in drifts, molehills, damp on the slopes; leaves
  only with edge trees. References: Clapham Common, the Sheep Meadow (Central Park), Tempelhofer Feld, a village rec ground.
- Both are **Plain**: the shared lawn variation keeps off them (GroundWear's lush/dry paint patches read as dark green
  blotches over half the park; Weathering's lawn tone, mottle, dry grass and clover decals).
- Park styles: **Plain park** (plain grass 100 %, 4 lone trees/ha, no flowerbeds, big trees or picnic) and **Open field**
  (field 100 %, a circuit walk, picnic tables).

Biomes or styles: a biome is a kind of ground that can share a site with others (grass, field, meadow, forest ...); a style
is a whole park (paths, furniture, features) plus a preset mix. Plain grass and the field are both: biomes to mix, and a
style each that is 100 % of it.

### The Landscape card (`UI/src/controls/BiomeMix.tsx`)

The zone tool's bar: names inside the parts, or sticking out above/below on a line in the first free slot (two rows each
side, so small neighbours never cover each other); the white lines between neighbours drag (each keeps 1 %); a click on a
part or its label opens that biome's box (share, Only this, take out, its settings); Add shows the biomes not in the mix;
"Whole place" holds Patch size. Mock-up shown to the user: https://claude.ai/artifact/YDbMHsGohWMLuRxSGKyuNN. Mock test:
`UI/mock/tests/biomes.spec.ts`.

### Runs (P2-8)

| Run | What it shows |
|---|---|
| `send-p2-biomes-r2-1030-235003` | BEFORE: 100 % meadow with trees |
| `send-p2-8-full-1031-132144` | 100 % meadow and flowers: no trees; natural unchanged |
| `send-p2-8-r3-1031-134440` | Plain park (the user: "looks very good"), Open field with decal spots (the user: stains) |
| `show-2-builds-1031-135837-7dc8` | Open field paint only: clean |
| `show-4-builds-1031-140237-354b` | Formal city green (lawn panels, avenues), garden without the pastel shrubs |
| `show-6-builds-1031-135541-3283` | Wildflower meadow: coloured drifts, no confetti |

Shown again on the shelf: Formal and Wildflower meadow. Still hidden: pocket (a wood on a block), meadow, victorian, landscape,
modern, riverside, greenway; the dog park type (a flat brown square). Not done: a Japanese park style (the Zen garden type
covers it), a dog park and a linear park worth showing, the panel's i18n fit test for the new labels.
