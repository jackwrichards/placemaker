# Terrain paint — materials, engine, toolkit, art guide (s33)

> **2026-10-29 (dynamic decals):** types weathered by code-made decals (docs/DYNAMIC-DECALS.md; v1: the community sports
> park) keep paint only as the soft macro layer (the lawn's lush/dry tone). Their organized paint wear is left out: mulch
> discs per tree, bullseye patches per bench and play item, verge bands along every path, edge discs at a fixed step,
> goalmouth ovals (`GroundWear.Apply`, `DressSettings.Weathering`).

Source: CS2 **1.6.2f1**. Decompiled `Game.dll` (`TerrainMaterialSystem`, `TerrainRenderSettingsPrefab`, `EnvironmentPanelSystem`,
`PrefabSystem.SaveTerrainRenderSettings`), the `PaintSplatmap` compute shader (DXBC from `resources.assets`, disassembled
with `fxc /dumpbin`), the `DefaultTerrainRenderSettings` MonoBehaviour in `resources.assets`, and the terrain textures in
`Content/Game/Blob_Environment.cok` (BC7, mean colour of the 64² mip decoded; the 1×1 mip agrees within 1/255).
Engine rules: RESEARCH-NOTES-ADDENDUM 57–62 and **86–89** (s33 corrections).

## 1. What can be painted

Exactly **four materials** plus **Clear** (TerraformingPrefabs "Terrain Material Extra 1..4", "Terrain Material Clear";
`TerrainMaterialType` Extra1..Extra4, None). Their textures are slots of the map's `TerrainRenderSettingsPrefab`
(`m_Extra1..4Diffuse/Normal`, per slot `DirtOverride`, `SplatRange`, `BlurDepth`, `HeightOffset`). There is **no rock
material**: grass on flat ground, dirt on uneven ground and rock on slopes are the procedural BASE layers (`m_Grass/Dirt/RockDiffuse`).

**Mods cannot add a fifth material.** The user splatmap is `R8G8` and stores four 4-bit weights (§2); the terrain shader
reads exactly four extra textures. A mod (or a map) can only change WHICH textures fill the four slots.

**Maps.** All 14 vanilla maps were saved before 1.6 (no `TerrainRenderSettings` format tag) and fall back to
`NewMapSettingsData.m_DefaultTerrainRenderSettings` = **DefaultTerrainRenderSettings**, the only render-settings prefab in
the base game. The map editor (Environment → Terrain rendering) can duplicate it and pick ANY texture per slot; the copy
is saved with the map (`SaveTerrainRenderSettings`). So custom maps may carry the arid / mediterranean / tropic / alpine
sets that ship (unused) in `Blob_Environment`, or their own.

## 2. How the game stores and applies paint (PaintSplatmap kernel)

- Splatmap texel = playable size / 4096 (**3.5 m** on a 14.336 km map); one texel stores **one 4-bit weight per
  material**: `R = w1 | w2 << 4`, `G = w3 | w4 << 4`. Materials are **independent**: painting dirt does not remove sand.
- `PaintDither` (strength > 0): `w = max(old, brush.a × strength)` on the tool's material, then `floor(w·15 + d)` with
  `d` = 0.25 / 0.75 in a 2×2 checkerboard. Painting the same value twice changes nothing. 16 levels (+ the dither).
  Anything below ~1/30 vanishes.
- `EraseDither` (strength ≤ 0, or the Clear tool): `w = min(old, 1 − brush.a)` on the tool's channels — Clear = all four,
  a material tool at strength 0 = **that material only**. Opacity is ignored for erasing (the mask carries it).
- Brush mapping: `uv = rotate(texel centre − brush centre) / half size`, `u = +x`, `v = +z` at `m_Angle` 0; row 0 of a
  `Texture2D` is `v = 0`. Both the 4096² playable splatmap and the 1024² world splatmap are painted; both are saved with
  the city. There is no undo in game mode.
- The terrain shader then blends the four weights over the procedural base (SplatMultiplier 2, SplatPower 1, the slot's
  HeightOffset/BlurDepth: dirt is crisp and "high", forest floor soft, dry grass recessive).

## 3. Census — the colours

Mean albedo (sRGB). Vanilla maps = DefaultTerrainRenderSettings:

| slot | texture | colour | look | blend (dirtOverride / splatRange / blurDepth / heightOffset) |
|---|---|---|---|---|
| base grass | Grass_temperate_01 | `#444b36` (68,75,54) | the lawn: dark olive green | — |
| base dirt | Dirt_temperate_01 | `#615952` (97,89,82) | grey-brown (uneven ground) | — |
| base rock | Rock_temperate_01 | `#6a645e` (106,100,94) | grey (slopes) | — |
| **1 Dry** | Grass_Dry_Temperate_01 | `#4f4f40` (79,79,64) | grey-olive, slightly yellower and lighter than the lawn | 0 / 0.28 / 0.18 / −0.09 |
| **2 "Lush"** | **Forest_temperate_01** | `#3b3e27` (59,62,39) | **dark forest floor** (leaf litter): the DARKEST material, not a fresh lawn | 0 / 0.35 / 0.22 / +0.09 |
| **3 Dirt** | Light_dirt_temperate_01 | `#61594e` (97,89,78) | light grey-brown earth (≈ the procedural dirt) | 0 / 0.30 / 0.08 / +0.20 |
| **4 Sand** | Sand_temperate_01 | `#777066` (119,112,102) | grey-beige temperate sand (not yellow) | 0.48 / 0.16 / 0.04 / +0.07 |

Other sets that ship (for custom maps), same measurement:

| theme | grass | dry grass | fertile / forest | dirt | sand | rock |
|---|---|---|---|---|---|---|
| arid | `#816c4d` | `#90725b` | fertile `#685e48` | `#a28674` | `#a18578` | `#7c533e`, rocky ground `#8a5d47` |
| mediterranean | `#6a6149` | `#816954` | fertile `#5f5d43` | `#8b7565` | `#9b8575` | `#8c7a6d`, rocky dirt `#857668` |
| tropic | `#404832` | `#4c4b38` | forest `#3b4129` | `#655446` | `#a5977d` | `#635c54`, rocky dirt `#5c5447` |
| alpine | — | `#4d4d3e` | — | gravel `#615e58` | `#736f68` | `#555453`, rocky ground `#6e6d6a` |
| temperate extras | — | — | fertile `#465138` | — | — | — |

The full table (37 textures, incl. the pre-1.6 legacy base textures) is `TerrainPalette.Known` in Core.

**What this means for art.** On vanilla maps the colour steps are small: dry grass is the lawn a little paler, forest floor
a little darker and browner, dirt clearly lighter and browner, sand lighter still and grey. Paint reads through texture
and shading as much as hue, so: use **forest floor** for anything DARK under plants (mulch, needle beds, leaf litter, shady
damp lawn, ash), **dirt** for anything trodden (paths, wear, pitches, headlands), **dry grass** for sun, wear halos, dune
grass and edge blending, **sand** for beaches, infields, raked gravel. A lawn needs no paint: the procedural grass is the
lawn; paint only VARIES it. On an arid map "lush" is a darker, browner grass and dirt ≈ sand: plans adapt automatically (§5).

## 4. The toolkit (Core/Paint)

`PaintShape` — a pure, immutable, thread-safe field xz → 0..1 with a support box:

| primitive | what for |
|---|---|
| `Polygon(ring, feather, noise)` | fill a polygon, soft and natural edge inside the outline ("paint the whole thing"; at 0.5 strength = "half-texture") |
| `Stroke(line, width, feather, start, end, along, endFade, wobble)` | painted paths, verges, ruts, trails petering out; strength gradient along the path |
| `Radial(c, inner, outer, falloff, noise, aspect, angle)` | sphere / disc / oval gradients: mulch, worn ground, ash, goalmouths |
| `Linear(from, to, falloff, start, end)` | linear gradients across a polygon (lawn drying toward an edge, beach → dune) |
| `Noise(scale, coverage, softness)` | speckle / patch masks with a real coverage share |
| `Grid(...)` | baked fields (masks, moisture) |
| `Mul`, `Or`, `Intersect`, `Subtract`, `Scale`, `Invert`, `Gamma`, `Clip`, `Union(list)`, `Split` | masks, clips, unions of hundreds of discs, complementary A→B blends |

Falloffs: Linear, Smooth (default), Fast (long faint tail), Hold (crisp disc), Sphere (√(1−t²) dome).

`PaintKit` emits shapes as `PlannedPaint`s (`Shape` field): `Fill`, `Stroke`, `Radial`, `Gradient`, `Blend` (two materials,
complementary weights), `Speckle`, `Erase` (one material or all), `Shape`. It applies a MASK (where bare terrain shows), an
amount, a strength cap, a paint budget (`MaxPaints`, `Dropped`), skips fields below one 4-bit step and fields that the mask
empties (`Skipped`), and stores the probed painted area (`AreaEstimate`, used by the permanent summary).

`PlannedPaint` (all optional, defaults = old behaviour): `Shape`, `Erase` (per-material erase), `Knockout` (this paint weakens
the OTHER materials under it — a dirt path through a lush lawn tint stays dirt), `AreaEstimate`.

`PaintRaster` (Core/Build): erases first (Clear, then per-material), paints max-merged per material, knockouts applied on the
aligned grids, square stamps ≤ 512² cells (≤ 48 per build), cell doubled when over budget, deterministic.

Example — a trail that frays, thins out toward its end and never paints on paving:

```csharp
var kit = new PaintKit(plan.Paint, ctx.H, seed) { Mask = groundMask };
kit.Stroke(line, TerrainMaterials.Dirt, 0.85f, width: 2.6f, feather: 0.9f, start: 1f, end: 0.4f, endFade: 4f, wobble: 0.3f, knockout: true);
```

## 5. Map adaptation (palette)

Plans write the vanilla INTENT (1 dry, 2 lush/forest floor, 3 dirt, 4 sand). The game reads the map's slots
(`Game/Preview/TerrainPaletteReader`: texture asset names; known textures take the census colour, unknown ones are measured
from the bound texture) into `SiteContext.Terrain`, logs `Placemaker terrain paint materials: …`, and after composing,
`TerrainPalette.Remap` sends every intent to the slot whose texture NAME says so (sand, dirt, forest/fertile/lush, dry…),
else to the closest COLOUR (CIE Lab). Vanilla maps: identity. `TerrainPalette.Nearest(colour)` picks a slot for a look
("dark mulch"). The gallery renders `--map arid|mediterranean|tropic|alpine|shuffled`.

## 6. Where it is used (Ground paint dressing, `Core/Decals/GroundWear.cs`)

Per fill step, masked to bare terrain (`GroundMask`: not under surfaces, nets, paving, sand, water, cutouts; outline
feathered so the build blends into the land):

| feature | paint |
|---|---|
| Lawn variation (painted lawns) | moisture field (noise, hollows / water wetter, knolls drier) → forest-floor tint where wet, dry grass where dry |
| Mulch under trees | union of radial fields under every crown (needle beds stronger; clean style: a neat round bed); shrub beds |
| Verges | dirt strokes full at every path edge, fading 2 m out |
| Desire paths | bowed dirt strokes with faded ends (knockout) where people cut corners / walk to benches and play items |
| Worn patches | radial dirt at play items, picnic tables, bench fronts, entrances, with a dry-grass halo |
| Edges | dry-grass speckle in a soft band inside the outline (gritty: dirt too) |
| Extras | goalmouths and fitness stations, campground pitches and fire rings (forest-floor ash), dune grass along sand, worn headlands + dry verges round crop fields, dog runs along the fence line |
| Painted trails (park `trailStyle = painted`) | `TrailPaint.Stroke` + an invisible pedestrian path when the playset has one (else visual only, Info) |
| Worn shortcuts (park `desireLines = worn`) | `TrailPaint.DesireLine` on dirt (was a dry-grass polygon) |

Controls — one **Ground paint** section per type: Lawn (Painted terrain / Grass surface, lawn types only), Ground paint
amount (`dressPaintWear`, 0–300 %), Desire paths, Mulch under trees, Lawn variation, Clear old paint first (off; also wipes the
player's own paint). Amount × style (Clean 0.6, Lived-in 1, Gritty 1.4).

## 7. Lawn: painted terrain vs grass surface

| default | types |
|---|---|
| **Painted terrain** | park (natural, woodland, meadow, pocket, landscape, wildflower, urban, riverside, greenway presets), picnic grounds, dog park, greenway park, adventure playground, animal park, community garden, orchard park |
| **Grass surface** | park formal / garden / victorian / modern presets, formal garden, botanical garden, cemetery, memorial park, fairground, sports park, amphitheatre, skate park, urban farm |

Use Grass surface on maps whose base terrain is not grass (desert, rock) or where a manicured, uniform lawn is the point.

## 8. Permanence

Paint is saved with the city and not undone when a build is removed. Ground paint alone is now one info line in the
review ("Ground paint, about N m² — stays if the build is removed"), without the tick; terraform / water still need it.
