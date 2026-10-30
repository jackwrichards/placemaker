# The ground rundown: giving any type a designed ground

For the agent who is told "make the ground of <parks / nature / beaches / parking / plazas / farms …> look real". Read
this page, then `docs/DECALS-PLAYBOOK.md` (the method and the user's art direction) and `docs/runbooks/sportsPark.md` (the
first type done: B+ then "nailed it", every number and mistake). This page is the short, complete how-to.

**The one rule: do more with less.** The user, 2026-10-29: "it doesn't mean more or less decals, it means correct";
"within reason, do more with less". Every decal must have a reason a visitor would recognise (a tree dropped these leaves,
feet wore this edge, rain sits in this dip). Fewer, right, crisp pieces beat many soft ones.

**This is a guide, not a cage.** It is how we did the sports park, not the only way. The user, 2026-10-29: if a rule or
part of this guide blocks your creative freedom, break from it; if you find a cooler way, do it. Only one thing ranks
above your judgement: **performance comes first** (frame time in a running city, planning time, memory: measure them as
in sections 7 and 8). Within that, do whatever makes the ground best, and write what you did differently, and why, into
your type's runbook so the next agent learns from it.

Build on the existing systems, don't rewrite them (the user: "invent your decals, or invent a new way to draw them, but
don't rewrite everything"). Invent new decal kinds, new brushes, new causes, a new way to lay them out, all freely; keep
the shared pieces (the library, the instance record, `SharedDecals`, the budget, the brushes others use) and extend them.
If one of them truly blocks you, change it with care and keep the other types working.

## 1. What you have (all built, all tested)

| Piece | Where | Use |
|---|---|---|
| Shared library | `Recipes/decals/library/<family>.json` (10 families, ~90 kinds) | the looks; **add your own freely** (section 4) |
| Instances | `AreaPlan.DecalInstances` (`DecalInstance`: kind, variant, x, z, yaw, stretch, opacity, cause) | what your plan lays; saved on the record (codec v2), drawn by `SharedDecals`, removed with the build |
| Brushes | `Core/Decals/Dynamic/Brushes.cs` | `Scatter`, `Tone`, `EdgeStrip`, `CracksAlong`, `Fill`, `Under`, `At` (section 3) |
| A worked design | `Core/Decals/Dynamic/Weathering.cs` (`Scene`) | the sports park: read the plan, decide the look, lay causes; copy its structure |
| Budget | `Weathering.PerHectare` 320, `MaxInstances` 9000, `BrushContext.Density` | any site size, from a 20 m scrap to 1 km² |
| Paint | `Core/Decals/GroundWear.cs`, `Core/Paint` | the big, soft variation (lawn moisture, forest floor); free, permanent |
| Offline renderer | `GroundDesignTests` with `PM_WRITE_GROUND=1 PM_GROUND_ONLY=<style>` | the plan's decals from above and close-ups in seconds: iterate here, confirm in the game |
| Game scenario | `suite/scenarios/ground-sports.json` (copy it for your type) | frame times with / without the ground, counts, GPU memory, close-ups by kind (`lab grime focus`) |
| Library bake | `PM_BAKE_LIBRARY=1 PM_BAKE_ONLY=<kind> dotnet test --filter DecalLibraryTests` | every variant of a kind on grass, asphalt, paving and dirt |
| Formatter | `node tools/decals/format-library.js <family>.json` | after any script edits a family file (small diffs) |

## 2. The steps for a new type

1. **Look first.** `tools/suite/Show.ps1 -Type <type> -Style all -Views 45,top,close` (background, `-Who <task>`; close the
   queue's game yourself when done). Write down what is grass, hard, water, wild; where people, cars and water go; what
   trees stand there; what the edges meet.
2. **Ground choice.** Surfaces only for real materials (courts, paving, asphalt, sand, water's edge); everything "just
   ground" is bare terrain + paint (`Lawn.cs` `TypeDefaults` → Painted). A whole-area grass surface is why types look plastic.
3. **Decide the look before any brush runs** (one vision): season and leaf set (`Weathering.Season`), tree species → leaf
   kinds, the paint's moisture (`GroundWear.LawnMoisture`) → where dry and lush decals go, surface material → crack and
   wear kinds, formality → colour level (quiet variants for formal places). Log it.
4. **Hook your type.** Today `Weathering.Wants` returns true for `sportsPark` and `Scene` is the sports design. For the
   next type, lift the plan reading (`Scene.Read`: paths, aisles, fences, surfaces by role, trees by species, objects) into
   a shared `GroundScene`, and give each type its own design class with the cause passes it needs (`Wants` → a per-type
   lookup). Keep `Weathering.Apply`'s budget loop (density, re-run when over) for every type.
5. **Causes, in order:** tone (faint, few) → trees → seams → the type's own use (play / feet / wheels / water / animals /
   harvest …) → objects → 1-4 signature flaws. Section 5 has the cause lists per category.
6. **Offline, then the game.** Render offline until it reads right from above, then `Show.ps1` on a block, 220 × 200 and
   a 5 % slope, 45/top/close; then your copy of the frame-time scenario. Look at every picture yourself.
7. **Show the user early** (one style), take their words literally and in spirit, write them into your runbook
   (`docs/runbooks/<type>.md` from `TEMPLATE.md`), then roll out to every style.
8. **Tests:** every style on block / big / slope gets instances from its causes within budget; tiny and huge sites;
   deterministic; record size. Copy `GroundDesignTests`.
9. **Done:** every style seen in the game, the runbook and the playbook's log (section 11) updated, frame time and memory
   reported, 12 languages for any text the player reads (`node tools/lang/check.js`).

## 3. Brushes: which for what

```csharp
var b = new BrushContext(lib, seed, plan.DecalInstances) { Density = density };      // one per build
Brushes.Scatter(b, area, "pebbles", 0.05f, new ScatterOptions { Weight = NearPath, Cause = "seam:grit" });
Brushes.Tone(b, lawn, "lawnTonePatch", new float2(0.18f, 0.32f), 0.2f, ColourLevel.Quiet, "tone:lawn");
Brushes.EdgeStrip(b, ring, "kerbGrimeStrip", new StripOptions { Side = -1, Offset = -0.3f, Closed = true, Gaps = 0.55f });
Brushes.CracksAlong(b, line, "crackAsphalt");
Brushes.Fill(b, hollow, "puddlePiece", new float2(0.7f, 1f));
Brushes.Under(b, crowns, "leafOakSummer", 0.85f, Weathering.Wind, fallArea);
Brushes.At(b, gate, inward, "barePatch", 6f, new float2(0.3f, 0.5f), satellites: 0);
```

| Brush | For | Notes |
|---|---|---|
| `Scatter(area, kind, coverage, Weight)` | anything spread by a cause: leaves, grit, dry grass, molehills, litter | coverage ~ share of the weighted area (overlaps count: 0.5 already reads full); the weight field is the cause |
| `Tone(area, kind, opacity, overlap)` | a faint variation over large uneven ground | patch kinds only (`…TonePatch`), opacity ≤ 0.35, low overlap. **Never on mown turf or small areas** |
| `EdgeStrip(line, kind, Side, Offset, Gaps)` | every seam: path edges, kerbs, fences, court lines, shorelines | `Side` +1 = heavy side (+z) on the left of travel; a CCW ring's inside is on the left; `Offset` moves toward the heavy side. Runs end in faded caps automatically |
| `CracksAlong(line, kind)` | crack chains on asphalt and concrete | variants mixed per piece, caps at run ends |
| `Fill(hollow, kind)` | puddles, marsh pools, spills that fill a shape | give it the real low ground's outline |
| `Under(crowns, kind, coverage, wind, limit)` | leaves, needles, petals, woodland floor | fall zone 2.6 crowns + 3 m, a crown downwind; area clipped to the crowns |
| `At(point, dir, kind, size, opacity, satellites)` | hot spots: goalmouths, gates, bench feet, mast feet, drains | 0-1 satellites usually; many satellites read as spots |

Every brush follows the placement rules (playbook section 7): overlap, no grid, neighbours never the same variant, no
ring "donut" fades (`BrushContext.Avoid`), deterministic from the seed, thinned evenly by `Density` on huge sites.

## 4. Making new decal kinds (you are expected to)

It costs nothing in the mod (a few hundred bytes of JSON); textures are made on the player's machine on first use and
cached on disk; only kinds a city uses go on the GPU (budget 200 MiB; a whole sports park uses ~55 MiB).

1. Add the kind to the family it belongs to (or a new `Recipes/decals/library/<family>.json`). Copy the closest existing
   kind and change it: `id`, `name`, `tier` (`macro` reads from the player's height; `detail` is culled from 200-600 m),
   `about`, `texel` (3-7 cm macro, 1.5-3 cm for crisp stamps), `layers` (`terrain` = ground and surfaces, `ground` = also
   paths and roads), `order` (−2 tone … 7 on top), `opacity`, `edge`, `variants` (`count` 6-16, `size`, `aspect` or
   `width`, `tile`: `none` patch, `x` strip, `xy` sheet, `chain` cracks; `fades`), `ops` (noise, cells, cracks, scatter of
   stamps, layers: `docs/DYNAMIC-DECALS.md`).
2. Season or palette versions are new kinds with a suffix (the sports park's `leafOakSummer`); pick them in code with a
   `Leaf(kind)`-style helper.
3. `node tools/decals/format-library.js <family>.json`, bake it (`PM_BAKE_LIBRARY=1 PM_BAKE_ONLY=<kind>`), look at every
   variant, run `DecalLibraryTests`.
4. A new stamp shape or op is a small change in `DecalGenerator.cs` (with a test; bump `DecalVariants.GeneratorVersion`
   when pixels of existing kinds change).
5. Show new kinds to the user in the decal browser (playbook section 8) when you can.

Rules that held: soft patches multiply their alpha by `shape` (else a hard square); strips that sit side by side need no
fade across (`mowingStripes`); leaves need no base film (it reads as a smudge); colours near the game's terrain
(`docs/TERRAIN-PAINT.md` §3).

## 5. Category starters (ideas, not a checklist: pick what this site would really have)

| Category | Causes to read | Kinds there now | Kinds you will probably add |
|---|---|---|---|
| Parks | trees by species, paths, benches, play areas, lawns, ponds | leaves (summer/autumn), `pathEdgeWear`, `wornPath`, `barePatch`, `pebbles`, `grassClippings`, `molehills`, `dryGrass`, `cloverPatch`, `puddlePiece`, `litterMix` | worn grass under swings and slides, picnic wear, pond-edge mud and reeds' debris, dog-walk shortcuts |
| Nature (big) | slope and flow lines (water runoff), wet hollows (marsh), rock, wind, deer/foot trails, fallen timber | `rockOutcrop`, `rocks`, `stonesCarpet` (sheet), `mossPatch`, `woodlandFloor`, `pineNeedles`, `twigs`, `mudPatch`, `dampPatch`, `wornPath`, `groundMottle` | **runoff rills** (strips following the downhill flow of the built heightmap), **marsh** (wet dark soil, standing water, sedge litter: `Fill` the hollows), scree and gravel fans at the foot of slopes, dry stream beds, fallen-leaf hollows, animal trails, burnt patches |
| Beaches and water | the waterline, dunes, paths to the sea, wind | `sandSpill`, `dryTidemark`, `dampPatch`, `puddlePiece`, `pebbles` | wet-sand band and wrack line (strips along the shore), shells, seaweed, footprints in sand, dune grass edges, driftwood |
| Parking | aisles, bays, the walk-in, kerbs, low corners, age | `crackAsphalt`/`crackSealed`/`crackWeb`, `potholePatch`, `oilSpot`, `oilDrips`, `tyreTrack`, `skidMark`, `kerbGrimeStrip`, `asphaltTonePatch` | sealed-crack patchwork, faded paint, rubber at turning points, drain stains, gravel-lot ruts |
| Shops and squares | doors, queues, café tables, bins, drainage | `pavingTonePatch`, `crackConcrete`, `chewingGum`, `cigaretteButts`, `drinkSpill`, `litterMix`, `kerbGrimeStrip` | gum fields at doors, café-table stains, joint weeds and moss in paving, drain streaks |
| Farms | tracks, gates, yards, animals, fields' edges | `wheelRut`, `mudPatch`, `barePatch`, `dryGrass` | hoof-churned gates, hay litter, tractor turning arcs, manure, field-edge strips |
| Industrial | trucks, loading, storage, spills, rust | `oilSpot`, `rustBleed`, `tyreTrack`, `crackConcrete`, `potholePatch` | forklift tracks, spill stains, rubble and debris piles, rust runs from containers |

Read the cause from the plan or the built ground, never from a pattern: runoff follows the heightmap's downhill
direction; marsh fills the real hollows; ruts follow the aisles; leaves follow the trees and the wind.

## 6. The look: rules the user taught (every type)

- **Macro soft patches are the riskiest decals**: from the player's height they read as smudges. Faint (≤ 0.35), few;
  the paint does the big variation. Spend the count on crisp, small, speckled pieces that read as detail.
- **Leaves read as leaves**: single stamps big enough to see from 30-100 m, scattered, no film; colour from the trees'
  species and the season (green trees in summer: brown and olive; autumn: the orange set).
- **Wear is speckled**: bare ground plus grit and small stones; mud only where water stands.
- **Dirt on a pitch or lawn is subtle**: the user asked twice for it "ever so slightly" more see-through.
- **Smother or vary, never a blotch**: one lonely patch reads as a mistake; one patch per object reads robotic.
- **A strip must know its far side**: a grass strip that lands on a track or paving reads as haze.
- **Boundaries carry the detail**: path edges, court lines, fence lines, kerbs, under trees.
- **Judge from the player's distance first** (45°, the whole site), close-ups second.

## 7. Engine facts that cost hours (don't relearn them)

- **Never a `MaterialPropertyBlock` per decal.** 0.7 µs per renderer: +1.6 ms for 2000. `SharedDecals` puts the size in
  the transform and the opacity in 8 shared step materials: 1500 instances cost ~0.03-0.2 ms. Measure with the scenario's
  "with / switched off" frames.
- **`RenderMeshInstanced` draws nothing** with CS2's DefaultDecal (the game batches its own decals). Don't try again.
- **The game draws its surfaces as decals** (turf, paving) at the template's render queue: library decals are always drawn
  above it (`SharedDecals`), or they vanish under the turf.
- **Planning runs on every preview re-plan** on a worker: keep it under ~1 s on normal sites. Use `PointGrid` for trees,
  barriers and crowns; clip brush areas to where the cause is (`BrushArea.ClipLo/Hi`); don't build per-area grids for
  many small areas (it made planning 50x slower). The report logs per-pass timing.
- **Huge sites**: thin evenly (`Density`), never let the hard budget cut off the last causes; no warning for large sites.
- **Removal is clean** by construction: instances are data on Placemaker's record, drawn as `HideAndDontSave` GameObjects;
  never entities or prefabs. Terrain paint is permanent (say so).
- **The game queue**: run commands in the background with `-Who`; close the queue's game yourself as soon as you have the
  pictures (`{"command":"quit"}` into the newest `.forensics/suite/runs/queue-*/inbox` when nobody else is in line).

## 8. Before you report

- Every style seen in the game on a block, 220 × 200 and a slope; your own eyes on every picture.
- Frame time with and without the ground; instance counts; GPU memory; planning time on a tiny and a huge site.
- Your runbook (what the user said, what failed and why, numbers, runs) and the playbook's log updated.
- Say plainly: "on branch X, seen in the game runner, not in your game yet".

## Lessons from parks (2026-10-29/30, `docs/runbooks/parks.md`)

- 2026-10-30: **Summer leaves read mustard yellow in the game** under green birches (the sports park's `leaf…Summer` carpets):
  a park lays a sparse dark olive-brown litter (`leafLitterSummer`) at ~40 % of the cover (`Scene.LeafScale/LeafOverride`).
- 2026-10-30: **A mood is a room, not a disc.** Discs of 8-38 m dropped "anywhere" rarely fitted between paths and read as
  nothing from 45°; giving each lawn between the paths (inset 3 m) one mood made every room of a park different.
- 2026-10-30: **Site harmony smooths designed ground.** Its change field is blurred 20 passes; tag designed places as
  regions (`mood:*`) and protect them, or a 1.5 m knoll comes out at 0.75 m.
- 2026-10-30: **Boulders are already buried.** `Boulder01-08` sit 1.3-1.8 m below their origin; a negative Elevation of
  half their height on top hides them. Sink 0.35-0.55 × 1.6 m × scale for "coming up through the turf".
- 2026-10-30: **A type with its own design needs its own budget.** The shared causes alone filled 320 a hectare and every
  cause was thinned to 40-60 %; parks use 700 a hectare (`ParkGround.PerHectare`).
