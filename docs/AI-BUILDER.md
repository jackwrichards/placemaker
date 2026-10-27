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
| `assets.card(id)` | everything needed to place it exactly (below, "Knowing every asset exactly") |
| `assets.look(id, views)` | real pictures of it from several sides, its axes drawn on |

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

## Knowing every asset exactly

The AI works only with the assets the player actually has (vanilla, DLC, every mod loaded), read from the game's
prefabs at the time, never from a fixed list. For each it must know enough to put it in the right spot facing the
right way:

- **Real pictures:** the game's thumbnail, plus `assets.look`: the hidden camera on one copy placed off to the side,
  from the front, the side, above and at 45°, with the **origin (zero point)** marked, the **front arrow** and the
  **bounding box** drawn on. Cached per asset, so each is drawn once per game.
- **The zero point and the box:** where the origin sits relative to the mesh (`ObjectGeometryData.m_Bounds`, min and
  max from the origin, `RESEARCH-NOTES-ADDENDUM.md` #47), so "put the bench's back against the hedge" is arithmetic,
  not guesswork.
- **Which way is front:** the local axis the asset faces (and, for buildings, the side that must touch a road).
- **How it sits:** on the ground, on water, on a wall, hanging (`PlacementFlags`); whether it follows the slope or
  stays upright; whether it needs a road, a lot, a zone.
- **What can change per copy:** position, rotation, height, colour (`CustomMeshColor`), the random seed (colour
  variants, props), one stretch axis (`Stack`), tree age. **Not size**: there is no per-copy scale
  (`docs/research/custom-assets-on-the-fly.md`).
- **What comes with it:** sub-objects and props a building brings along, and its lot.
- **Description:** name, the asset's own description and tags, the mod it comes from, and our own classification
  (`AssetHeuristics`, `AssetCategories`).

All of this is read on demand and cached; nothing is sent until the AI asks.

## Building things out of assets

With exact boxes and zero points, the AI can assemble new things from existing pieces: a house out of walls, roofs and
fences, a stage out of crates and lights, a pier out of planks.

| Tool | Does |
|---|---|
| `object.snap(id, to, how)` | put one object against another: face to face, on top (the box's height), centred, at an edge |
| `object.measure(a, b)` | distance, gap or overlap between two objects' boxes |
| `object.height(id, h)` | lift above the ground (stacking, roofs), where the game allows it |
| `piece.make(name, ids)` | save a group of placed objects as a piece, with its own zero point |
| `piece.place(name, x, z, rotation)` | place copies of a piece, rotated and moved as one |
| `piece.list()` | the pieces made so far in this build |

Open question: objects above the ground. Most objects are snapped to the terrain; mods like Anarchy keep them up. How
far we can go (and whether it survives a save and load) is to be tried in the game before the AI relies on it.

## Code: the AI writes its own generators

Our own types are not hand-placed: they use rules, randomness and noise (scatters, spacing, curved paths, clumps,
edges). The AI gets the same, two ways:

1. **Our generators as tools:** `gen.scatter` with noise and minimum spacing (the parks' tree and bush clumps),
   `gen.poisson` (even but natural spacing), `gen.noise(region, scale)` (a noise field to drive density, height or
   colour), `gen.curve(points, smooth)` (smooth paths), `gen.offset(shape, distance)` (edges, rings), `gen.split(shape)`
   (beds, plots, a grid or Voronoi cells), `gen.along(curve, spacing, jitter)`. Seeded, so the same call gives the same
   result.
2. **Small scripts:** `code.run(script)`: the AI writes a short program (JavaScript, run by an embedded interpreter in
   a sandbox: no files, no network, a time and memory limit) that calls the same functions, loops and does its own
   maths, and returns placements. Everything a script produces goes through the same placing tools and the same checks
   as a single object: it is a faster way to place, not a way around the rules. Which interpreter (e.g. Jint, BSD
   licence, pure .NET) and whether it loads inside the game: to try.

Generators come first (phase two); scripts once the generators show what is missing.

**Superseded (the user, 26 Oct, after the first rounds):** don't box the AI in. Every object is placed by the AI
itself; tools are the minimum it needs to be as creative as possible. No generators that decide placement (a scatter
tool was tried for one round and taken out). Most improvements are prompt changes (`Core/Ai/AiPrompts.cs`); a tool is
added or changed only when the AI is literally blocked (it cannot do or cannot know something). Information tools
(asset sizes, the map, the problems a placement caused) are fine. Paths: open; for now `place_path` takes the AI's
points as given, with an optional smooth curve through them. Scripts (item 2) stay possible later, as the AI's own code.

## Getting better: tools the AI asks for

The tools are not fixed. They grow from what the AI needed and did not have:

- **`feedback.wish(text)`:** the AI notes what tool or information it was missing ("I needed the height of the
  roof's ridge", "a tool to mirror a piece"). Saved with the build's log.
- **We use it ourselves:** in the game runner, Claude Code (an LLM too) runs the same loop with the same tools on the
  standing prompts, notes where it struggled, and we add or change tools here. Then the in-game AI gets them.
- **Each tool change is checked** against the standing prompts: same lands, same prompts, before and after.

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

- **Experimental**, off by default, in the options: "AI builder (experimental)", the key, the model, the address. Only
  those: nothing about what to build lives in the options.
- The key is stored in the mod's settings on the player's machine and sent only to the AI provider.
- **The panel (the user, 26 Oct):** with the setting on, the Make row has one more category, **AI**. It has no types and
  no styles: picking it shows a text box ("What do you want here?") and the chat below it. The back and forth happens
  there (the AI's progress lines, its questions, the player's "fewer trees").
- **Design** works like any other type's design stage: the mini map (the site plan sketch) shows what the AI has placed
  so far, redrawn as it works. **Review** and **Build** are exactly the other types' stages: the ghost preview, Build,
  undo.

## Who refines it

Claude Code does, in the game runner, round after round: run the AI category on the standing prompts and lands, read
its log, look at the pictures, **rate each result itself** (layout, fit to the ground and the site, what was asked),
smooth out the AI's hiccups (prompt, brief, tool answers) and add the tools it was missing (its `feedback.wish`
notes and Claude Code's own). This needs a key in the runner's games: `%USERPROFILE%\.placemaker\ai-key.txt`,
put there by the user (an agent never types a key).
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

## Phases

### Phase one: the loop begins

The basics, end to end, however rough. Steps:

1. **The call:** the experimental setting and the key; the mod sends a site brief (text and one drawn map) of a
   selection and logs the reply, the tokens and the cost. Proves the network from inside the game and the brief's
   format.
2. **The loop:** the agent loop with tool calls (off the main thread, tools run between frames), a few tools
   (`site.brief`, `site.map`, `assets.search`, `assets.card`, `object.place`, `object.remove`, `site.check`,
   `plan.done`, `feedback.wish`), ghosts only, every step logged.
3. **A runner step** `ai-build` with a prompt, so we can run it here and watch it, and Claude Code can try the same
   tools and tell what is missing.
4. **First result:** a small park by prompt, previewed in the game.

### Phase two: building well

- Ground tools, `net.*` (paths, roads, fences), the many-at-once tools, the generators.
- `assets.look` (the hidden camera), snapping and pieces; objects above the ground once tried.
- Approval through the existing build pipeline and undo; the panel; the translations.

### Phase three: taste and reach

- Scripts (`code.run`), the "real look" render.
- The big system prompt grown from the teardown rules and example layouts; the standing prompts; compare models.
- The tool wishes, read and turned into tools, round after round.
