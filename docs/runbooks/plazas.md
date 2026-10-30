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

- **Colours drew a gamma too bright in the game (found and fixed 2026-10-30).** The lab (`suite/scenarios/lab-plazas.json`)
  laid kinds on open ground: a black and a dark brown blob drew right, every mid tone drew pale (concrete stains as pale
  grey, olive leaves as yellow, grime as haze). The albedo texture was sRGB-flagged but sampled without decoding;
  `SharedDecals.LinearAlbedo` now uploads linear values in a linear texture (GeneratorVersion 7). Also fixed: the mask's blue
  follows the coverage (it was 255 over the whole box; no visible change). Ruled out first: render order (orders 5-6, no
  change), texel size, smoothness, the decal layer, an albedo-only material. The sports park looks as it did, a touch richer,
  same frame time. On bright paving, thin dark film kinds still read softly from the player's height.
- The park's summer leaves read as yellow-orange stains on grey paving and even on grass: squares use their own darker
  olive-brown set (`leafSquareSummer`). `leafWet` is the autumn palette: never under summer trees.
- A grungy square on pale concrete still reads clean: the ground colour carries most of the vibe. Dark tiles, asphalt
  joints every 8 m, a pale concrete skate floor.
- A design's whole box should be used: the first theatre sat at one end with 80 m of empty lawn; the first big market
  left three quarters of a 220 x 200 square empty (rows now fill the square, fewer stalls = wider aisles).
- A Christmas market as a field of scattered tables read empty: it is the market square in winter (stall rows round a big
  spruce, snowmen, lights).
- On a slope the theatre turns so its seats climb the fall (it faced across the slope with a cliff at one end).

## Numbers

- Frame time (game runner, 220 x 200, camera on the whole square): grungy 2066 instances, 7.42 ms with the ground,
  7.40 ms switched off; dining terrace 7.64 vs 7.41 ms (runs `send-ground-plazas-1030-002415`, `-020434`).
- Instances: a city block 450-900, 220 x 200 about 2000 (budget 1.6 × 320 a hectare). Weathering on a worker: 40-370 ms
  on a block (piazza with many paving pieces the slowest), 2 s on 420 x 380 m.
- GPU: the plazas family 11 kinds, 56 variants, 17.9 MiB (whole library on this branch 186.3 MiB of 200; nature's family
  is not on this branch).

## Runs that prove it

| Run id | What it shows |
|---|---|
| `show-30-builds-1029-233044-f16f` | BEFORE: every style of every type on a block (45, close) |
| `show-5-builds-1029-235643-9acc` | first grungy and first ground: too faint, pale concrete, skate props missing |
| `show-6-builds-1030-000714-ccab` | first stone theatre (terrain map: a smooth 4.3 m cone), designed market, grungy on its first palette |
| `show-5-builds-1030-001050-c135` | first transit forecourt (a forest: thinned), first fair |
| `send-ground-plazas-1030-020434` | FINAL frame time with and without the ground, close looks by kind |
| `show-20-builds-1030-020329-c08e` | FINAL: every shown style on a block, 45 / close / low / terrain, 20 of 20 built |
| `show-6-builds-1030-020426-5939` | FINAL: market, big market, theatre, Christmas market, fair, grungy on 220 x 200 at 5 % |
| `send-lab-plazas-1030-015243` | the plaza kinds on open ground: film layers faint, leaves strong |
| `send-lab-plazas-1030-021710` | the cause: black and dark brown blobs draw right, every mid tone pale |
| `send-lab-plazas-1030-031221` | after the linear-albedo fix: stains, bare patches, leaves in their true colours |
| `send-ground-sports-1030-031347` | the sports park after the fix: as before, a touch richer; 7.35 ms with the ground, 7.15 ms off |
| `show-16-builds-1030-032202-aee7` | ROUND 2 FINAL: every shown style on a block (45 / close / low / terrain), 16 of 16 built |
| `show-7-builds-1030-032347-e4ee` | ROUND 2 FINAL: plaza Standard, grungy, theatre, transit, market, Christmas market on 220 x 200 at 5 % |

## What failed and why

- Paving joints of 0.24 m: the paving kit opens needles under 0.375 m, so the grungy square's slab grid vanished. 0.5 m.
- Skate props placed after the pit was taken in the occupancy: none built. Placed before.
- The Wear test (clean < lived-in < gritty) failed while the shared cap thinned every square: squares got their own budget.
- Render-queue guess for the faint decals (orders 5-6 and five copied kinds): no change; the copies were removed again.
- A transit grove on a 9 m grid with the bosque leftover read as a forest; 13 m grid, no leftover planting.

## Left to do

- The squares' grime strips and gum read softly on bright paving even with correct colours: wider and darker kinds would help.
- The mall park stays hidden (a shops type, needs growth).
- A big transit forecourt (220 x 200) is still a large grove even with cross walks and lawn islands; the islands read only close up.
