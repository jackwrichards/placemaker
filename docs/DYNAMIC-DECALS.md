# Dynamic decals

Code-made grime: worn turf, mud, puddles, cracks, leaves, oil, dust, rust. Generated on the fly from recipes, drawn with
the game's own decal material, never entities, never prefabs, nothing in a save but a few bytes on Placemaker's own
record. Removing the mod leaves no trace (no white boxes, no errors); the grime simply goes with it.

Research behind it: `docs/research/dynamic-decals.md` (engine route, saves and removal, art direction). This page is
how the system works and how to use it.

## The pieces

| Where | What |
|---|---|
| `Core/Decals/Dynamic/DecalStroke.cs` | One stroke in a plan: kind, seed, footprint (box, path with widths, outline), strength, wetness, age, decal layers, draw order |
| `Core/Decals/Dynamic/DecalLibrary.cs` | Kinds as data: a recipe of generator operations; the library file is `Recipes/decals/kinds.json` |
| `Core/Decals/Dynamic/DecalGenerator.cs` | The interpreter: a kind + a stroke + a tile → albedo, normal and mask maps (pure .NET, worker threads, deterministic) |
| `Core/Decals/Dynamic/StrokeFrame.cs`, `DecalTiles.cs` | The stroke's geometry in world metres; a stroke cut into decal boxes (grids, or boxes chained along a path) |
| `Core/Decals/Dynamic/DecalNoise.cs` | Gradient noise, fBm, ridged noise, Worley cells (no tables, no allocation) |
| `Core/Decals/Dynamic/DecalTexture.cs` | Mip chains and BC3 (DXT5) compression on the worker |
| `Core/Decals/Dynamic/DecalCodec.cs` | Strokes as bytes on the group record (version first) |
| `Core/Decals/Dynamic/Weathering.cs` | Placement from causes (play, feet, water, wind, wheels, objects) with a budget |
| `Game/Decals/DynamicDecalSystem.cs` | The renderer: records → tiles → background generation → upload a few a frame → cull, hide, free |
| `Game/Groups/GroupComponents.cs` | `PlacemakerDecalBlob`, the byte buffer on the record |
| `Game/Build/BuildService.Adopt.cs` | `WriteDecals`: the plan's strokes onto the new record |

## How a builder adds grime

Any layer (a type, a feature, the AI builder) adds strokes to its plan:

```csharp
ctx.Plan.Decals.Add(DecalStroke.Box("mud", seed, centre, yaw, new float2(5, 4), 0.8f));
ctx.Plan.Decals.Add(DecalStroke.Path("leafDrift", seed, points, width: 1.6f, strength: 0.6f));
ctx.Plan.Decals.Add(DecalStroke.Outline("asphaltTone", seed, lotRing, 0.9f));
```

Box yaw is the game's `RotateY` (local +x → world (cos, −sin)). A path's "across > 0" side is the left of its direction of
travel: kinds that pile against one side (kerb grime, leaf drifts) pile there. `Layers` picks what it lies on
(`DecalLayerBits.Terrain` = bare ground and every surface, `Roads` = paths and streets); 0 = the kind's own.
`Wetness` and `Age` are inputs the kinds read (a puddle is water when wet, a tidemark when dry).

Strokes need no space and reserve nothing. Keep them heavy-tailed: a few strong hot spots, most of the site clean.

## How a builder defines a new kind (no engine code)

Add an object to `Recipes/decals/kinds.json`, or give the plan its own (`ctx.Plan.DecalKinds.Add(DecalLibrary.ParseKind(json, warnings))`;
a plan's own kinds are saved with its strokes):

```json
{
  "id": "mossyJoints",
  "texel": 0.04, "layers": "terrain", "order": 2, "lod": 30,
  "edge": { "feather": 1.0, "warp": 0.8, "warpScale": 3, "round": 1 },
  "ops": [
    { "op": "worley", "out": "w", "scale": 0.6, "mode": "edge" },
    { "op": "remap", "in": "w", "out": "joint", "lo": 0.06, "hi": 0.0 },
    { "op": "fbm", "out": "n", "scale": 2.5, "octaves": 4 },
    { "op": "remap", "in": "n", "out": "patch", "lo": -0.2, "hi": 0.5 },
    { "op": "mul", "a": "joint", "b": "patch", "c": "shape", "out": "moss" },
    { "op": "layer", "alpha": "moss", "color": [70, 86, 40], "color2": [52, 66, 34], "tone": "n", "smooth": 0.3 }
  ]
}
```

Fields: each op writes a named field (`out`); later ops read it. Built-ins: `shape` (the footprint with its soft, warped
edge, 0..1), `edge` (metres inside the footprint, negative outside), `along` / `across` (metres along and across a path,
or the box's own x / z), `u` (0..1 along), `one`. Any number may follow the stroke: `{"wet": [dry, soaked]}`,
`{"age": [fresh, old]}`, `{"strength": [weak, strong]}`.

| op | what it makes |
|---|---|
| `fbm` | fractal noise, about −1..1: `scale` (m), `octaves`, `gain`, `warp` + `warpScale` (domain warp: organic lobes instead of circles), `stretch` [along, across] (streaks along a path), `ridged` |
| `worley` | Voronoi cells in metres: `scale`, `mode` `f1` (distance to the nearest point) or `edge` (distance to the cell border) |
| `remap` | smoothstep(`lo`, `hi`, `in`) × `k` (lo > hi inverts) |
| `band` | 1 near `at` (± `width`): rings, tidemarks, twin wheel tracks |
| `mul`, `add`, `sub`, `max`, `min` | of `a`, `b` (and `c`), fields or numbers, × `k` |
| `erode` | height-blend edge erosion: `cover` breaks into grains of `grain` (0..1) at its edge, filling the low spots first (`sharp`) |
| `cracks` | random-walk cracks that end in T-junctions: `seeds` per m², `step`, `turn`, `branch`, `len` [m, m], `width` [m, m]; writes `out` (the lines) and `outHalo` (a soft band either side) |
| `scatter` | stamps in clusters of clusters: `stamp` (`leaf`, `pebble`, `print`, `blob`, `twig`, `tread`, `litter`), `density` per m², `clusters` per 100 m², `cluster` (share in clusters), `clusterRadius`, `size`, `aspect`, `palette`, `alpha`, `noise` {scale, lo, hi} (thinning), `ramp` [start, end] along a path (prints fading), `side` (pile toward across > 0), `align` (`along` a path), `height`, `smooth` |
| `layer` | composites a material: `alpha` field × `alphaK`, `color` → `color2` by `tone` (a −1..1 field), `vary` (value variation inside), `smooth` → `smooth2` by `smoothBy` (wet = glossy: puddles reflect the sky), `height` (relief for the normal map), `ao` |

Output per texel: colour (sRGB), opacity, relief (normal map), smoothness and AO, as the game's decal shader takes them.
`texel` sets the detail (metres per texel); a tile is at most `maxTexels` (512) a side; `lod` keeps it drawn up to
lod × its size in metres from the camera (at least 250 m) and fades it over the last quarter.

The starter library: `wornGrass`, `bareEarth`, `desirePath`, `mud`, `muddyPrints`, `puddle` (and its dry tidemark),
`cracksTar`, `oilStain`, `oilLine`, `wheelPath`, `kerbGrime`, `leafDrift`, `leafLitter`, `cornerDebris`, `dust`,
`rustStain`, `stain`, `gravelSpill`, `asphaltTone`.

## How placement works (Weathering)

`Core/Decals/Dynamic/Weathering.cs` runs after the plan is composed, graded (SiteHarmony) and made safe (SiteSafety), so it
sees the ground the build leaves. It reads what was planned and asks why each place would get dirty:

- **play**: goalmouths (skewed off-centre), the goal crease worn to soil and damp, the centre spot, the spine down the
  middle (widest at midfield), the linesmen's tracks (one each half, opposite touchlines), warm-up wear behind the busy
  end, the turf in front of dugouts and stands;
- **feet**: mud where players step off the pitch onto a path, prints fading along it; shortcuts trodden across the grass
  from a gate to the stands or courts when the paths go the long way round;
- **water**: puddles in the real low points of the built ground plus 2–3 cm of micro-unevenness, along the paths and over
  the car park; the deepest two wet, the next three dry tidemarks;
- **wind**: one prevailing wind (from the south-west); each tree sheds downwind and the first kerb, path edge, court or lot
  edge or fence on the way catches the leaves; hits along one barrier merge into one drift, a lone tree's leaves blow on;
  thin litter downwind of the bigger tree groups (one sheet per group); court corners downwind collect dust and leaves;
- **wheels**: the whole lot's uneven tone, wheel paths and the lane oil line per aisle (stronger near the walk-in), oil
  under the busy stalls (the nearest to the park, thinning out), kerb grime in broken runs, a few cracks with tar seals;
- **objects**: rust at the foot of a third of the steel masts and lamps, grime under a few benches;
- **signature flaws**: each site picks one or two to exaggerate (a bald goalmouth, a big puddle, a leaf-choked corner, a
  well-worn shortcut).

Budget: about 60 % of the site stays clean (the pass measures the touched share on a 1.5 m grid and softens the broad
strokes over 40 %), under 10 % strong. The player's amount (sports park **Grime**, 0–300 %) scales every stroke's
strength; 0 places nothing. v1 weathers the community sports park only (`Weathering.Wants`).

For that type the terrain-paint wear (docs/TERRAIN-PAINT.md) keeps only the soft lawn tone; its organized parts are left
out: a mulch disc per tree, bullseye patches per bench and play item, verge bands the full length of every path, edge
discs at a fixed step, goalmouth ovals.

## How it is drawn

`Game/Decals/DynamicDecalSystem` (UIUpdate phase, runs while paused):

1. Every second, or when the group registry changes, it reads the records with a `PlacemakerDecalBlob`. A record still
   under construction, being removed, or ungrouped gets nothing (yet).
2. A new record's strokes are decoded and cut into tiles; each tile goes to a queue that up to three background threads
   (below normal priority) work through: generate, mip chain, BC3. Nothing heavy on the main thread.
3. Each frame the main thread takes finished tiles for at most 2 ms: three `Texture2D` (DXT5, the worker's bytes copied in,
   `Apply(false, true)`: non-readable), a copy of the game's `DefaultDecal` material (`colossal_*` properties as the game
   sets them, `ENABLE_VT` off), a box mesh spanning the ground under the tile (+6 m), a `MeshRenderer` on a GameObject.
   Everything `HideAndDontSave`.
4. Renderers are culled by distance with a fade, and hidden while an infoview is open.
5. A record that goes (undo, bulldoze, Edit, ungroup) or another city loading frees its renderers, materials, meshes and
   textures. Results still in flight for a gone group are thrown away.

Logs (`Placemaker.log`): "Placemaker dynamic decals: group N: S strokes … on the record; weathering …" at build, then
"… drawn as T tiles … worker time … upload … worst tile … MiB on the GPU" when done, and "… tiles removed (why)".

## Why removal is clean

- The decals are GameObjects, never entities: saves contain entities only (`SerializerSystem`), so no save names them.
- No prefab is made or referenced, so nothing can become an obsolete prefab with the game's missing-object box.
- The only trace in a save is `PlacemakerDecalBlob` on Placemaker's own record entity. Without the mod the game skips
  that component type cleanly (one "Not serializable type" warning, as for every mod component) and drops the record at
  the next save. With the mod, the strokes come back with the save and are regenerated.
- Terrain paint stays what it always was (vanilla terrain data, kept after removal).
- The old pack-decal path is off for good: no plan places a decal from a decal pack (`PackDecals`), the Decals feature
  scatters nothing but the base game's markings, and older saves' see-through copies are still cleaned up
  (`OldDecalCleanupSystem`).

## Testing

- `dotnet test Tests/Placemaker.Tests --filter DynamicDecalsTests`: the library reads, every kind generates, the same
  stroke gives the same pixels, paths join across tiles, the codec round-trips and refuses garbage, BC3 stays close, the
  community park's weathering grows from causes within budget (block, big site, slope), the Grime amount works.
- `PM_WRITE_DECALS=1` with the same filter writes pictures to `.forensics/decals/`: the plan's decals from above and
  close-ups (a goalmouth, the mud gate, stall oil, a leaf drift, a puddle, a court corner), a quick look without the game.
- In the game: `tools/suite/Show.ps1 -Category sports -Type sportsPark -Style community` (pictures wait for the grime:
  `settle.grimeMs`), the lab op `{"do":"lab","op":"grime"}` (wait, count; `"on": false` frees everything), and the
  removal proof `suite/scenarios/grime-save.json` + `grime-load.json` (`-Fresh`).
