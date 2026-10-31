# Placemaker architecture & working rules

Read with `DESIGN.md` (UX) and `RESEARCH-NOTES.md` (CS2 engine rules — **mandatory checklist**).

## Big idea: recipes of layers

An area runs a **recipe**: a tree of layers (`Core/Composition.cs`).

| Layer kind | Examples | Does |
|---|---|---|
| Fill | park, plaza, parking, roads, campground | turns a region into something; may emit sub-regions (`block`, `green`, `edge`, `core`…) |
| Split | split (divider line / ratio) | cuts a region into `part:0`, `part:1`… |
| AddOn | fountain, playground, restrooms, edge zoning, picnic area (big trees, rocks, ponds: the layers stay, shown as settings) | adds to a filled region, avoiding `ReservedSpace` |

Built-in "types" in the Make row are shipped recipes (`Recipes/*.json`): Parking, Plaza, Park,
Roads, Roads + Parks, Park | Plaza split… Player recipes later = same JSON saved in ModsData.

**Adding a new type** = one class implementing `IAreaGenerator` with `[AreaGenerator(order)]`,
its `Lang/en-US.<layer>.json` keys, optionally a shipped recipe JSON. Nothing else changes:
registry, schema-driven UI, undo, presets, receipts and composer pick it up.

## Controls model: Type → Style → Settings → Add-ons (Phase 1, 2026-10-30)

What the player picks and tunes, top to bottom in the Design panel, and where a new knob goes:

| Level | What it is | Where it lives | Example |
|---|---|---|---|
| **Type** | what the place is | one fill layer (`IAreaGenerator`), on the shelf | Park, Car park, Quarry |
| **Style** | a finished look of that type: a set of setting values | `DefinePresets()` (`Preset`, `PresetFeatures`) | Formal, Woodland, Neglected |
| **Settings** | every knob of the look: amounts, choices, switches | `DefineControls()`; groups are the panel's sections | Tree density, Big trees, Water, Rocky areas |
| **Add-ons** | real facilities the player places on purpose | `IHasFeatures.Features` (a child step of an add-on layer) | Tennis courts, Playground, Restrooms, Kiosk |

**When to use which.**
- It changes how the place *looks* (more or fewer, which kind, how worn, how shaped): a **setting**. Its styles preset
  it. Never make a look an add-on (the user: "Why is big trees an add-on? Shouldn't that be part of the artistic vision?").
- It is a *thing people use* that the player may or may not want (a court, a toilet block, a bandstand): an **add-on**.
- A grungy or neglected version is a **style** (or its own type), never a slider: neglect changes everything at once.
- An add-on layer can still do the work of a setting: register it in `Core/Layers/Common/LookSettings.cs` (feature id,
  control id, levels: off plus a preset of the feature layer, values, or `"*key"` = the host's value times a factor).
  `LayerBase` then gives every type that offers the feature one control in the right section, turns its styles'
  feature lists into the control's value, and moves the feature's own options under it; `Features.Of` stops listing it;
  the composer makes the child from the value (`LookSettings.Children`). Designs saved with the old add-on child keep
  building exactly as before until the player touches the setting.
- Everything about the ground goes in one **Ground** section (`Core/Layers/Common/GroundSection.cs`): group
  `groundPaint` (Terrain paint: painted into the terrain texture), `ground` (Ground cover: surfaces, gravel, rubble),
  `terrain` (Shape: mounds, rocky areas, patches). The Decals switch (`dressGroundDecals`) is on its heading; every
  decal a type lays must check `DressingControls.GroundDecalsOn`.

**Every shown control must change the plan.** `Tests/ControlEffectTests` plans every shelf type and moves each shown
control; one that changes nothing fails the test. Fix it, hide it (`Core/Tool/ControlsShown.cs`: hidden, never deleted;
saved values still load), or, when it only acts on some land (water, slopes), grey it out with a reason
(`Core/Tool/Availability.cs`) and list it in the test's `Conditional` with why.

**Requirements and size.** A type that needs an entrance plans nothing until it has one (the yellow "Add an entrance"
button leads there). Each Make-row group has a size its types are made for (`Core/Tool/SizeGuide.cs`, or
`LayerBase.ComfortMaxArea`); past it the panel warns that it still works but bulldozing may be slow.

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
