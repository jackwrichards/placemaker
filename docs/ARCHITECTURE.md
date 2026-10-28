# Placemaker architecture & working rules

Read with `DESIGN.md` (UX) and `RESEARCH-NOTES.md` (CS2 engine rules — **mandatory checklist**).

## Big idea: recipes of layers

An area runs a **recipe**: a tree of layers (`Core/Composition.cs`).

| Layer kind | Examples | Does |
|---|---|---|
| Fill | park, plaza, parking, roads, campground | turns a region into something; may emit sub-regions (`block`, `green`, `edge`, `core`…) |
| Split | split (divider line / ratio) | cuts a region into `part:0`, `part:1`… |
| AddOn | fountain, playground, restrooms, big trees, edge zoning, picnic area | adds to a filled region, avoiding `ReservedSpace` |

Built-in "types" in the Make row are shipped recipes (`Recipes/*.json`): Parking, Plaza, Park,
Roads, Roads + Parks, Park | Plaza split… Player recipes later = same JSON saved in ModsData.

**Adding a new type** = one class implementing `IAreaGenerator` with `[AreaGenerator(order)]`,
its `Lang/en-US.<layer>.json` keys, optionally a shipped recipe JSON. Nothing else changes:
registry, schema-driven UI, undo, presets, receipts and composer pick it up.

## Folder layout and ownership

```
Core/                  engine-free (.NET + Unity.Mathematics ONLY). Unit-tested on net9 without the game.
  Contracts.cs         IAreaGenerator, AreaInput/AreaPlan, schema, ReservedSpace      (owner: lead)
  Composition.cs       Recipe, Composer, Seeds, Pricing                                (owner: lead)
  AssetCategories.cs   shared asset vocabulary                                          (owner: lead)
  GeneratorRegistry.cs                                                                  (owner: lead)
  Geometry/            polygon math, surface acceptance, offset, brush mask, net graph  (owner: geometry)
  Layers/Park/  Layers/Plaza/  Layers/AddOns/  Layers/Split/                           (owner: park-plaza)
  Layers/Roads/  Layers/AddOns/EdgeZoning*                                            (owner: roads)
  Layers/Parking/                                                                       (owner: parking)
Game/                  CS2 systems (Game.*, Unity.Entities)
  Abstractions.cs      IBuildService, IGroupRegistry, IAssetCatalog, Services           (owner: lead)
  Tool/  Selection/  Overlay/  Preview/  UI/  Settings/  Localization/                 (owner: game-tool)
  Build/  Groups/  Assets/  Economy/                                                    (owner: game-build)
UI/src/                React panel, schema-driven; contract.ts is authoritative          (owner: ui)
UI/mock/               browser harness: real panel + mock backend; Playwright             (owner: ui)
Lang/en-US.<area>.json one file per area so parallel work never conflicts; loader merges Lang/<locale>*.json
Recipes/*.json         shipped recipes                                                  (owner: each layer)
Tests/Placemaker.Tests xUnit, links Core/**                                             (all; one file per area)
```

Only edit files you own. Need a contract change? Don't edit `Contracts.cs`/`Composition.cs`/
`Abstractions.cs`/`contract.ts` — write the proposal in `docs/proposals/<you>.md` and work
around it; the lead merges contract changes.

## Code rules

1. **Core is pure.** No `Game.*`, `Colossal.*`, `Unity.Entities`, `UnityEngine` in `Core/`. Only `Unity.Mathematics`.
   C# 9 / net48-compatible (no `Span` tricks needing net core, no `init`, no records, no default interface methods).
2. **Layers are deterministic and cancellable.** Same input → same plan. Check `cancel` between phases.
   Never throw for bad geometry; return `PlanIssue`s. Budget: < 250 ms for a 5 ha area on one core.
3. **Never emit a surface CS2 will silently drop.** Every `PlannedSurface`/`PlannedArea` ring passes
   `Core/Geometry/SurfaceAcceptance` (0.1 m inset + ear-clip check, no edges < 0.3 m, no needles).
4. **Respect `input.Reserved`** (add-ons especially) and register what you place in your plan so the composer reserves it.
5. **No game asset names hard-coded as the only option.** Use `AssetRef(id, fallbacks…)` + categories; empty id = automatic.
6. **Localisation keys only** in plans/issues/schema (`Placemaker.<Area>.<Name>`), English text in `Lang/en-US.<area>.json`.
7. **Settings stability:** control ids and layer ids are save format. Never rename; add new ones.
8. Game layer: follow every rule in `RESEARCH-NOTES.md`. No Harmony unless unavoidable (and then guarded with `HarmonyPrepare` + try/catch at install). No debug hotkeys in release.
9. **No `SystemAPI`, `IJobEntity`, aspects** (we must compile without the Entities source generators). Use `EntityQuery`, `ComponentLookup`, `IJobChunk` or main-thread loops.
10. Comments explain *why* (engine constraints, measured numbers), not what. English only.
11. Every Core feature ships with tests in `Tests/Placemaker.Tests/<Area>Tests.cs`.

## Build & test

```
dotnet build Placemaker.csproj -c Release -p:SkipUI=true -p:Deploy=false   # compile check vs installed game DLLs
dotnet test Tests/Placemaker.Tests                                         # Core unit tests, no game needed
cd UI && npm run build                                                     # UI bundle
```
`Deploy` defaults to on: copies to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\Placemaker`.
With the official toolchain installed (Options → Modding), the csproj switches to Mod.props/Mod.targets automatically.

## Runtime flow

```
ToolSystem (stage machine) ─ Selection (draw|block|paint|perimeter) → AreaShape
      │                       History (shape + recipe edits, one stack)
      ├─ SiteCapture (main thread snapshot: heights, nearby nets, obstacles, AssetInfo)
      ├─ PreviewWorker: Composer.Compose(recipe, area, site) on a worker; latest request wins
      ├─ Overlay + FilledPreview draw the ComposedPlan; Summary = stats + Pricing
      └─ primary(build) → IBuildService.Start(plan) → definitions → Temp → validate → apply → group
UI ⇄ PlacemakerUISystem: 6 JSON values (state, catalog, design, summary, assets, managed) + triggers (contract.ts)
```

- **Footpaths at city streets** (`Core/Layers/Common/StreetJoins`, run by the composer for every type): a visible footpath
  stops its round end a quarter metre off the street and the game joins it to the sidewalk (LocalConnect): no cut road, no
  crosswalk. Only an entrance placed with the panel's Crosswalk switch (kind `pedestrian+crosswalk`, `MarkerVariants`)
  keeps its attach and splits the street, which draws the crosswalk. The build re-checks the clearance with the real path
  width (`BuildService.Emit`). Invisible paths keep their old join.
- **Ungroup** (`GroupFlags.Ungrouped`, `Game/Groups/GroupRegistrySystem.Dissolve.cs`): dissolves the build for good, in
  PreTool: the record, the owner lot, the park and parking companion buildings and the carrier go, the tags come off, and
  the props and trees leave the carrier (no Updated, so the game's override check never runs and nothing is hidden). The
  pieces are plain city pieces, as if built by hand. Zone block lots and a lake's water source stay, untagged (zoning is
  the exception; the raycast lift never picks a loose zone block lot). A lot with stall markings keeps a parking frame
  (`GroupFlags.Dissolved`): the carrier its parking lanes need, its markings and driving lanes, with a bare record that
  keeps the carrier's SubNet right; not in Manage, no fee, no upkeep; the markings go with the last driving lane. It
  cannot be undone. Review's "One group" switch off builds the group ungrouped: it is dissolved right after the build.
  Saves with ungrouped builds from before dissolve them after the load.

## Visual check (mandatory for layout work)

Render before you claim a layout is good:
`dotnet run -c Release --project Tests/Placemaker.Gallery -- --out docs/gallery` (≈35 s, real planners + real asset
footprints). Read the PNGs of the types/presets you touched and iterate until they look right. Renders are not
committed (ignored); `docs/gallery/REVIEW.md` holds known visual problems.
