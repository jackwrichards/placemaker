# Asset pictures for the AI builder (research, 2026-10-26; nothing built or run)

The question: the AI placing objects should **see** any placeable static object (props, trees, plants, fountains, decals,
vanilla and modded, about 14,000), on demand, as a small picture (256 px, from 45° and from above) with its origin and
front marked. The pictures must not touch the player's view or cause stutter, and must be cached on disk. It also needs to
know which local axis is the asset's front. Context: `docs/AI-BUILDER.md` ("Pictures, drawn by the mod", "Knowing every
asset exactly").

Sources:
- Decompiled Game.dll 1.6.2f1: `C:\Users\jack\AppData\Local\Temp\pm-decomp\game`, cited below as `game:<file>:<line>`.
- `Colossal.IO.AssetDatabase.dll`, decompiled for this note with
  `ilspycmd -p -o <dir> "...\Cities2_Data\Managed\Colossal.IO.AssetDatabase.dll"` (cited as `aadb:<file>:<line>`).
- `Colossal.UI.dll` and HDRP `CustomPassUtils`, decompiled by type with `ilspycmd -t`.
- The mod's own code.
- The user's disk: the catalog dump and Asset Icon Library's folder.

## Short answer

1. **The game itself renders no thumbnails any more.** `thumbnail://ThumbnailCamera/...` URLs are looked up only in
   pre-baked atlases (`AtlasAsset`), by prefab name and size. The game's thumbnail camera pass is still in Game.dll, but
   nothing uses it. The pictures that do exist come from elsewhere:
   - the prefab's own `UIObject.m_Icon`;
   - **Asset Icon Library (AIL)**. On this machine it holds **13,809 ready PNGs (256×256, a 3/4 front view) for
     `StaticObjectPrefab`s** in `ModsData\AssetIconLibrary\.Thumbnails`;
   - icons inside mod packages (`assetdb://...cok@x.jpg`), which can be read as raw bytes.
2. **Rendering a prefab ourselves works without the game's batch renderer, and works the same for modded assets.**
   The model is the game's own asset-importer preview (`RenderPrefabRenderer`): the prefab's meshes and materials go on
   plain `MeshRenderer`s on a private layer, and a hidden camera renders them into a `RenderTexture`. Two traps:
   - Do not use `RenderPrefab.ObtainMeshes()`/`ReleaseMeshes()`. They share the geometry's loading state with the game's
     mesh streaming, and `ReleaseMeshes` disposes it. Load into our own buffers instead.
   - Vanilla textures are virtual-textured. The non-VT material path gives untextured (dummy) materials.
3. **Meshes are streamed by the main camera's distance.** That makes "one real copy far away and a hidden camera on it"
   unreliable: a far copy is never loaded or drawn. Every asset's geometry can still be read from its file on demand:
   vanilla `.cok` blobs and mod `.cok` packages are the same kind of `GeometryAsset`.
4. **Front is local +Z** (buildings: the road side; benches and lamps: the side you sit on or that faces the path).
   No component says so per asset. `PlaceableObjectData.m_RotationSymmetry` says when the front does not matter, but is
   almost never set. The picture with a +Z arrow on it is the real answer.

## 1. What the game offers

### Icons and thumbnails

- `ImageSystem.GetIcon(prefab)` returns `UIObject.m_Icon`, or null (`game:Game.UI/ImageSystem.cs:61-68`).
  `GetThumbnail(prefab)` returns the icon if there is one, otherwise `"{prefab.thumbnailUrl}?width=128&height=128"`
  (`ImageSystem.cs:101-116`), with `thumbnailUrl = "thumbnail://ThumbnailCamera/" + id`
  (`game:Game.Prefabs/PrefabBase.cs:32`).
- That URL is served by `GameUIResourceHandler.TryThumbnailRequestAsync`
  (`game:Game.UI/GameUIResourceHandler.cs:299-394`). It finds the prefab and calls
  `ThumbnailCache.GetThumbnail(prefab, w, h)`, then answers with `info.atlasFrame.texture` cropped to `info.region`
  (`:368-374`), or "Thumbnail not found".
- **`ThumbnailCache` renders nothing.** `GetThumbnail` is only a dictionary lookup keyed by `(name, width, height)`
  (`game:Game.UI.Thumbnails/ThumbnailCache.cs:214-224`). The dictionary is filled only from
  `AssetDatabase.global.GetAssets<AtlasAsset>()` entries (`LoadAtlas`, `:155-174`, subscribed in `Initialize`,
  `:196-199`), and `Update()` is empty (`:226-228`). So a thumbnail exists only if some atlas asset contains an entry
  named exactly like the prefab at 128×128.
- `ThumbnailCustomPass` (`game:Game.UI.Thumbnails/ThumbnailCustomPass.cs`) draws a "thumbnail layer" into its own
  buffer. It is the old live renderer and has **no users** in Game.dll (grep: only its own file). The pattern is still
  useful (§2).
- The mod already resolves icons in this order: ui → swatch → thumbnail URL → group
  (`Game/Assets/AssetCatalogSystem.Icons.cs:29-60`). Its header notes that the thumbnail URL "renders nothing" for
  surfaces; per the above, for objects it shows something only when a baked atlas has the name.
- There are no per-prop PNGs in the install. `Cities2_Data\Content\Game\UI\Media` holds only SVG UI and category icons
  (e.g. `Editor/Thumbnails/Fallback_*.svg`). Prefab art lives inside `.cok` blobs.

### How many have a picture

From the user's catalog dump
(`%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\ModsData\Placemaker\catalog-dump.json`, 2026-10-27, user's
playset). It covers 2,973 unique catalogued items; the uncatalogued library is not in it.

| source | vanilla | mod |
|---|---|---|
| `coui://ail/...` (Asset Icon Library) | 1,879 | 357 |
| `Media/...` (vanilla icon or group SVG) | 219 | 6 |
| `assetdb://paradoxmods/...cok@*.jpg` (icon inside the mod package) | – | 158 |
| `coui://extraassetsimporter/...` (EAI files on disk) | – | 107 |
| `assetdb://Global/...` | – | 48 |
| `thumbnail://ThumbnailCamera` (no icon at all) | 179 | 9 |

- About **94 %** of catalogued items have a real icon, but **AIL supplies about 75 %** of them. Without AIL, vanilla
  props are mostly icon-less: the vanilla `Media` icons are mostly group or category icons, not per-prop pictures.
- **AIL's folder is the real treasure.** `ModsData\AssetIconLibrary\.Thumbnails\` holds 13,809
  `StaticObjectPrefab.<name>.png` files, plus `BuildingPrefab.*`, `Colored\`, `ColoredPropless\` and others:
  38,016 files in all, 256×256, AIL version 4.1.1.0, the files dated 2025.
  - Each picture is a clean 3/4 view of the asset alone on a transparent background (checked:
    `StaticObjectPrefab.GardenBench01.png`, the bench seen from its front-left). That is about one per vanilla static
    object, readable as plain files with no game API.
  - Gaps: AIL is a mod (not every player has it); it covers vanilla, not the user's asset packs (none found for
    `Bollard01`, `PierBench01`, `Pack10-Bench01`); there is a single angle with no origin; and the camera framing per
    asset is unknown (AIL's art, not ours to redistribute).
- Modded assets usually ship their own icon: 98 % of catalogued mod items have one, as a JPG/PNG inside the `.cok`
  (`assetdb://`) or a file in the mod's folder (EAI).

### Reading the pixels

- **Files on disk** (AIL, EAI, our own): `File.ReadAllBytes`. A `coui://<host>/<path>` maps to folders kept in
  `DefaultResourceHandler.HostLocationsMap`, which is `internal` (Colossal.UI `DefaultResourceHandler`, line 393), so it
  is read by reflection through `UIManager.defaultUISystem.resourceHandler`. AIL's folder can also simply be hard-coded.
- **Icons inside packages** (`assetdb://...`): `AssetDatabase.global.TryGetAsset(uri, out IAssetData a)`
  (`aadb:AssetDatabase.cs:533-538`). An `ImageAsset` is a `FileAsset` (`aadb:ImageAsset.cs:13`), so
  `((FileAsset)a).GetReadStream()` (`aadb:FileAsset.cs:118`) gives the original **PNG/JPG bytes**, with no GPU work.
  `ImageAsset.Load(bool srgb)` gives a `Texture2D` if a resize is needed (`:154`).
- **Baked atlas thumbnails:** `GameManager.instance.thumbnailCache.GetThumbnail(prefab,128,128)` returns `atlasFrame.texture`
  (a `Texture2D`, `aadb:AtlasFrame.cs:32`) and a `region`. Crop with `Graphics.CopyTexture` or a Blit into a
  RenderTexture, `ReadPixels`, then `EncodeToPNG`: the same steps the mod already uses for surface swatches
  (`Game/Assets/SurfaceSwatches.cs:104-122`).
  - **Unknown:** whether any vanilla atlas ships at all. The spike counts `GetAssets<AtlasAsset>()`.
- SVG icons (`Media/*.svg`) cannot be turned into pixels without a rasteriser. They are group icons, so skip them.

## 2. Rendering a prefab off-screen

### How the game draws objects (and why its renderer cannot be borrowed)

- Objects are drawn through one `BatchRendererGroup`. Its culling callback culls only the **active instance groups**
  (`game:Game.Rendering/BatchManagerSystem.cs:1446-1515`).
- Which batches are active, which LOD they use, and which meshes get loaded are all decided from the **main camera**:
  `BatchDataSystem.OnUpdate` takes the position from `CameraUpdateSystem.TryGetLODParameters` (`BatchDataSystem.cs:2414-2421`),
  computes `CalculateMinDistance`/`CalculateLod` per batch (`:1589-1590`), and writes `m_BatchPriority` (`:1669`).
- `BatchMeshSystem` loads geometry by that priority under a 1.5 GB budget (`memoryBudget = 1610612736`,
  `BatchMeshSystem.cs:331`; budget checks at `:899`, `:1283-1305`). Its generated meshes are uploaded
  `markNoLongerReadable: true` (`:621`): GPU only.
- So a copy placed far from the player's camera is never in an active group, and may not even have its mesh loaded. A
  copy near the camera is visible to the player. **"Place a real instance in a far corner and aim a hidden camera at
  it" does not work** unless the main camera goes there, which is exactly what the suite runner does and the player must
  never see.

### What the game itself uses for a preview

- `Game.Rendering.Debug.RenderPrefabRenderer` is a MonoBehaviour used by the in-game asset importer
  (`game:Game.AssetPipeline/AssetImportPipeline.cs:3206`, `:3231`) to show `RenderPrefab`s **as ordinary GameObjects**.
- Its `Instance` constructor (`game:Game.Rendering.Debug/RenderPrefabRenderer.cs:112-227`):
  - `prefab.ObtainMeshes()` and `prefab.ObtainMaterials(useVT)`;
  - per mesh, a child GameObject with `MeshFilter` + `MeshRenderer`, its sub-mesh materials sliced in order
    (`:193-216`);
  - VT: per material, `TextureStreamingSystem.BindMaterial(material, stackGlobalIndex, j, paramBlock)`, plus a
    `VTTextureRequester` that requests the full 0..1 texture area for alpha-tested, impostor and "manual VT" meshes,
    and the `m_TextureArea` for decals (`:131-185`);
  - per-instance looks through `MaterialPropertyBlock`: colour variations (`SetColorProperties`, `:386`; the shader
    ids `colossal_ColorMask0..2`, `:572-592`), decal properties (`:401`), emissive (`:433`); and `colossal_LodFade = 1`
    (`:135`).
  - This proves the game's shaders render correctly outside the batch renderer, as plain MeshRenderers, in a shipped
    build.
- An object prefab's parts: `ObjectGeometryPrefab.m_Meshes : ObjectMeshInfo[]` (`game:Game.Prefabs/ObjectGeometryPrefab.cs:11`).
  Each has `m_Mesh` (a `RenderPrefab`), `m_Position`, `m_Rotation`, `m_RequireState` (`ObjectMeshInfo.cs:12-20`).
  For trees take `ObjectState.Adult`, or the `None` meshes (`game:Game.Objects/ObjectState.cs`). LODs are in
  `LodProperties.m_LodMeshes` (`LodProperties.cs:14`); use the base mesh.

### Trap 1: the mesh load is shared

- `RenderPrefab.ObtainMeshes()` calls `GeometryAsset.ObtainMeshes()` (`game:Game.Prefabs/RenderPrefab.cs:193-197`).
  That loads synchronously into the asset's **own** `m_Data`/`m_Loading` (`aadb:GeometryAsset.cs:764-778`).
- These are the same buffers the game's streaming fills through `RequestDataAsync` (`:1061-1083`).
  `ReleaseMeshes()` destroys the meshes **and disposes `m_Data`/`m_Loading`** when its own reference count reaches 0
  (`:780-793`), even if `BatchMeshSystem` is using them. Expect a native crash or vanishing city meshes.
- Never releasing them leaks a mesh per asset viewed instead.
- **The safe way:** our own `GeometryAsset.Data` and `Loading` structs.
  1. `desc = geometryAsset.GetAsyncReadDescriptor()` (`aadb:AssetData.cs:148`).
  2. `GeometryAsset.LoadSync(desc, uint.MaxValue, ref myData, ref myLoading)`: public static, a synchronous file read
     plus decompression (`aadb:GeometryAsset.cs:856-890`).
  3. Build the Unity `Mesh`es with the private static `CreateMeshes(name, ref Data)` (`:1304`, by reflection). It
     handles the game's vertex layout (octahedral normals, packed attributes).
  4. Dispose `myData`/`myLoading`.

  This works for any `RenderPrefab.geometryAsset`, **vanilla and modded alike**. Everything comes from the asset
  database's file (`.cok`), whether or not the game currently has it streamed in.

### Trap 2: textures are virtual

- `SurfaceAsset.Load(useVT:false)` skips any texture that `IsHandledByVirtualTexturing` reports.
  - That check returns **true for dummy textures even when VT is off** (`aadb:SurfaceAsset.cs:375-389`; set loop
    `:600-614`). Shipped VT materials keep their pixels only in the VT stacks, so `useVT:false` gives grey,
    untextured materials (expected; the spike confirms).
  - Use the VT path exactly as `RenderPrefabRenderer` does, and wait a few frames for the tiles to stream in.
- `SurfaceAsset.Load` returns **one shared `Material` per surface** (`aadb:SurfaceAsset.cs:671-698`; ref-counted, the same
  instance on every call). The game's batches do not use it: `ManagedBatchSystem.CreateMaterial` builds its own copies
  from the surface's properties (`game:Game.Rendering/ManagedBatchSystem.cs:1346-1392`).
  - Others may still use the shared one, though: the asset importer, and mods like EAI.
  - **Copy it** (`new Material(shared)`) before setting keywords or VT bindings. `RenderPrefabRenderer` mutates the
    shared one; we must not.
  - `ReleaseMaterials()` is a no-op (`RenderPrefab.cs:254-257`), so materials stay loaded. Keep a small LRU of our copies.
- Decals project onto other geometry, so in an empty preview there is nothing for them to land on. For decals, draw
  the top view from the decal's albedo, cropped to `DecalProperties.m_TextureArea` (`game:Game.Prefabs/DecalProperties.cs:13`),
  as `SurfaceSwatches` does for surfaces. A 45° view of a decal says nothing anyway.

### The camera

- **What the mod already has.** `SuiteCapture.RenderTexture2D` (`Game/Suite/SuiteCapture.cs:199-251`) renders the
  **main** camera into a `RenderTexture` the way the game's own screenshot does (`game:Game.UI/ScreenCaptureHelper.cs:93-111`):
  `camera.targetTexture = rt; camera.Render()` ×8 for TAA history, the UI off, then `ReadPixels` and `EncodeToPNG`.
  It proves `Camera.Render()` into a RenderTexture works in CS2's HDRP. It is a player-view capture, though: it hides the
  UI for those frames.
- **For assets, use a second, persistent, disabled `Camera`** (we call `Render()` ourselves):
  - **On a private layer.** Pick a layer whose `LayerMask.LayerToName` is empty, e.g. 31. The game names only Marker,
    Moving, Outline, Pipeline, SubPipeline, Tunnel and Waterway (grep `NameToLayer`). The asset's GameObjects go on that
    layer, and `cullingMask = 1 << 31`, so the city's BRG batches and the game's `Graphics.DrawMesh` overlays (layer 0)
    are excluded.
  - **Far away and above** the map (e.g. y = 5,000 m) with `farClipPlane` ≈ 4× the asset radius, so terrain and water
    are out of range.
  - **`cameraType = CameraType.Preview`.** The game's per-camera draws (areas, overlays, brushes, notifications,
    aggregates) only run for `Game`/`SceneView` cameras (`game:Game.Rendering/AreaRenderSystem.cs:97,108,174`,
    `OverlayRenderSystem.cs:897`, `NotificationIconRenderSystem.cs:157`, …). HDRP also treats Preview cameras as asset
    previews. **To test:** HDRP may change sky or exposure for Preview; if it misbehaves, keep `Game` and rely on
    layer and range.
  - **`HDAdditionalCameraData`:**
    - `clearColorMode = Color` on a neutral grey or transparent background;
    - `antialiasing = None` or SMAA, so one `Render()` is enough with no TAA history (SuiteCapture uses SMAA for
      the same reason, `:222-228`);
    - `allowDynamicResolution = false` and `allowDeepLearningSuperSampling = false`: DLSS must stay off this camera.
      The user's DLSS 5 / RenoDX hang is about the window's swap chain changing size, and an off-screen RT does not
      touch it, but no upscaler should ever run on it;
    - `customRenderingSettings = true`, with the frame settings for CustomPass, volumetrics, SSR, SSAO, motion vectors
      and contact shadows off. CS2's global custom passes (terrain/underground, outlines, debug) then do not run for
      this camera;
    - fixed exposure: a `Volume` on the private layer with `volumeLayerMask` = that layer.
  - **Fixed RT size** (256×256, or 512 downscaled), allocated once. HDRP keeps per-camera history buffers, so never
    resize.
  - **Lighting is the world's.** The sun lights our object (at night it is dark). Choices:
    - render only in daylight;
    - or, around our own `Render()` call only, switch on a private directional light and switch the sun off,
      restoring both straight after. It runs synchronously on the main thread, so the player's frame never sees it.
      The risk is cached shadow maps.
- **Alternative, the game's own pattern:** a `CustomPass` that calls HDRP `CustomPassUtils.RenderFromCamera(ctx, ourCamera,
  rt, ClearFlag.All, layer)` (HDRP `CustomPassUtils`, lines 464-509), inside the main camera's frame. It is cheaper (no
  second HDRP frame), but it draws from the main camera's `cullingResults`: objects outside the player's frustum are
  culled. It needs `AggregateCullingParameters` hacks and oversized renderer bounds. Not the simplest; keep it as a
  fallback if a second camera costs too much.
- **Marks:** do not render them. After the readback, project these points with `camera.WorldToScreenPoint` and draw
  them into the `Texture2D` in C#, as the mod already draws its plan maps:
  - the origin;
  - `ObjectGeometryData.m_Bounds` corners (the box);
  - a +Z arrow from the origin.

### Cost

- **Geometry:** one `LoadSync` per asset, a few ms for a prop, tens of ms for a building, on the main thread
  (`JobHandle.Complete`). Spread it: one asset per frame at most, only when the AI asks.
- **Render:** one HDRP camera render of a 256² target with features off. Unknown in CS2: HDRP's fixed per-camera CPU
  cost might be 2–6 ms. Two views per asset means two `Render()` calls on different frames.
- **VT:** tiles appear over a few frames. Render, check that the texture is not grey, and re-render after N frames if
  needed.
- **Cache:** PNGs in `ModsData\Placemaker\looks\<prefab>__<view>.png` (+ a JSON with bounds, front, mod id/version). Each
  asset is drawn once per install, not once per game. 14,000 × 2 × ~40 KB is about 1.1 GB if everything were ever
  drawn, so draw on demand only (the AI asks for maybe 50 per session).

## 3. Modded assets' meshes

- Mod packages (`pdx_mods\...\*.cok`) are `ILocalAssetDatabase` sources like the vanilla `Blob*.cok`. Their
  `RenderPrefab.geometryAsset` and `SurfaceAsset`s are ordinary assets in `AssetDatabase.global`, read through the same
  `AsyncReadDescriptor` (`aadb:GeometryAsset.cs:773`).
- The geometry is **not** resident until something streams it. The game streams it only by main-camera distance (§2).
  A synchronous file read (own `Data`, `LoadSync`) gets it at any time, CPU-readable before `CreateMeshes` makes the
  Unity `Mesh`.
- The asset importer shows freshly imported meshes the same way (`RenderPrefabRenderer`, `ObtainMeshes`); it never
  goes through `BatchMeshSystem`.
- Mod surfaces are VT-reserved at load (`PreReservedAtlassingInfos`, used as a fallback in
  `RenderPrefabRenderer.cs:138-141`), so the VT path works for them too.
- Assets made at runtime by code mods (EAI decals, RoadBuilder pieces, Placemaker's own copies) have
  `GeometryAsset`s in their mod's registered database, so the same path applies. A prefab whose `RenderPrefab` has no
  geometry asset (`hasGeometryAsset == false`, `RenderPrefab.cs:71`) gets "no picture".

## 4. Front direction

- **Buildings: +Z is the road side.**
  - `BuildingUtils.CalculateFrontPosition` is local `(0, 0, lotDepth·4)`, i.e. the middle of the +Z lot edge
    (`game:Game.Buildings/BuildingUtils.cs:88-92`).
  - The object tool samples the terrain there for road-side buildings (`game:Game.Tools/ObjectToolSystem.cs:1626-1630`).
- **Props snapped to a road (lamps, signs, benches on sidewalks):** the tool sets
  `rotation = LookRotation(direction)` (`game:Game.Tools/ToolUtils.cs:24-30`), with `direction` the net area's
  `Left(tangent)` or `Right(tangent)` per `NetAreaFlags.Invert`, and a perpendicular for medians
  (`ObjectToolSystem.cs:1787-1824`). The prop's local +Z is turned perpendicular to the road. Which way, toward or away,
  depends on the area; it is not a per-prop setting.
- **Placemaker's own finding:** "CS2 benches and lamps face local +Z" (`docs/RESEARCH-NOTES-ADDENDUM.md:300-304`;
  `PlannedObject.Yaw` 0 = facing +Z). The lab's generated shapes put their front toward −Z
  (`Game/Suite/SuiteLabMesh.cs:16`), which is our own convention, not the game's.
- **No component marks a front.**
  - `ObjectGeometryData` flags (`game:Game.Objects/GeometryFlags.cs`) carry none.
  - `PlaceableObjectData` has `m_RotationSymmetry` (None/180/90/45/Any, `game:Game.Objects/RotationSymmetry.cs`;
    `PlaceableObjectData.cs:19`). It is set only by `SubObjectDefaultProbability` (`SubObjectDefaultProbability.cs:15,35`),
    so almost every prop has `None`. When it is set, `Any`/`_90` tells the AI "rotation does not matter" (the tool then
    hides rotation, `ObjectToolSystem.cs:2441`).
  - `PlacementFlags` (`game:Game.Objects/PlacementFlags.cs`): `RoadSide`, `RoadEdge`, `Wall`, `Hanging`, `Shoreline`, …
    say how the asset attaches, which implies a back (wall) or a road side.
- **So:** assume +Z is the front for every object, and let the pictures show the AI the truth. Draw a +Z arrow on
  both views, and add a third, straight-on view from +Z (looking toward −Z) when the AI asks "which way does it face".
  A statue looking at the camera in the +Z view is facing +Z.

## Recommendation (simplest that also covers mods)

`assets.look(id)` returns 1–3 PNGs plus facts, best source first, cached on disk:

1. **The asset's own picture, no rendering:**
   - AIL's file (`.Thumbnails\StaticObjectPrefab.<name>.png`) if present;
   - else the prefab's `m_Icon`: `assetdb://` bytes via `FileAsset.GetReadStream()`, `coui://` via the host folder;
   - else a baked atlas frame, if any atlas exists.

   This covers about all vanilla static objects when AIL is installed (the user has it) and most asset-pack items. It
   costs nothing and stays the "what is it" picture.
2. **Our own render for the AI's working views** (45° and top, origin, box and +Z arrow drawn on), for any asset with
   geometry, vanilla or mod:
   - an isolated GameObject copy built from `ObjectGeometryPrefab.m_Meshes`, with meshes from our own `LoadSync`
     buffers and copied materials with VT bound as `RenderPrefabRenderer` does;
   - a persistent, disabled, private-layer Preview camera far above the map;
   - one `Render()` per view, on separate frames; `ReadPixels` to PNG; marks drawn in C#; the GameObjects and meshes
     destroyed after.

   Decals get a top-down texture swatch instead.

Facts sent with the pictures:
- bounds (`ObjectGeometryData.m_Bounds`);
- `PlacementFlags`;
- `m_RotationSymmetry`;
- "front = +Z (assumed; see the arrow)".

### Risks

- **VT textures** may need several frames, or an explicit `VTTextureRequester`, before they are not grey; that is the
  main unknown.
- **Shader paths outside the BRG**: colour masks give black or white parts unless the `ColorProperties` block is set
  (copy `SetColorProperties`); trees may sway (wind globals) or need `_GPU_ANIMATION_*` keywords off.
- **HDRP second-camera cost**, and side effects of global custom passes or volumes if the frame-setting overrides do
  not hold.
- **Lighting at night**, and exposure.
- **Reflection** into `CreateMeshes` and `HostLocationsMap` (internal APIs; a game patch can change them; fall back to
  "no picture").
- **Never call `RenderPrefab.ReleaseMeshes`/`ObtainMeshes`, or mutate shared `SurfaceAsset` materials:** that can
  crash or corrupt the city's rendering.
- **AIL** is a third-party mod: its pictures are for the AI only, never redistributed, and absent for players without
  it.
- **The game runner runs a clean profile** (no AIL loaded). AIL's files are still on disk and are read as files, but
  "the asset's own icon" coverage there is lower than in the user's game.

## Minimal spike (described, not run)

A lab op in the game runner. Its pictures land in `shots/` through the same copy the AI steps use
(`Game/Suite/SuiteSteps.Ai.cs:68-83`).

- **Code:** `Game/Suite/SuiteLab.AssetLook.cs`, op `assetlook`, step
  `{"do":"lab","op":"assetlook","names":["GardenBench01","SmallFountain01","StreetLamp...","Tree...","<a mod prop>"],"views":["45","top","front"],"useVT":true,"waitFrames":8}`.
  For each name:
  1. Find the prefab, its `ObjectGeometryPrefab.m_Meshes` (Adult or None state) and `ObjectGeometryData.m_Bounds`.
  2. For each `RenderPrefab`: `LoadSync` into its own `Data`/`Loading`, then `CreateMeshes` by reflection, then dispose.
     Log the vertex count and the ms.
  3. Materials: `new Material(renderPrefab.ObtainMaterial(i, useVT))`; when `useVT`, bind VT as in
     `RenderPrefabRenderer.cs:131-185`; set `colossal_LodFade = 1`; a `MaterialPropertyBlock` with the first colour
     variation.
  4. A root GameObject at (0, 5000, 0) on the free layer, children per mesh at `m_Position`/`m_Rotation`.
  5. Camera as in §2 (Preview type, private layer, clear colour, no AA or DLSS, custom frame settings), framed on the
     bounds: 45° from the +Z/+X side, top-down with +Z up, front from +Z.
  6. Wait `waitFrames`, `Render()` one view per frame, `ReadPixels`, draw the origin cross, box and +Z arrow, and save
     `shots/look-<name>-<view>.png`. Destroy everything.
  7. Also log, per name:
     - AIL file present or not;
     - `ImageSystem.GetIcon`;
     - for `assetdb://` icons, the byte count read through `GetReadStream`;
     - `thumbnailCache.GetThumbnail(prefab,128,128) != null`;
     - the count of `AtlasAsset`s in the database;
     - `m_RotationSymmetry` and `PlacementFlags`.
  8. Log the frame time of each `Render()` frame and the 5 frames around it (as `polishIdle` does), so stutter is
     measured.
- **Scenario:** `suite/scenarios/lab-asset-look.json`:
  1. a flat land, the camera on it (so the player-like view is busy);
  2. the `assetlook` step with about 8 names: bench, lamp, statue, fountain, tree, bush, sign, one modded prop. The clean
     profile has no asset packs; for the mod case, a second run with an asset-pack mod enabled only if
     `docs/AI-BUILDER.md` allows it (memory: never `-AllMods`);
  3. one normal `45` shot of the city afterwards, to confirm the player's view and the city's meshes are untouched.

  Run it with `powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/lab-asset-look.json -Who asset-look`
  (in the background, through the queue).
- **Read:**
  - the pictures: textured or grey (VT), lit or dark, colours right, front arrow on the sitting side of the bench and
    the lamp's arm side;
  - Placemaker.log: ms per load and per render, worst frame, how many had AIL, icon or atlas pictures;
  - Player.log: no errors or warnings from HDRP or the VT system;
  - the final city shot: identical to one before the step.
- Then run `useVT:false` once to confirm the grey-material prediction, and the same names with `cameraType Game` to see
  what Preview changes.
