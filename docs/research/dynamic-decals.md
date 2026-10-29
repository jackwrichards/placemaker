# Dynamic decals: drawing code-made grime that leaves no trace (R&D, 2026-10-29)

The user: vanilla has no good decals; no shipped decal packs of our own; decals a place builder makes on the fly (noise,
faded edges, cracks, puddles, mud, prints, leaves, dirt paths, curves), and **removing the mod must leave nothing**: no
white checkered boxes, no errors, no stuck data. This is the engine part (how to draw it and why removal is clean). The
art direction (what real grime looks like) and the full save/removal audit are separate notes.

Sources: decompiled `Game.dll` 1.6.2f1 (`%TEMP%\pm-decomp\game`), `Unity.RenderPipelines.HighDefinition.Runtime.dll`
(`%TEMP%\pm-decomp\hdrp`, CO's HDRP fork for Unity 2022.3), `Colossal.IO.AssetDatabase.dll` (`%TEMP%\pm-decomp\coio`),
ExtraAssetsImporter 1.x (`%TEMP%\pm-decomp\eai`), and the spike below run in the real game.

## Answer in one paragraph

Draw the decals **ourselves, outside the game's entity world**: a plain Unity `MeshRenderer` (a decal box mesh) with a
copy of the game's own **`DefaultDecal`** material template and **textures generated in code**. The game's HDRP renders
every renderer whose material has the `DBufferMesh` pass into the same DBuffer as its own decals, so ours get the same
lighting, decal layers (terrain / roads / buildings ...), smoothness (glossy puddles reflect the sky) and sorting as
vanilla decals. Nothing is an entity, a prefab or an asset, so **a save never names them**: without the mod they are
simply not there (proven in game: saved with 23 decals, loaded in a fresh game: 0 obsolete prefabs, 0 objects on a
missing prefab, no errors, the same view shows the clean field). What the plan stores is a small recipe (kind, seed,
shape, outline/curve, strength) in Placemaker's own records, regenerated after load; a save without the mod drops those
records with the rest of Placemaker's components.

## The spike (in the game)

`Game/Suite/SuiteLab.DynDecals.cs` (lab ops `dynDecalInfo`, `dynDecal`, `dynPath`, `dynStress`, `dynFrames`,
`dynFrameAll`, `dynDecalClear`, `dynDecalCount`) and `Game/Suite/SuiteLabDecalArt.cs` (the generators: pure math on
`Unity.Mathematics.noise`, run on a worker thread). Lab only: registered with the game runner, never for players.
Scenarios: `suite/scenarios/lab-dyn-decals.json` (look + frame times), `lab-dyn-decals-save.json` +
`lab-dyn-decals-load.json` (removal proof, the second with `-Fresh`).

How one decal is made (`Spawn`):
1. `AssetDatabase.global.resources.materialLibrary.GetMaterial("DefaultDecal")` (shader `BH/Decals/DefaultDecalShader`,
   queue 2000, passes `DBufferProjector` + `DBufferMesh`, keywords `_MATERIAL_AFFECTS_ALBEDO/_MASKMAP/_NORMAL`).
   `new Material(template)`, `HideFlags.HideAndDontSave`.
2. Three `Texture2D` from the generator (RGBA32 + mips, sRGB albedo with alpha = opacity, linear normal (RG = xy, A = 1),
   linear mask (R metal, G AO, B 1, A smoothness)), block-compressed with `Texture2D.Compress` (sides must be multiples
   of 4), made non-readable. Set as `_BaseColorMap`, `_NormalMap`, `_MaskMap`; `ENABLE_VT` off (plain textures).
3. The properties the game's batch code sets for a decal (`ManagedBatchSystem` lines ~1011-1021, and the game's own
   `Game.Rendering.Debug.RenderPrefabRenderer.SetDecalProperties`, which draws decal prefabs on plain MeshRenderers
   exactly like this): `colossal_TextureArea` (0,0,1,1), `colossal_MeshSize` (box size, centre y),
   `colossal_DecalLayerMask` (`asfloat` of the `DecalLayers` bits: Terrain 1, Roads 2, Buildings 4, Vehicles 8,
   Creatures 16, Other 32), `_BaseColor` (w = opacity), `renderQueue` = template queue + draw order.
   `HDMaterial.ValidateMaterial` as the game does.
4. A box mesh as ExtraAssetsImporter builds its decal boxes (centred; x length, y height, z width; 24 vertices) on a
   GameObject (`HideAndDontSave`, `DontDestroyOnLoad`), no shadows, no probes. UV: u = +x, v = +z of the box (read from
   the calibration decal, `calib-top`).

### What the game showed

Runs (all under `C:\Users\jack\cs2-area-tool\.forensics\suite\runs\`): `send-lab-dyn-decals-1029-095547` (first look,
the save), `send-lab-dyn-decals-load-1029-100014` (that save loaded in a fresh game), `send-lab-dyn-decals-1029-100255`
(framed pictures, frame times, stress).

- **HDRP setup** (`dynDecalInfo`): decals on, decal layers on, DBuffer with per-channel mask, deferred only, SSR on.
  Draw distance 1000 m and atlas 4096² apply only to HDRP projectors; "max 512 decals on screen" only to clustered
  decals that affect transparent surfaces. DBuffer mesh decals have no count limit (`HDRenderPipeline.RenderDBuffer`,
  ~line 19647: one renderer list of every `DBufferMesh` pass, queue range "all opaque"). Templates in the material
  library: DefaultDecal (VT), CurvedDecal, CurvedDecalColorOnly, CurvedDecalDeterioration.
- **Drawn, lit and layered like vanilla decals**: dirt, leaves, cracks, puddles and the 110 m curved path on grass,
  asphalt, sidewalk and a plaza's paving (run 100255: `01-dyn-decals.06-dirt-45.png`, `.21-cracks-road-45.png`,
  `.31-dirt-over-kerb-45.png`, `.19-path-low.png`, `02-dyn-decals-plaza.10-plaza-45.png`). The layer mask works:
  Terrain-only decals stop at the kerb and lie on paving surfaces (surfaces are on the Terrain layer,
  `RenderedArea.m_DecalLayerMask`), Roads decals lie on asphalt and sidewalks only (nets are layer 2,
  `ManagedBatchSystem` ~line 884), nothing lands on lamp posts, trees or hydrants (Other layer left out). Lane
  markings draw over our road dirt (their draw order is higher; ours is set with `renderQueue`).
- **Puddles reflect the sky** (smoothness 0.97 in the mask alpha): blue at low angles (`.14-puddle-grass-low.png`,
  `.25-puddle-road-low.png`), dark from above, as water does.
- **Per-decal opacity works** (`_BaseColor.w` on our own material copy, `.11-leaves-40pct-45.png`), which vanilla decals
  cannot do per instance (addendum 82).
- **The curved path is seamless** with a 5 cm overlap of the boxes' arc ranges (the first run, without it, showed faint
  cut lines across the path at the box joints).
- **HDRP `DecalProjector` with the DefaultDecal material draws nothing** (`.16-projector-45.png`), although the shader
  has a `DBufferProjector` pass. Not pursued: the mesh route is the game's own.
- **Frame time** (fixed block view, 300 frames, median / 95 % / worst ms): none 3.35 / 4.31 / 8.5; 25 decals
  3.29 / 4.52 / 13.3; +400 sharing one material 3.35 / 4.50 / 7.5; **+2000: 3.59 / 4.59 / 6.5**; cleared
  3.09 / 4.18 / 9.8. 2000 extra decals on screen (`.43-stress-2000.png`) cost about 0.25 ms. Spawning 1600 renderers: 6 ms.
- **Upload** on the main thread: 2-21 ms a decal (mips + DXT5 of three maps; 512x640 the slowest). GPU memory
  0.15-1.3 MB a decal.
- **Player.log**: no errors; only our own "not multiples of 4, Compress will not work" warnings in the first run (fixed).
- **Removal proof**: the city saved with 23 code-made decals on it, then loaded in a fresh game that never made them
  (`lab-dyn-decals-load.json`): loaded in 3.1 s, 0 obsolete (missing) prefabs, 0 objects on a missing prefab, no lab
  decals, no errors in Player.log, and the same 45° view shows the plain field and roads
  (`send-lab-dyn-decals-load-1029-100014\shots\01-dyn-decals-load.03-loaded-45.png` against
  `send-lab-dyn-decals-1029-095547\shots\01-dyn-decals.24-all-45.png`). The runner cannot start the game without
  Placemaker (Placemaker runs the runner); a fresh game is the same situation for this feature (no code made the
  decals), and nothing about them can be in the save: they are `HideAndDontSave` GameObjects, and the save writes
  entities only (`SerializerSystem`). A true mod-less load would need the queue to launch with Placemaker disabled and
  a person to look; this route's argument does not need it.

## Route by route

### 1. Mod-rendered decals that are never entities (recommended)

- **Works**, with the game's own decal shader: same lighting, decal layers and sorting (`renderQueue`) as vanilla
  decals. Weather: the DBuffer is applied by every receiver's own shader, so wetness and snow treat our decals as they
  treat vanilla decals (not looked at in the runner).
- **Removable by construction**: no entity, no prefab, no asset database entry, no file. Saves contain entities only
  (`SerializerSystem.CreateQuery`), so nothing in a save points at them. Mod removed: not drawn, nothing to clean.
  Switched off while playing: destroy the GameObjects, materials and textures (`dynDecalClear`).
- **Cheap**: one renderer each, SRP-batched when they share a material; 2000 on screen +0.25 ms.
- **What a real feature must add**: (a) level of detail: vanilla culls small decals by distance
  (`colossal_LodDistanceFactor`, MinLod), ours draw at any distance: our own distance culling or a `LODGroup`.
  (b) Infoviews: vanilla decals take the infoview colour (`BatchFlags.InfoviewColor`), ours keep theirs: hide them
  while an infoview is open. (c) The underground view sets `DECALS_OFF` for its own pass (`UndergroundPass`), as for
  vanilla. (d) Not entities: no selection, bulldozer or Move It; they follow Placemaker's group records (delete the
  group, its decals go). (e) Generation off the main thread (done) and in Burst (below).
- Also possible on the same path: `Graphics.RenderMesh(Instanced)` or a CommandBuffer each frame instead of
  GameObjects (same material, same pass); GameObjects were simplest and let Unity cull them.

### 2. Runtime decal prefabs as entities, made "safe"

What the old way did and why it showed white boxes: a placed decal object is an entity with `PrefabRef` to a runtime
prefab; saves name prefabs by type + name. Loading without that prefab, `InitializeObsoleteSystem` (line ~149) gives the
obsolete prefab one sub-mesh, `MeshSettingsData.m_MissingObjectMesh`, the checkered box. The objects stay in the save and
come back when the mod returns (custom-assets-on-the-fly.md §2).

Can entities be kept out of the save? `Game.Serialization.SerializerSystem.CreateQuery` saves every entity that has ANY
serializable component (`PrefabRef` is one) and NONE of `NetCompositionData`, `EffectInstance`, `LivePath`, `Temp`,
`Deleted`. So a decal object is saved unless it carries one of those five:
- `Temp`: wiped by `ToolClearSystem` whenever any tool clears its temporaries (addendum 41): unusable.
- `Deleted`: the entity dies at the end of the frame.
- `EffectInstance` / `LivePath` / `NetCompositionData`: other systems' identities (effects, live paths, net compositions);
  adding them to a static object invites those systems' jobs to treat it as theirs. Untested and fragile.
- Strip before save, re-add after: a `PreSerialize`-phase system deletes them and a later frame re-creates them. The
  game autosaves; a crash or a failed save between the two leaves the city without them, and any frame where the save
  wins the race writes them into the file (= white boxes later). It also re-creates hundreds of entities around every
  save (a hitch).

Even then, every look needs its own prefab (mesh and material are per prefab, per instance only transform/colour:
custom-assets-on-the-fly.md §1), so "dynamic" means hundreds of runtime prefabs, each registered with PrefabSystem
(the catalog rescans and switching freezes of report 09). Verdict: no.

### 3. Textures generated in code

- **Plain `Texture2D` works** with the DefaultDecal material (spike). No SurfaceAsset, no virtual texturing (VT is only
  used when a SurfaceAsset's textures are in a VT stack: `MaterialKey.Initialize` adds non-VT textures as plain
  material textures, `ManagedBatchSystem` ~line 117); `ENABLE_VT` off on our copy.
- Channels: albedo RGB (sRGB) + alpha (opacity, also drives normal and mask blending: the template has
  `_NormalAlphaSource` = `_MetallicAlphaSource` = 0), normal map RG (A = 1, HDRP `UnpackNormalmapRGorAG`), mask map
  R metal / G AO / B 1 / A smoothness (the template's `_Metallic` and `_Smoothness` are 1 = pass-through). High
  smoothness reads as wet: the puddles reflect the sky.
- Generation cost (Mono, no Burst, one worker thread): dirt 512x400 6.1 s, cracks 512x428 4.1 s, leaves 512² 0.9 s, puddle 256x204 0.7 s, the 110 m path 12.3 s of worker
  time over 14 boxes (brute-force nearest-point search). Never on the main thread (Task.Run), so no hitch, but slow. The same code as a Burst `IJobParallelFor`
  (Unity.Mathematics noise is Burst-ready) is typically 20-50x faster; a GPU pass would need our own shader.
- **Shaders of our own**: the game has the AssetBundle module (`UnityEngine.AssetBundleModule.dll`, Unity
  2022.3.71f1), so a mod could load a bundle with a compute shader or an HDRP Custom Pass shader built with the same
  Unity and HDRP version. None of the 160-odd installed mods does (searched their dlls for `AssetBundle`,
  `ComputeShader`, `DecalProjector`, `CustomPass`: only `CommandBuffer.DrawMesh` overlays). It is possible but adds a
  build pipeline and a per-game-update risk; not needed: CPU/Burst generation into textures is enough.
- Memory: block-compressed (DXT5/BC3) = 1 byte a texel + 1/3 for mips per map, 3 maps: a 512x512 decal 1 MB, 256x256
  256 KB. Textures can be shared between decals (one material, many renderers, see the stress test).

### 4. Curved and long decals

- **How vanilla curves decals**: net lane decals (markings) are one box over the whole Bézier
  (`BatchDataHelpers.BuildTransformMatrix(curve, …, isDecal)`, line ~480) and the shader bends the texture along the
  curve from per-instance `colossal_CurveMatrix` / `colossal_CurveParams` / `colossal_CurveScale` (`LaneProperty`,
  `BuildCurveMatrix` line ~535). Their templates are separate shaders: `CurvedDecal`, `CurvedDecalColorOnly`,
  `CurvedDecalDeterioration` (`BH/Decals/Curved*`, in the material library next to DefaultDecal). Reusing that outside
  the batch system means feeding those per-instance properties through a MaterialPropertyBlock; possible (the non-DOTS
  shader variant reads them as ordinary properties) but undocumented and tied to the curve maths of one game version.
- **What the spike does instead (works)**: chain boxes along the curve, each box aligned to its chord, and generate each
  box's texture in **world coordinates** (distance to the curve, arc length): the texel belongs to the box whose arc
  range holds its nearest curve point (+5 cm overlap so bilinear filtering leaves no seam). Ruts, crown, tread marks and
  mud puddles follow the arc length, so the 110 m path is one continuous thing over 14 boxes. Any outline works the same
  way (a worn patch shaped like the lawn, kerb grime along an edge): it is a mask, not a mesh.
- A mesh strip following the curve would NOT project correctly: the decal shader reconstructs the ground position from
  depth inside the box and maps it by the box's own axes (`colossal_MeshSize`), so the box must be a box.

### 5. Terrain paint as the base layer

Already used (docs/TERRAIN-PAINT.md): 4 extra materials, one 4-bit weight each, **3.5 m texels** (4096² over the 14.3 km
playable area), only on bare terrain (not on roads, nets, buildings; surfaces cover it), saved with the city and kept
when the mod is removed (it is vanilla terrain), no undo. It is the right layer for large soft wear (a dry lawn, a worn
field corner, dirt round a gate) and costs nothing to draw. It cannot do anything below ~7 m (two texels): edges,
cracks, prints, puddles, leaves, a path's ruts. Decals go on top of it: they are blended over whatever the terrain
shows, so a dirt decal on painted dirt reads as one ground.

## Recommended architecture

1. **The plan stores recipes, not pixels.** A `DecalStroke` per patch or stroke in the Core planner (pure .NET,
   testable): kind (dirt, mud, dust, leaves, cracks, stain, puddle, path, ruts, prints, kerb grime ...), seed,
   footprint (centre + yaw + size, a polyline or Bézier with widths, or an outline taken from the plan: a lawn edge, a
   gate, a desire line), strength, wetness, layer mask, draw order. The art rules decide where (under trees, along
   kerbs, where paths meet); a place builder (AI or type) only adds strokes. A few hundred bytes a build.
2. **Generation**: the `SuiteLabDecalArt` generators as Burst jobs (noise, domain warp, Voronoi, scatter, distance to a
   curve), deterministic from the seed, into texture tiles: one box per patch; boxes along curves with world-space
   masks and arc-length ownership + overlap (seamless in the game). Share tiles between strokes of a kind where the look
   allows (a dozen leaf/dirt tiles at random yaw already vary; stress run). Compress (DXT5), cache by
   (kind, seed, size) in memory; an optional disk cache in ModsData is a pure cache, deletable.
3. **Drawing**: a Placemaker `DecalRenderer` (managed system): per stroke a box `MeshRenderer` with a copy of the game's
   DefaultDecal material (or one copy per shared texture set), `HideAndDontSave`, distance culling, hidden in
   infoviews; made from the group records after a load and when a build is applied, destroyed with the group.
4. **Persistence while the mod is present**: the strokes live on the group's record entity as a serialized buffer
   (version-first ISerializable like `PlacemakerMember`), come back with the save and are regenerated after load (a few
   seconds of worker time, drawn as they finish). No prefab, no asset, nothing else in the save.
5. **Why removal is clean**: the only trace in a save is that buffer on our own record entity; without the mod the game
   drops unknown component types (RESEARCH-NOTES rule 4; the save/removal agent covers the detail). No prefab is
   referenced anywhere, so no obsolete prefab and no `m_MissingObjectMesh` box (`InitializeObsoleteSystem` ~line 149 is
   where that box comes from), no error. The city simply loses the grime.
6. **Base layer**: terrain paint for large soft wear (it stays after removal, as vanilla terrain), decals for anything
   under ~7 m and on paving and asphalt. Paint gives the broad uneven tone, decals the sharp local detail.

## Open points / not verified

- Weather (wetness, snow), night, photo mode: not checked (clean profile, noon, dry); expected as vanilla decals.
- A load with the mod truly absent: argued, not done (see above).
- City-wide counts (10k+) need the distance culling first; not measured.
- An HDRP projector with HDRP's own `HDRP/Decal` shader was not tried (it may be stripped from the build, and it would
  not know `colossal_DecalLayerMask`).
- The spike's textures prove the engine, not the art (the path's twin ruts read "organized"); the art research feeds the
  generators.

## Saves and mod removal (decompiled 1.6.2f1; the save/removal research, 2026-10-29)

- **Why runtime prefabs leave boxes.** `PrefabSystem.Serialize` names every referenced prefab by type + name. On load an
  unknown name stays as an obsolete prefab entity (`ResolvePrefabsSystem`, negative index, `PrefabData` disabled) and
  `InitializeObsoleteSystem` gives any object/net/area prefab without meshes the missing mesh (objects: `m_MissingObjectMesh`,
  stretched to the saved bounds by `BatchDataSystem`; nets: `m_MissingNetSection`; surfaces: `m_MissingRenderedArea`).
  No object prefab kind escapes it (decal-only, marker, mesh-less all get the box). Obsolete IDs are written back on every
  save, so the placeholders stay until the objects are deleted. Only a non-object `PrefabBase` (Traffic's `FakePrefab`)
  draws nothing.
- **A mod's own components are skipped cleanly.** `ComponentSerializer` writes each type's assembly-qualified name, and
  each type's data per archetype as a sized block. An unknown type logs one warning ("Not serializable type") and the
  `ObsoleteComponentSerializer` skips the block; an entity made only of mod components loads empty and is dropped at the
  next save. Same for `IDefaultSerializable` systems (never plain `ISerializable` on a system: it logs an error on every
  start). Traffic and Anarchy rely on this.
- **What a save contains.** `SerializerSystem.CreateQuery`: any of `LoadedIndex`, `PrefabRef`, a list of simulation
  components or any mod `ISerializable`/`IEmptySerializable`; none of `NetCompositionData`, `EffectInstance`, `LivePath`,
  `Temp`, `Deleted` (and not `Disabled`). GameObjects and renderers are never saved. All saves (autosave, editor map save)
  run `SaveGameSystem` → the Serialize phase in one synchronous call; a `PreSerialize` system could strip and restore
  entities there, but route 1 does not need it.
- **What today's Placemaker leaves behind when removed** (from the code, not yet seen in a mod-less game):

| Leftover | Without the mod |
|---|---|
| Group/member components | skipped, dropped at the next save: clean |
| Owner lot and zone block (runtime `LotPrefab`) | obsolete lot, grey in lot overlays, zone cells under builds stay blocked: minor |
| Surface clones (`SurfaceCloneSystem`, parking aprons) | drawn with the missing-area material: likely bad |
| Park companions (runtime Tiny Park clone) | a white box at each park build's road front: bad |
| Vehicle props ("Placemaker Vehicle ...") | vehicle-sized white boxes in car-yard and industrial builds: bad |
| See-through decal copies (builds up to 0.1.1) | white boxes (issue #2), old saves only |
| Terrain paint, terraforming, vanilla nets/objects/surfaces | ordinary game data: clean |

  Also to check: `Game.Tools.Hidden` is not saved, and `ParkCompanionSystem` adds it only when it creates a companion, so
  the Tiny Park companion may show after a reload even with the mod present.

## Art direction for the generators (the art research, 2026-10-29; summarised)

Why today's dressing (`GroundWear`) reads as "organized dirt": every host gets its own centred, round patch (every tree
a mulch disc, every bench a bullseye of dirt, dry grass and green), bands run the full length of every path and outline,
edge discs repeat at a fixed step, one noise scale and one softness everywhere, and 3.5 m paint texels can't make an edge.
Real grime is caused by things that cut across the design, and is heavy-tailed (a few hot spots carry most of the wear;
most ground is nearly clean).

1. **Cause fields per site** (0.25-0.5 m grid, Core): foot traffic (walkers from entrances to benches, shops, play items;
   trails reinforce themselves), wheel paths (wheel trajectories of cars entering, turning, parking; occupancy-weighted
   stalls), water (flow accumulation and basins on the built heightmap, with 2-3 cm micro-unevenness on paving), wind (one
   prevailing direction per city; traps in the lee of walls, kerbs, corners), shade (sun and crowns), sources (trees by
   species, unpaved edges, bins, stalls). Effect = product of causes × domain-warped multi-octave noise → contrast curve →
   hot spots.
2. **Two operators that kill the airbrushed look**: domain warping (organic lobes instead of circles and bands) and
   height-blend edge erosion (alpha = saturate((coverage − (1 − grain height)) × sharpness): edges break into speckles that
   fill cavities first). Plus variation inside every decal (alpha 60-100 %, value ±15 %).
3. **Scatter with cluster processes, not Poisson-disk** (clusters of clusters, density decaying from its cause, anisotropic
   things aligned to their field; 6-12 variants per kind, never the same one within 10 m).
4. **Three delivery modes**: stamps (oil, gravel, leaf piles, litter, rust), curve strips (kerb grime, wheel paths, lane
   oil lines, tracks, cracks, drifts) and field-baked unique sheets (aisle tone, worn grass, desire paths, repair patchwork).
   Cracks grow as random walks that end in T-junctions (not Voronoi honeycomb); puddles are contours of the real basins
   (damp rim, dark water with a flat normal and high smoothness, a silt tidemark; a dry-weather variant is tidemark only);
   coverage-preserving alpha mips so thin things survive at distance.
5. **Layer order**: macro tone (paint, baked sheets) → structure (joints, cracks, patches) → traffic → stains from
   objects → deposits (dust, gravel, leaves, litter) → water (puddles drown what is under them). Later layers read earlier
   fields (people walk round puddles, leaves are kicked off paths, moss avoids traffic).
6. **Budgets** (lived-in): about 60 % of a site clean, 30 % faint (5-12 % value change), under 10 % strong, 1-4 hot spots.
   Knobs: Age, Care, Use, Weather, Season (Clean/Lived-in/Gritty become presets). Each site exaggerates one or two
   "signature flaws" (the big puddle in the low corner, the shortcut to the bus stop, the leaf-choked corner).
7. **First ten effects** for a city-builder view: parking wheel paths + lane oil + stall oil; puddles from real basins;
   kerb-line deposits; leaf drifts; emergent desire paths and fragmented worn grass; cracks with tar seals and patches;
   macro tone patchwork on big paved areas; spill at material edges (gravel, mud fans with fading prints); sports and play
   wear from the play pattern (goalmouths, base paths, swing trenches) plus mowing stripes; object stains (rust streaks,
   drip lines, planter rings, dumpster grease).

Sources worth reading: Quilez on domain warping; Heitz & Neyret histogram-preserving blending (tile-free sheets);
van Muijden, Horizon Zero Dawn GPU placement (GDC 2017); Widmark, Terrain in Battlefield 3 (GDC 2012, spline decals);
Lagarde's wet-surface articles; Helbing et al. 1997 (trail formation); Castaño on alpha mipmaps; the FHWA pavement
distress manual (crack types and where they occur).
