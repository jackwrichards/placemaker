# Runbook: Plazas (the plazas makeover, 2026-10-29)

The Plazas category (`Recipes/shelf/plazas.json`): plaza, market square, and the three types moved in for the makeover:
amphitheatre (from Parks), transit plaza (from Transport, hidden before) and fairground (from Shops, hidden before).
Brief: `docs/refactor/MAKEOVER-BRIEF.md`. Read `docs/GROUND-RUNDOWN.md` and `docs/runbooks/sportsPark.md` first.

## Status

- Grade from the user: not graded yet (before: shops & squares mostly F, market square D, amphitheatre F; plazas B+).
- Branch: `makeover-plazas` (see git log).
- In the user's game: no.

## What the user asked (2026-10-29)

- "Plazas ... different types, like a grungy plaza or a nice plaza, but it's all done with the settings, right? And I guess
  some of them, grungy or nice, might be more than just what settings can do." → the **Wear** choice (Clean / Lived-in /
  Gritty, already in every type, 12 languages) is the setting for every square's ground; the **grungy square** is its own
  design with its own contents.
- "Neglect is more than stripping away features or adding features. It changes the entire vibe of everything." → the
  grungy square has its own plan (a skate pit, concrete planter boxes with dead trees and wild growth, a kiosk with its
  clutter, a poster wall, a dry fountain, jersey barriers) and its own ground (skate scuffs, weeds, concrete stains, gum,
  litter blown into the corners) on dark weathered concrete.
- Cracks, joints, stains, worn paths across paving, dirt where paving meets planting, leaves under the trees matching
  them, the game's own props used richly; a photograph of real life, no lag; fewer, better styles.

## What is on the shelf

| Type | Shown | Hidden (why) |
|---|---|---|
| plaza | grand, rooms, terraces, sunkenPlaza, dining, grandSteps, **grungy** | piazza (an empty bullseye with cafes at the edges), railed (a twin of grand), marketHall (the market square does it), modern (scattered); the older ones as before |
| marketSquare | weekly, bigMarket (both the designed market) | farmers (a third market of the same kind) |
| amphitheatre | **theatre** (the stone theatre, now the type's default) | classic, cinema, bandstand, meadow, speakers (the old bowl in square terrain cells: the F) |
| transitPlaza | busPlaza, bikeHub, parkRide (the designed forecourt behind the kerb lane) | taxiRank, forecourt (twins) |
| fairground | fairground (summer fair), christmas (Christmas market) | festival, funfair, foodTrucks (assets nobody has, or sparse) |
| mallPark | (type hidden) | a shops type round a park: its look depends on shops growing on its plots, not a square |

## The pieces

| Piece | Where | What it does |
|---|---|---|
| The ground of a square | `Core/Decals/Dynamic/PlazaGround.cs` | reads the composed plan (paving pieces, lawns, walls, trees, benches, cafes, stalls, bins, lamps, gates) and lays the shared library from causes; hooked in `Weathering.Wants/Apply` (two small additive edits) |
| New decal kinds | `Recipes/decals/library/plazas.json` | jointWeeds, weedTufts, bedSpillStrip, pavingWearStrip, cafeStains, gumField, skateScuffs, concreteStain, drainStreak |
| Designed squares for other types | `PlazaLayer.PlanDesignedSquare` | lays a `PlazaDesigns` design (paving tiling, access points, regions, stats) for the market square, the amphitheatre, the transit plaza and the fairground |
| Grungy square | `PlazaDesigns.Grungy.cs` | the design and its dark-concrete palette (`GrungyPalette`, used when the palette is Automatic) |
| Stone theatre | `PlazaDesigns.Theatre.cs` | a smooth cone of sloped terraform quads (one per 6°), a stone lip every tier, radial stairs, a bank behind, orchestra, stage, lawn and trees |
| Market | `PlazaDesigns.Market.cs` | streets on the axes, a market cross, quarters of stalls in rows back to back (food stalls, produce stands: a table under a parasol with crates, carts, gazebos), tree ring, cafes |
| Transit forecourt | `PlazaDesigns.Transit.cs` | a canopy along the platform, a grove with benches and bike racks, a focal point; the layer keeps the kerb lane, bays and park & ride |
| Fair | `PlazaDesigns.Fair.cs` | a midway of stalls on a green, a stage end with deckchairs and blankets, a play corner on sand; the winter version on setts with spruces |

## The ground: causes and what each lays down

| Cause | Where | Kinds |
|---|---|---|
| Tone | the hard ground (minus big lawns), big lawns, bare ground | `pavingTonePatch` (0.26-0.42), `concreteStain` crisp stains in loose groups, `lawnTonePatch`, `groundMottle`, `dryGrass` |
| Joints | every paving piece edge once (dedup by midpoint), classified by what lies either side | hard-hard: `kerbGrimeStrip` runs, `jointWeeds` on a share (more when Gritty); paving against lawn: `bedSpillStrip` on the paving, `pathEdgeWear` on the lawn; the outline: `kerbGrimeStrip` |
| Walls | wall pieces joined into lines | grime both sides, moss and weeds on the shady (north) side |
| Trees | by species (summer kinds) | leaves under and downwind (full colour), `leafWet` stuck flat on the paving, `barkMulch`/`mossPatch` round pits, droppings, drifts against walls downwind |
| Feet | entrances to the centre; benches, cafes, stalls, bins | `pavingWearStrip` lanes, `gumField` and butts at entrances and benches, `cafeStains` under tables, spills and litter at stalls and bins |
| Water | real low spots of the built ground (+2.5 cm settling) | `puddlePiece` fill (2 wet, the rest tidemarks or damp), `drainStreak` toward them, damp under crowns |
| Age | big slabs; lamps, bollards | `crackConcrete` chains, `weedTufts` in cracks when Gritty, rust, droppings |
| Neglect | Gritty, or the grungy square | `concreteStain`, `skateScuffs` at ledges, benches and skate pieces, `weedTufts` at the edges, `jointWeeds` round the outline, litter in the corners |
| Flaws | 1-2 per site | big puddle, gum corner, leaf-choked corner, mossy corner |

Budget: a square is all detail: `PlazaGround.BudgetFactor` 1.6 × the shared per-hectare cap. Joint and spill strips have a
metre budget of their own so a square of many small pieces (garden rooms) never spends everything on its seams.

## Look rules the pictures taught

- On the pale vanilla paving everything reads fainter than on a lawn: the first run (decals at sports-park strengths) was
  invisible from 100 m. Tone and grime go up on paving; leaves go full colour; crisp stains (concrete stains, gum fields)
  read where soft tone does not.
- A grungy square on pale concrete still reads clean: the ground colour carries most of the vibe. Dark weathered concrete.
- A design's whole box should be used: the first theatre sat at one end with 80 m of empty lawn.

## Runs that prove it

| Run id | What it shows |
|---|---|
| `show-30-builds-1029-233044-f16f` | BEFORE: every style of every type on a block (45, close) |
| `show-5-builds-1029-235643-9acc` | first grungy and first ground: too faint, pale concrete, skate props missing |

## What failed and why

- Paving joints of 0.24 m: the paving kit opens needles under 0.375 m, so the grungy square's slab grid vanished. 0.5 m.
- Skate props placed after the pit was taken in the occupancy: none built. Placed before.
- The Wear test (clean < lived-in < gritty) failed while the shared cap thinned every square: squares got their own budget.

## Left to do

- (filled in at the end)
