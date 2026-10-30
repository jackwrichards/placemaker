# Ground runbook: sportsPark (Sports park)

The first type on the shared decal library (docs/DECALS-PLAYBOOK.md). It built the general pieces every later type
reuses: library instances in the plan and on the record, brushes, end caps for strips, the boundary passes, and the
causes ported from v1 strokes. Read the playbook first; this page is what was done for sports parks and what was learnt.

## Status

- Grade from the user: **B+** (2026-10-29, after the first game pictures; before: C, "the most plasticky")
- Branch / last commit: `claude/sports-ground-a0e92d` (see git log)
- In the user's game: no

## The pieces (built here, for every type)

| Piece | Where | What it does |
|---|---|---|
| Instances | `Core/Decals/Dynamic/DecalInstance.cs`, `AreaPlan.DecalInstances` | kind, variant, x, z, yaw, stretch (x, z), opacity; the cause for logs and tests |
| Record codec v2 | `DecalCodec.cs` | v1's strokes, then a kind-name table and 18 bytes per instance; v1 records still read. A sports park is 10-55 KiB |
| Game side | `DynamicDecalSystem.Start/Drop`, `SharedDecals.Place(…, float2 scale …)` | instances placed under owner `g<group id>` when the record is finished, cleared with the group (undo, bulldoze, Edit, ungroup, another city) |
| Library loader | `DecalLibrary.Loader`, `EnsureCurrent()` | plans run on workers before the decal system has read the library: the first plan reads it |
| End caps | `DecalVariants.CapEnd/CapStart` (index 100 + i, 200 + i) | a strip's or crack chain's variant i fading out toward +x or −x (same pixels times a ragged ramp): runs never end in a hard cut |
| Spans | `DecalVariants.SpanOf/FootprintOf` | the box an instance draws and its visible footprint, without baking (brushes size pieces with them) |
| Brushes | `Core/Decals/Dynamic/Brushes.cs` | `Scatter` (cause field, clumps of clumps, loners), `Tone` (big faint patches on a jittered turned lattice), `EdgeStrip` (runs with gaps, cut at corners, a whole number of repeats stretched to fit, caps at both ends, heavy side left or right), `CracksAlong` (chains, variants mixed), `Fill` (a hollow), `Under` (canopies, downwind), `At` (a hot spot with satellites) |
| The design | `Core/Decals/Dynamic/Weathering.cs` | reads the plan (paths, aisles, fences, surfaces by role, trees by species, goals, stands, benches, bins, bays, steel) and lays tone, trees, seams, play, feet, water, wheels, objects |
| Offline look | `Tests/Placemaker.Tests/GroundDesignTests.cs` with `PM_WRITE_GROUND=1` (`PM_GROUND_ONLY=community`) | the plan from above with the real variants (lawn, surfaces, terrain decals, paths, road decals) and close-ups per cause, in `.forensics/ground/`: seconds, not a game launch |
| Format | `tools/decals/format-library.js` | writes family files the way they are hand-written, so a script that adds kinds leaves a small diff |
| Game scenario | `suite/scenarios/ground-sports.json` | frame times before / with / without the ground, instance and memory counts, close-ups by kind |

## One look, decided first (the user: "crafting it as one thing, not decals slapped on after")

Before any brush runs, the design knows the whole site and every layer agrees with the others:

- **Season and leaves.** `Weathering.Season` ("Summer"): leaf kinds have a summer version (`leafMixedSummer`, `leafOakSummer`
  …: olive, green and brown, the odd yellow) and `Leaf(kind)` picks it. Autumn orange under green trees looked wrong to the
  user. Each tree's species picks its leaf kind (birch, oak, plane, maple, petals, needles); palms drop none.
- **Moisture.** The painted lawn's lush and dry clumps (`GroundWear.LawnMoisture`, the same seed) also place the dry-grass
  and clover decals: paint and decals never disagree about where the lawn is dry.
- **Surfaces decide the wear kind.** Cracks follow the material they lie on (`crackAsphalt` on tarmac and hard courts,
  `crackConcrete` on paving, `crackSealed` on old car parks); clay courts get clay spill, not grit.
- **Formality decides the colour level.** Turf and paving use the quieter variants, leaves and wild corners full colour.
- **What the game draws over what.** The game draws its own surfaces (turf, paving) as decals at the template queue: every
  library decal is drawn above them (`SharedDecals.Upload`), or it vanishes under the turf.

## The ground plan (top to bottom)

| Area | Ground | Paint | Surface | Decals |
|---|---|---|---|---|
| Park lawn (between everything) | painted terrain (`dressLawn` default is now Painted) | lawn tint (lush/dry clumps), lived-in mulch under crowns | none | `lawnTonePatch` tone everywhere, `groundMottle` clumps, `dryGrass` toward the open edges and away from shade, a little `cloverPatch` in the shade |
| Pitches, infields | grass surface (turf) | none (paint cannot show through) | turf | `mowingStripes` (5.5 m bands, subtle), a few `grassClippings`, one bald goalmouth in a halo of thinned grass, a thinned spine, spots, linesmen. No tone patches |
| Courts | asphalt or clay surface | none | court | `asphaltTonePatch` / `dirtTonePatch` faint, `seamGrime` along the edge, cracks across hard courts, grit and leaves in the downwind corners, `claySpillStrip` round clay courts |
| Paving, terraces | paving surface | none | paving | `pavingTonePatch`, `kerbGrimeStrip` inside the edges |
| Car park | asphalt surface + aisles | none | asphalt | `asphaltTonePatch`, cracks a step in from the edges, `crackWeb`, a pothole, tyre tracks in both lanes, oil at the busy stalls, puddles in the low points |
| Paths | nets | none | none | `pathEdgeWear` on the grass side (terrain only, never on the path), mud and prints at the gates, puddles in the dips, leaves blown across |
| Under trees | painted terrain | lived-in mulch (forest floor) | none | leaves of the tree's own kind (smothered), mixed leaves blown on, `woodlandFloor` and a little bare earth under groups, drifts against the first barrier downwind, leaf piles |

## Causes and what each lays down

| Cause | Where (how found in the plan) | Kinds, coverage, opacity |
|---|---|---|
| Tone | the lawn (site minus every surface), each turf, paving, asphalt, court | `lawnTonePatch` 0.45-0.75; patches on a lattice at 0.6 × their narrow side, every point covered |
| Trees | `ObjectRole.Tree`, species from the asset id (birch/alder/poplar → birch, oak, plane/linden, chestnut/hickory/maple, apple → petals, conifers → needles, palms none, else mixed) | `Under`: fall zone 2.6 crowns + 3 m, a crown downwind; coverage 1.1 (needles 0.8, petals 0.55) × Grime; mixed leaves 0.35 a further 1.8 crowns on |
| Wind | from the south-west, `Weathering.Wind` | rays from every tree to the first path edge, court, paving, asphalt or fence; hits along one barrier merge into a drift (`leafDriftStrip`), the heaviest get a `leafPile` |
| Play | goal pairs (25-140 m apart) | goalmouth `barePatch` 12-16 m, crease `mudPatch` 6-8 m, penalty spots, centre, `barePatch` spine by a midfield field, linesmen `wornPath` |
| Stands, dugouts | `SportParkStand*`, `SportParkBench*`, the busiest 8 | `barePatch` 14 m / `mudPatch` 7 m in front, toward the pitch |
| Feet | gates = path ends at the outline | `mudPatch` and `footprintsMud` at each gate, mud where the path is nearest each pitch, `wornPath` shortcuts when the paths go the long way round, bare ground and litter at the 14 busiest benches on the grass, litter at bins |
| Water | local minima of the built ground (+ 2-3 cm unevenness) along paths and on asphalt | `Fill` a wobbly ellipse with `puddlePiece` (3 wet), `dryTidemark` / `dampPatch` (4 dry) |
| Wheels | asphalt surfaces, aisles, bays | see Car park |
| Objects | lamps, `Lightpole*` | `rustBleed` at the feet of 45 % |

## Seams

| Seam | Strip kind | Side | Notes |
|---|---|---|---|
| Path edge | `pathEdgeWear` | heavy side (+z) toward the path, centre 0.8 m beyond the edge | gaps 25 %, runs 12-40 m |
| Paving / car park edge | `kerbGrimeStrip` | heavy toward the edge, 0.3 m inside | closed rings, gaps 35 % |
| Court edge | `seamGrime` inside, `pathEdgeWear` outside | inside heavy toward the edge; outside heavy toward the court | |
| Turf edge (pitch) | `pathEdgeWear` outside | heavy toward the turf | gaps 55 %, faint |
| Fence | `fenceLine` | either | gaps 20 % |
| Clay court | `claySpillStrip` outside | heavy toward the court | new kind |

Strip side rule: `StripOptions.Side` +1 puts the heavy side on the left of the line's direction of travel; a CCW ring has
its inside on the left. `Offset` moves the strip toward its heavy side (negative: away).

## Signature flaws (1-4)

- Picked per site from the seed: bald goalmouth (1.35× and more satellites), big puddle (1.9×), leaf-choked corner (the
  heaviest drift wider plus a big leaf pile), the well-worn shortcut (unbroken, stronger).

## New or changed library kinds

| Kind | Why | Browser verdict |
|---|---|---|
| `lawnTonePatch`, `pavingTonePatch`, `asphaltTonePatch`, `dirtTonePatch` | the tone sheets (`xy`) have a hard square edge when laid alone; these are the same looks as big soft patches with ragged fades | not shown yet |
| `claySpillStrip` | clay kicked off a court onto the grass | not shown yet |

## Settings and numbers

- Instances per build (Grime 100 %): 700-1500 on a city block, 1200-3100 on 220 × 200 (cage courts the most: many
  rings of kerb, court and fence seams). Under `Weathering.MaxInstances` 9000.
- Record: 10-55 KiB. Weathering time: 30-500 ms on a worker (most of it the cause fields of `Scatter`).
- Touched by causes (not the tone): 40-60 % of the site; strong (not counting leaf carpets): 3-13 %.
- Grime 0 lays only the tone; 200 % lays more and stronger (coverage × min(2.2, amount), opacity scaled up to 1.3-1.4×).

## Frame time and memory (game runner, community park on 220 x 200, camera on the whole park)

| Drawing | Instances | Frame (median) | Ground switched off | Cost |
|---|---|---|---|---|
| one renderer each, property block per instance (size, opacity) | 1999 | 8.76 ms | 7.18 ms | +1.6 ms |
| same, half the tone overlap | 1983 | 8.68 ms | 7.07 ms | +1.6 ms (overdraw was not the cost) |
| same, a render-queue slot per variant | 1528 | 8.77 ms | 7.74 ms | +1.0 ms (material switches were not the cost) |
| RenderMeshInstanced per variant and opacity step | 1528 | 7.78 ms | 7.84 ms | draws **nothing**: CS2's DefaultDecal is fed by the game's own batching |
| **unit box scaled by the transform, shared material per variant and opacity step, no property block** | 1528 | **7.87-8.06 ms** | 7.84 ms | **+0.03-0.2 ms** |

Lesson: a `MaterialPropertyBlock` per renderer stops Unity from batching (about 0.7 µs per renderer). Put the box size in
the transform's scale and the opacity in a few shared step materials. Library memory: 306-310 variants, 52-55 MiB on the
GPU for a whole park (budget 200 MiB for the city); 5.5-9 s of worker time the first time, 0.1 s from the disk cache.
Weathering (planning, on a worker): 30-500 ms; it runs on every preview re-plan.

## What the user said

- 2026-10-29 (brief): plan paint, surfaces and decals as one vision; surfaces only for courts, pitches, paving, car
  parks; the park ground painted terrain, not a whole-ground grass surface; far more decals (10-20× v1), large but subtle,
  never organised; leaves are their favourite; boundaries are where the detail goes.
- 2026-10-29: close test games yourself as soon as you have the pictures; don't wait out the idle timeout.
- 2026-10-29 (B+): "the leaf colour doesn't look quite right with the tree colour" (autumn leaves, green trees) → summer
  leaf kinds; "tone the dirt on the field ever so slightly more transparent" → pitch wear opacity ×0.8; "more cracks, rubble,
  speckled dirt versus mud" → paving cracks, a second crack ring in the car park, 2-3 cracks per hard court, grit beside
  paths, pebbles instead of mud at creases and gates; "it doesn't mean more or less decals, it means correct"; "maybe the
  trees need more variety too" (left: the planting is the composer's, see Left to do).
- 2026-10-29: the per-renderer cost: "optimize plus pick a reasonable amount per area" → no property blocks (above); a
  budget per hectare only if a later type needs it.

## Runs that prove it

| Run id | What it shows | Pictures |
|---|---|---|
| `show-10-builds-1029-154915-8109` | BEFORE: every style on 220 × 200, v1 grime on community only, grass-surface lawns, tennis nets along the courts | `shots/02-sportsPark-community-big.05-45-clean.png`, `04-…tennisClub…07-close-clean.png` |

## What failed and why

- The pitch looked like flat plastic even with tone: its tone and stripes (orders −2, −1) drew under the game's turf decal.
- Mowing stripes 3 m wide and faint never read from the player's height; 5.5 m bands like a real pitch, rows side by side
  with no fade across (a fade drew a dark line at every row seam).
- Turf mottle and scattered bare spots on the pitch read as yellow blotches; ring-fade patches as donuts. Pitch wear is one
  bald goalmouth in a halo of thinned grass and a continuous thinned spine; brushes never use the ring fade.
- Big soft tone patches on the turf looked smudgy (the user: "a waste of decals, I'd rather that go into more fine-grained stuff"): tone belongs on large uneven ground (lawn, asphalt) at low opacity; on a mown pitch the stripes are the tone.
- A dry-grass edge strip round the turf lay on the running track outside it and read as pale haze: a seam strip must know what lies on its far side.
- The sports park's automatic trees were one species per park (the automatic pool picks one per step): now a mix of the game's broadleaf trees in clumps.
- A grid mask for BrushArea made planning 50x slower (built per area, many small areas): measure before optimising.
- First pass: leaves were a few lonely blobs (the fall zone, 1.6 crowns, was smaller than one leaf patch, 8-13 m, so
  Scatter placed one or two per tree). Fixed: a fall zone of 2.6 crowns + 3 m, coverage 1.1.
- Path edge wear sat mostly under the path (centre 0.35 m beyond the edge, heavy side toward the path). Fixed: 0.8 m.
- Bench wear in front of every seat of an amphitheatre (142-262 patches: one per object). Fixed: the 14 busiest benches
  on the grass only.

## Left to do

- Tree variety (the user): the sports park plants one or two species per belt; a mixed planting would give mixed leaves.
- Show the new kinds (tone patches, clay spill, summer leaves) in the decal browser.
- The five `Types.SportsParkTests` that fail (RespectsReservedSpace, EveryPresetPlans…, FieldsFill…, Facilities…, PresetsLookDistinct) fail on the base commit 62e845d2 too: not from the ground work.
- A season knob from the city (summer / autumn leaf sets), when the game's season is readable.
