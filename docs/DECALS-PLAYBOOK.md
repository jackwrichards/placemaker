# Decals playbook

How to give a Placemaker type (a park, a nature type, a plaza, a car park) its ground detail with the shared decal
library. Written as we go: the sports park is the first type done this way; every later type follows this page and
adds what it learns. How the system works is in `docs/DYNAMIC-DECALS.md`; this page is how to use it well.

## The user's rules (2026-10-29, after round 1)

These come straight from the user's review of the library. They beat any default below.

1. **Large.** The game is played from far away; the player looks at a whole park at once. A patch is 5-15 m, not 2 m.
   Pieces (leaves, petals, stones, sticks) are drawn a little bigger than life so they read from that distance. Anything
   you can only see with the camera on the ground (acorns, tiny twigs) does not belong.
2. **See-through, with faded edges.** Large but subtle: every kind has an `opacity` well under 1 (bare ground 0.5,
   puddles 0.75 with a pale sheen, never a dark blob), and edges that fade over metres. A hard outline or a solid blob
   is a defect (round 1's puddle looked like an oil stain; the dry leaves like a stain).
3. **No blotches.** If a type uses a detail, it uses it properly: smother the area (the `…Carpet` kinds repeat both ways
   and cover a whole lawn or grove), or vary it (patches cycle through fade types and sizes). One lonely patch of birch
   leaves, four stones, one cluster of cigarette butts all read as mistakes.
4. **Consistent across the whole site.** Small details only work when they are everywhere they belong: a litter
   sprinkle over every busy area, not one patch of butts by one bench. That means many instances; the shared library
   makes them cheap (one texture set per variant for the whole city).
5. **Higher-level looks over tiny parts.** "Woodland floor" (sticks, bark, leaves, seeds, humus) instead of separate
   twigs and acorns; "stones" lathered over rough ground instead of a few rocks.
6. **New looks are made by editing the library, never on the fly.** A type that wants more litter, other leaves or a
   new stain adds or changes a kind in `Recipes/decals/library/<family>.json` (data only), the user approves it in the
   browser, and every type can then use it. No type generates its own textures.

Round 2 notes (same day): **nothing robotic, anywhere.** Organic, overlapping, high transparency, and larger again
(every family scaled 1.5×: leaf patches 8-15 m, bare ground up to 20 m). Colour comes in **two levels**: every family
has `"saturations": [1, 0.8]`, so its variants alternate between full colour (the user liked round 2's colour) and a
step toned down. Builders pick the level that suits the place (quiet, formal sites: the toned variants; autumn
woodland: full colour). Transparency is set per kind; the user's word: "maybe a little too transparent, maybe not, it
depends how they're used", so how strongly a brush lays them (instance opacity, overlap) is where it is tuned.

Round 3 notes: **cater to what the game shows best.** Parks are seen from hundreds of feet up in the editor and about
100 feet up in play. Decals are mainly there to add texture to the whole when you blur your eye, through a macro
lens, though each must still be an A+ up close. So:

- Every kind has a `tier`. **macro** kinds read from the player's view (leaf masses and carpets, bare and dry ground,
  ground mottle, rock outcrops, stones, worn paths, puddles, mud, cracks, edge strips, tone): reach for these first.
  **detail** kinds (gravel spill, gum, butts, droppings, spills, drips, paw prints, mixed litter, sticks, molehills,
  rust, pine-needle patches, clippings) only show up close: use them sparingly, as texture inside macro looks.
- Resolution follows the view: at 30-100 m a screen pixel covers several cm, so macro kinds use 3-7 cm texels. That
  makes many more variants fit the budget: 484 variants of 65 kinds in 131 MiB, 8-16 variants for the common macro
  kinds, so a park almost never shows the same one twice (with random rotation, scale and opacity on top).

## Placing them: never robotic

How instances go down matters as much as how they look. A brush (phase 2) must:

- **Overlap.** Patches overlap each other and their neighbours' kinds (leaves over bare ground over tone); the edges
  blend because every kind fades out over metres. Never butt patches edge to edge, never leave a gap of the same width.
- **Never a grid, never even spacing.** Positions come from the cause (wind shadow, foot traffic, low ground) plus a
  cluster process: clumps of clumps, thinning with distance from the cause. No two neighbours the same variant; random
  rotation (any angle for patches, along the line ±10° for strips); scale 0.7-1.4; opacity 0.6-1.0.
- **Follow the ground's shapes.** Leaves pile against kerbs and fences downwind, wear follows the walking line and
  widens where people bunch, puddles sit in the real hollows. A detail with no reason to be where it is reads as fake.
- **Mix kinds where causes mix.** Under a mixed group of trees: mixed leaves, woodland floor and a little bare ground,
  overlapping, not three tidy separate patches.
- **Carpets for "everywhere", patches for "here".** A carpet covers a whole area at low opacity; patches on top make it
  uneven. A carpet alone looks like wallpaper; patches alone look like blotches.
- **Judge from the player's distance.** If the placement reads as a pattern from 100 m up, it is wrong, however good
  each patch is up close.

## Choosing what a type gets

Work from causes, then from seams, then from tone:

| Cause | Ask | Library kinds |
|---|---|---|
| Trees | Which species, how many, where does the wind blow? | `leaf…` patches downwind, `…Carpet` under groups, `woodlandFloor`, `pineNeedles`, `blossomPetals` |
| Feet | Where do people walk, wait, cut corners? | `barePatch`, `pathEdgeWear`, `footprintsMud`, `litterSprinkle`, `chewingGum` (paving) |
| Play | Goals, courts, swings, stands? | `barePatch` (goalmouths), `mudPatch`, `sandSpill` (courts), `grassClippings` |
| Water | Low points, paths, car parks? | `puddlePiece` (overlap pieces), `dampPatch`, `dryTidemark` |
| Wheels | Car parks, service tracks, mower routes? | `tyreTrack`, `skidMark`, `oilSpot`, `oilDrips`, `wheelRut` |
| Age | Asphalt and concrete, kerbs, fences? | `crack…` chains, `crackWeb`, `potholePatch`, `kerbGrimeStrip`, `fenceLine`, `seamGrime`, `rustBleed`, `mossPatch` |
| Everything | Every open lawn and every paved area | `lawnTone`, `mowingStripes`, `asphaltSheet`, `pavingTone`, `dirtTone` (faint, whole-area) |

Seams are the best place for detail: paving meeting grass, court meeting lawn, car park meeting verge, path edges,
fence lines, the ground under canopies, bench feet and posts, kerbs. Every seam the plan knows gets a strip
(`pathEdgeWear`, `kerbGrimeStrip`, `fenceLine`, `seamGrime`, `leafDriftStrip`) laid end to end along it.

## Density and strength targets

- Tone sheets cover **every** open lawn and paved area, faint (5-12 % change). No swath is untouched.
- Cause-driven kinds (leaves, wear, water) cover what their cause reaches, with carpets where the cause is everywhere
  (a grove) and patches where it is local (one tree, one gate).
- Strong spots (a bald goalmouth, the big puddle, a leaf-choked corner): 1-4 per site. Everything else is see-through.
- Small things (litter, gum, droppings) are thin but even over their whole area, never one cluster.

## Adding or changing a kind

1. Edit `Recipes/decals/library/<family>.json`. Each kind has `name`, `about`, `texel`, `layers`, `order`, `lod`,
   `opacity`, `edge`, `variants` (`count`, `size`, `aspect` or `width`, `strength`/`wet`/`age`, `tile`, `fades`) and `ops`
   (the generator recipe; ops and stamps are listed in `docs/DYNAMIC-DECALS.md`).
2. Keep it large and see-through (rules 1-2). Patches: `"fades": ["soft", "islands", "linear", "wide", "side", "ring"]`
   so the variants differ in how they fade. Areas: add a `…Carpet` kind with `"tile": "xy"`.
3. Render it offline: `PM_BAKE_LIBRARY=1 PM_BAKE_ONLY=<kind or family> dotnet test Tests/Placemaker.Tests --filter
   DecalLibraryTests` → `.forensics/decals/library/img/<kind>-<n>.png` (each variant on grass, asphalt, paving, dirt).
   Look at every variant yourself.
4. Run the tests (`--filter DecalLibraryTests`): the whole library must stay under the 200 MiB budget, strips and sheets
   must repeat without a seam, chains must join.
5. Republish the browser (below) and ask the user.

## Judging in the game

- `powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/lab-library.json -Build -Who <you>` lays every kind
  on a test field (lab op `library`; `"focus": "<kind>"` frames one for a close picture) and logs instances, GPU memory
  and frame times. Round 1: 388 instances of 320 variants, 84 MiB, frame time 6.56 → 6.58 ms (no measurable cost).
- Then the type itself with `tools/suite/Show.ps1` (plan, built, terrain pictures). Look from the player's distance
  (45°, whole park in view) first, close-ups second.
- The offline renders and the game agree closely; the game adds sky reflection on glossy kinds and the real light.

## The approval loop with the user

- The browser: https://claude.ai/artifact/BTMZX1HfRAhjkRHdCBzJCN. Every variant on four grounds, settings in plain
  words, Good/Bad and a note per variant, Keep/Drop and a note per kind, and suggested settings. Answers are in its
  store: read them with the artifact data tool (collections `marks`, `kinds`, `suggestions`).
- To republish: bake, convert the pictures to JPEG (`ffmpeg -q:v 4`), copy `manifest.json`, publish the page with the
  changed pictures (keep the same URL; at most 255 files per publish).
- The user also reviews in plain words in chat. Act on the sentiment, not only the letter: "the puddle looks like oil"
  means water must be see-through everywhere, not only in that kind.

## Removal rules (never break these)

- Decals are GameObjects with `HideAndDontSave`: never entities, never a `PrefabRef`, never a prefab.
- A save holds only Placemaker's own record data (strokes, later brush calls). Without the mod the game drops it.
- The disk cache (`ModsData/Placemaker/decal-cache/`) is a pure cache: deleting it costs only regeneration time.
- Terrain paint stays the base layer (vanilla data, kept after removal); decals go on top.
- No pack decals, ever.

## Log of types done

| Type | Date | What it uses | Notes |
|---|---|---|---|
| (library round 2) | 2026-10-29 | 62 kinds, 352 variants, 124 MiB | bigger, see-through, fade types, carpets |
| (library round 3) | 2026-10-29 | 65 kinds, 484 variants, 131 MiB | 1.5x larger, two colour levels, macro/detail tiers, rock outcrops, worn paths, ground mottle |
