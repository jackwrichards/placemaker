# The AI builder (design, 2026-10-26; nothing built yet)

An experimental category where an AI agent builds whatever the player describes, inside their selection, object by
object. The player pastes their own API key, types what they want, and gets a preview to approve.

## What the user asked

> Describe anything you want, and it would do it for you. ... The AI places every object itself, and it's taught
> exactly how, and it's given tools ... and all the pitfalls ... can be solved with the agent loop, where it can see
> what's going on. ... You'd paste your key in. And this would be an experimental setting.

Decided in the brainstorm (2026-10-26):

- **The AI places every object itself.** Not "AI picks a preset" and not "AI sketches, our planner fills in". The
  recipes and planners stay as they are; this is a separate category.
- **The safety comes from the tools and the loop**, not from limiting the AI: every tool reports what is wrong with
  what it just did, the AI sees its work (as pictures and as data) and fixes it before it says it is done.
- **Everything runs inside the mod.** No local bridge: the mod calls the AI's API with the player's key, runs the
  tools between frames and sends the results back.
- **The player does not have to watch.** No camera moves, no screenshots of their screen; the mod draws the pictures
  it sends from game data. They keep playing and look when the preview is ready.
- **Any selection shape** (triangle, C shape, squiggle, holes) works.
- **The AI gets the exact ground**, every height, not a description, and can reshape it.
- **It knows the surroundings**, not only the selection: coast, industry next door, the road network.
- **Chat is secondary.** The goal is "describe it, get it". The player can still say "fewer trees" after the preview.

## The loop

```
player: prompt + selection
  -> mod builds the site brief (data + pictures)
  -> AI: plan, then tool calls (look / search assets / place / reshape ground / check)
       each tool answers with its result AND the problems it caused
       the AI looks again (redrawn maps), fixes, repeats
  -> AI: "done" + a short note of what it built and why
  -> player sees the preview (ghosts, as today's Review stage)
       Build                    -> the existing build pipeline (Apply frame, receipts, undo)
       "change X" (optional)    -> back into the loop with the current plan
       Cancel                   -> nothing was ever built
```

Everything the AI places is a **preview ghost** until the player presses Build. Building goes through the existing
pipeline so the fixes it carries still hold (marker elevation on the Apply frame, the un-hide of overlapping pieces,
the zoning guards, receipts and undo).

The AI works in the background: API calls off the main thread, tool calls queued to a system that runs them between
frames within a time budget (the "zero stutter in a running city" rule applies here too).

## The site brief: what the AI is told

An overview first, details on request. Dumping everything into the first message wastes tokens and buries what
matters.

### Coordinates

Metres from the south-west corner of the selection's bounding box, x east, z north, heights in metres above sea level.
Every tool speaks this frame; the mod converts to and from world space. Directions in words (north, south-east) use
the same frame.

### The selection

- **The exact outline** as a list of points, plus holes. Total size and area.
- **An inside/outside mask** on the height grid (one cell per heightmap texel), so the AI never works out
  point-in-polygon itself. Odd shapes need nothing special: the mask is the truth, and every placement tool checks
  against it ("this tree is 2 m outside the selection").
- For a C shape or a hole: what sits in the gap is listed as part of the surroundings.

### The ground, exactly

- **Every height** of the game's heightmap inside the selection plus a margin around it. The heightmap has one texel
  per ~3.5 m on a vanilla map (14 336 m / 4096, `RESEARCH-NOTES-ADDENDUM.md` #85); a 200 × 200 m selection is about
  3 300 numbers, small enough to send whole. Large selections get the overview grid first and the full grid in tiles
  on request (`ground.heights(region)`).
- The **water level** and where water is, and the **surface** (grass, sand, rock, paint) per cell.
- A short summary on top (lowest, highest, steepest, which way it slopes) so the AI does not have to read 3 000
  numbers to know the site is a hill.

### What is inside the selection

- **Roads and paths:** each as a polyline with type, width, lanes, height, whether it is a city street or an inner
  road, and its junctions.
- **Buildings:** footprint, kind (house, shop, service, industry, ...), whether removing it is allowed.
- **Trees and props:** positions and kinds, grouped (counts plus where they cluster) in the overview, exact on request.
- **Zoning, water, power lines, pipes**, our own earlier builds.

### The surroundings

A ring around the selection (a few hundred metres, or a few times its size), at lower detail:

- **By direction:** coast, river, lake, industrial, dense housing, low housing, shops, offices, parks, farmland,
  forest.
- **Roads:** highways and main roads nearby, the roads that touch the selection and where (the possible entrances),
  where traffic comes from.
- **Landmarks and services:** a school, a station, a hospital next door.
- A wider map picture with the selection highlighted.

### Example: the start of a brief

```
Selection: 180 x 120 m bounding box, 5 corners, area 17 900 m². Origin = south-west corner, x east, z north.
Ground: 412-419 m, rises toward the north-east, steepest 9 % near (150,100). Full heights: 52 x 35 grid, 3.5 m.
Inside: road "Small road" (2 lanes, 8 m) (0,60) -> (180,55), city street, junction at (92,58).
        3 buildings: house at (20,80) 12x15 m, removable; ...
        41 trees, mostly in the north-west quarter.
Edges:  "Large avenue" (6 lanes) along the whole south side: possible entrances anywhere on z=0.
Around: houses to the north (low density), a container port 300 m east, the sea 450 m south.
```

## Pictures, drawn by the mod

No screenshots of the player's screen. The mod draws images from data and saves them as PNG (the plan sketch already
works this way):

- **Top-down maps:** outline, height colours with contour lines, roads, buildings, water, a labelled 10 m coordinate
  grid, and everything the AI has placed so far, labelled. Redrawn after each change. Fast and cheap; the main way the
  AI sees its work.
- **The wider map:** the surroundings at lower detail, the selection highlighted.
- **Asset pictures:** the game's own thumbnails (vanilla and most modded assets have one).
- **Optional "real look" render:** a second, hidden camera rendered to a texture, without touching the player's
  view. Heavier; at the end or when the AI asks. Not tried in CS2 yet (watch the DLSS / window-size crash in memory).
- **Assets with no usable thumbnail:** the same hidden camera on one copy placed off to the side.

## The tools

Every placing tool returns the new object's id **and** the problems it caused, at once ("the bench overlaps the lamp
by 0.4 m", "the path ends 1.2 m short of the road", "the tree is 2 m outside the selection").

### Look

| Tool | Returns |
|---|---|
| `site.brief()` | the overview above (again, after many changes) |
| `site.map(region, layers)` | a drawn top-down picture: heights, roads, buildings, placed objects, zoning, grid |
| `site.inspect(region)` | everything in a spot, exact positions |
| `ground.heights(region)` | the exact heights, water and surface per cell |
| `site.render(view)` | the optional "real look" picture |

### Assets

| Tool | Returns |
|---|---|
| `assets.search(query, kind, size)` | matches from the whole library, mods included: id, name, tags, footprint, picture |
| `assets.card(id)` | thumbnail, real footprint and height, what it is, whether it needs a road / is a building / a decal |

### Place

| Tool | Does |
|---|---|
| `object.place(asset, x, z, rotation)` | one object, sits on the ground |
| `object.move / rotate / remove(id)` | edits, including removing existing things the brief allows |
| `objects.row(asset, line, spacing)` | a row along a line (a hedge, a tree avenue) |
| `objects.scatter(assets, shape, density, spacing)` | a scatter inside a shape (a wood, a meadow) |
| `objects.along(asset, path, side, spacing)` | along a path edge (lamps, benches) |
| `net.node(x, z, height)` / `net.connect(a, b, type, curve)` | roads, paths, tracks, fences with the game's own snapping, heights and junctions |
| `surface.paint(shape, surface)` / `decal.place(...)` | ground paint and decals |

The "many at once" tools are still the AI deciding everything; they exist because 300 trees as 300 calls would cost
minutes and dollars.

### Ground

| Tool | Does |
|---|---|
| `ground.set(cells, heights)` | exact heights per cell |
| `ground.flatten(shape, height)` | one level |
| `ground.slope(shape, from, to)` | an even slope between two points or edges |
| `ground.raise / lower / smooth(shape, amount)` | brushes |

After each change it returns the new heights and what the change broke: a road left hanging, a building floating, a
tree buried, water that will now flood the spot.

### Check

`site.check()` runs every check over the whole plan: overlaps, blocked entrances, pits, objects floating or buried,
paths that do not connect, fences across walkways, unreachable spots, shops facing the wrong street, things outside
the selection. The AI must run it and get it clean (or explain what it left) before it may say done.

### Finish

`plan.done(note)` hands the preview to the player with the AI's short note.

## What it is taught (the big system prompt)

- **How the game works:** the rules from `RESEARCH-NOTES.md` and `RESEARCH-NOTES-ADDENDUM.md`, rewritten as
  instructions.
- **What goes wrong:** the `docs/teardown/` lessons as "never do this" rules.
- **What good looks like:** example layouts from our own types (a park, a parking lot, a market square), as plans
  with their pictures.
- **How to work:** look first, plan in words, build in layers (ground, then roads and paths, then big things, then
  small things), check after each layer, never finish with problems unexplained.

It is large and the same for every call, so prompt caching makes it cheap after the first one.

## Code to build on

Much of the data and many checks exist; the new work is mostly exposing them as tools in a form the AI reads well.
To check one by one when building:

- Terrain data and pictures: `Core/Build/TerrainDump.cs`, `TerrainMetrics.cs`; the runner's terrain map.
- Reshaping ground: `Core/Build/TerraformRaster.cs`, `TerraformDabs.cs`, `TerraformSurface.cs`, `TerraformPits.cs`.
- The plan and its picture: `Core/Build/SitePlan.cs`, `SitePlanView` in `UI/src/contract.ts`.
- Checks: `Core/Build/OwnOverlap.cs`, `SiteSafety.cs`, `BuildVerification.cs`, `BuildAudit.cs`, the pit check.
- Roads and the selection: `Core/Tool/RoadGraph.cs`, `AreaClip.cs`, the area tools (`docs/AREA-TOOLS.md`).
- Zoning and neighbours: `Core/Build/ZoneBlockPredictor.cs`, `ZoneGuard.cs`, `Core/Neighbours/WaterEdge.cs`.
- Assets: `Core/Build/AssetCuration.cs`, `AssetHeuristics.cs`, `Core/AssetCategories.cs`.
- Preview, build, undo: the Review stage and `Game/Build/`.

## The setting, the key, the panel

- **Experimental**, off by default, in the options: "AI builder (experimental)", a key field, and the model to use.
- The key is stored in the mod's settings on the player's machine and sent only to the AI provider.
- The panel: a category "AI" on the shelf (`Recipes/shelf/`), a "Describe what you want" box, a progress line while it
  works ("placing the path loop", "checking"), the preview with the AI's note, Build / Change / Cancel.
- Every new text in all 12 languages (`AGENTS.md`). The AI's own replies come in the player's language by itself.

## Logs and testing

- Every AI build writes a folder: the brief, every tool call and its answer, every picture, the AI's text, tokens and
  cost. The game runner and `Placemaker.log` point to it.
- A game runner step `ai-build` (prompt, selection, land) so it is tested like any type: `Show.ps1` with a prompt.
  Results differ run to run, so the runner shows whether what was built is sound, not whether it matches a reference.
- A set of standing prompts on standing lands (flat, slope, C shape, by the sea, through an existing road) to compare
  models and prompt versions.

## Costs and risks

- **Time and money:** dozens of tool calls and several pictures per build. Guess: a few minutes and roughly $1–5 a
  build; unmeasured. Measure in the first milestone.
- **Network from inside the game:** should work (C# HttpClient), not yet proven. First thing to prove.
- **Paradox Mods rules** on mods calling the internet and on API keys: unchecked. Matters before publishing, not for
  the user's own game.
- **Stutter:** tool calls run on the main thread in small slices; drawing maps and checks on workers where possible.
- **Huge selections:** the brief and the ground come in tiles; the AI may be asked to work part by part.
- **Bad results:** the checks catch the mechanical problems; taste is the prompt's and the examples' job, and the
  player always approves before anything is built.

## Milestones

1. **Proof of the call:** the setting and the key; the mod sends a site brief (text and one drawn map) of a selection
   and logs the reply. Proves the network, the brief's format and the cost of one call.
2. **Look and place:** the look, asset and place tools, ghosts only, the loop, `site.check`. A runner step with a
   prompt. Build a small park by prompt.
3. **Ground and roads:** the ground tools and `net.*`. Prompts that need a slope reshaped or a path network.
4. **Build and undo:** approval through the existing pipeline; the panel; the translations.
5. **Taste:** the example layouts, the teardown rules, the standing prompts; compare models.
