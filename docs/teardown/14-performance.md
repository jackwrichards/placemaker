# Teardown 14: why the design and build phases are slow, and how to get ~100× faster

The user (25 Oct): "I'm clicking parks and make a big area ... the spinner's at the bottom and it just takes forever."
Then: every type, which part is slow, "100 times faster", real-time editing of settings that costs the game nothing; and
the lag spike when the build happens ("Done placing").

Labels: **[P]** proven by a run (named below), **[C]** from code, **[H]** hypothesis with how to check it.
Nothing here was seen in the game runner yet; the runs are offline benches of the real Core and the runner's old logs.

## Where it stands (25 Oct, end of day)

Planning in the game's runtime (PlanBench.Mono, 12.5 ha, 400-corner outline, master `975212aa` → this branch, fastest of 2
alternated runs): parking 4713 → 421 ms (11×), park 1316 → 206 (6.4×), plaza 747 → 207 (3.6×), mall park 2726 → 931
(2.9×), car yard 1768 → 697, junk yard 1370 → 616, adventure playground 450 → 183, botanical garden 1712 → 977, mall district
2573 → 1819, formal garden 1294 → 937, community garden 1052 → 786, beach 569 → 420; the 12 together 20.3 → 8.2 s (2.5×).

Game runner (10 ha blocks, 25 Oct 15:05–15:16): five park styles, mall park and plaza build with the worst frame of the
mod's own work 16–35 ms (was 135–190 ms three times per park build), the preview ghost 7–21 ms (was up to 349 ms on a big
lot), every parking style with all its stall markings. Still the biggest costs: site harmony on plazas (450 ms of a 560 ms
plan), mall park's parking and zone guard (2–3 s), the game's own frames when the ghost is rebuilt (100–300 ms, §2.2 item 6).

## Tools made for this

- `Placemaker.Gallery --bench` (`Tests/Placemaker.Gallery/Bench.cs`): every type × preset at the given sizes, one plan at a
  time, on .NET. `bench.csv`: total, every Composer / layer step (PlanClock), and a **signature** of everything the plan
  builds, so a speed-up can be checked to build exactly the same thing. `--corners N` turns the site into a painted-style
  outline of N corners. Profile it with `dotnet-trace collect -- <gallery exe> --bench ...`.
- `Placemaker.PlanBench.Mono` (`Tests/Placemaker.PlanBench.Mono`): the same plans in **the game's own Mono runtime**
  (`tools/monohost`), with garbage-collection counts. This is the number the player feels.

```
dotnet run -c Release --project Tests/Placemaker.Gallery -- --bench --out .forensics/bench/plan/x --no-features --areas 1,5,12.5
dotnet build Tests/Placemaker.PlanBench.Mono -c Release -o .forensics/bench/planmono
tools/monohost/bin/monohost.exe .forensics/bench/planmono/Placemaker.PlanBench.Mono.exe <repo> --only park --areas 12.5 --corners 1200
```

## 1. The design phase (the spinner)

What happens on a click: the outline is built (main thread), the site is captured (main thread, ~18 ms), the whole recipe
is planned from scratch on one worker thread (`PreviewWorker`), assets are resolved (main thread, ~20 ms), then the
ghost preview is re-emitted (main thread, see §2.9). The spinner covers the worker part.

The user's own log, 25 Oct 11:51, a 12.5 ha painted park: **2412 ms on the worker** (park 1646, site harmony 385,
decals 148); parking right after: **3925 ms** and **3080 ms**. [P]

### 1.1 Four multipliers make a 12.5 ha park take 2.4 s

| Factor | Evidence | Size |
|---|---|---|
| **The painted outline has hundreds of corners** | Mono bench, park default 12.5 ha: 6 corners 806 ms, 300 corners 1111 ms, 1200 corners 2986 ms. Parking: 998 ms → 4332 ms → **40 s**. [P] A Paint area outline (`BlockBrush.Outline`, 4 m cells) of 15 ha has ~150 corners on open land and ~450 with 60 zone-cell strips around it (offline test, 25 Oct); they are real notches, so simplifying at 0.3–0.5 m removes only ~10 %. The fix is algorithms that do not scale with the corners (§1.5), not a coarser outline. [P] | 1.5–4× |
| **The game's Mono runtime** | same plans: .NET 327 ms vs Mono 848 ms (park default), 570 vs 2112 ms (garden). [P] | 2.5–4× |
| **Slow inner data structures** | profile below: a third of a park is dictionary lookups in the spacing grid. [P] | 2× (fixed, see 1.3) |
| **Everything is re-planned from scratch on every change** | `UpdatePlanning` → `Composer.Compose` of the whole recipe for any slider tick. [C] | all of it, every time |

With painted outlines parking is worst: it splits the outline into convex pieces (`PGeo.ConvexDecompose`, 39 %) and
shrinks it (`Inset.Eroded`, 27 %), both far worse than linear in the number of corners. [P, trace `parking1200`]

### 1.2 Where the time goes, all types (12.5 ha, .NET profile of every type × preset)

| Share | Step | What inside |
|---|---|---|
| 32 % | Shops planners (CommercialLayer) | strip mall plans its layout up to 3× and each lot up to 4× (`LotCentre.cs:111,823`), each a **full parking plan**; its green squares are **full park plans**; `DropBaysOffAsphalt` removes from a list one at a time (10 % of shops) |
| 8 % (+ inside shops) | Parks | 74 % tree/bush placement (`ParkPlanting.Vegetation`): spacing checks `TooClose` 39 %, path clearance 21 % |
| 7 % | Site harmony (ground grading) | point-in-polygon against the whole outline per sample (`SiteHarmony.Inside`) |
| 6 % | Decals / ground wear | the ground mask sampled per point (`GroundMask.Build`, `PolygonShape.At`) |
| 6 % | Industrial yards | free-space grids |
| 4 % | Zone guard | polygon booleans per blocker |
| 3 % | Parking | tree rows, layout, surface cells |

Worst designs at 12.5 ha on .NET (×2.5–5 in game, more with a painted outline): strip mall 778 ms, botanical garden
746, park greenway 669, mall park 586, mall district 537, car yard 407. [P, `.forensics/bench/plan/all`]
In the game's Mono on the plain 6-corner site: **strip mall ~4.0 s with ~40 garbage collections per plan**, park 0.4–2.1 s.
[P, `.forensics/bench/planmono/all.log`]

### 1.3 Already fixed on this branch (not seen in the game yet)

`Core/Layers/Common/SpatialIndex.cs`: the spacing/clearance grid used a `Dictionary<long, List<int>>`; now a flat
open-addressing cell table. Same results in the same order: **78 of 78 signatures identical** (park, parking, mall park,
strip mall, botanical garden, 5 and 12.5 ha). Park 12.5 ha .NET: default 346 → 160 ms, garden 608 → 293, greenway
362 → 169; botanical garden 821 → 505; mall park 570 → 410. [P, `base-park` vs `grid-park`]
In the game's Mono with a 1200-corner outline the gain is only 15–20 % (park default 3344 → 2836 ms, garden 4614 → 3701):
there the outline's corners dominate, so 1.4 item 1 comes first. [P, `planmono` vs `planmono-grid`, tests running alongside]

### 1.3b Parking planner (`Core/Layers/Parking`, not seen in the game yet)

Measured with `Placemaker.Gallery --bench` and `PlanBench.Mono` (base `d044c071`). Lots look the same and keep their
stalls (capacity identical in every gallery case: parking, mall district, odd shapes, 400-corner outlines); the surface
rings and the jittered fill plants differ (the rings are grouped from different cells).

* **Surface cells** (`SurfaceCells`): the zones come straight from the bands between the nested outlines (`Inset.Of`
  keeps edge i parallel to the lot's edge i, so lot → setback → ring road → grid is one quad per edge) and the innermost
  ring is cut by peeling its convex pockets (`Decompose`), not by ear clipping, which fanned slivers across the whole lot
  that every stall row then cut again: 13 800 cells → 5 000 on 12.5 ha with 400 corners. A setback thinner than 0.45 m
  joins the next zone (the short-edge clean-up folded it onto the outline). Merge on flat arrays (edge twins by sorted
  keys), the short-edge collapse only revisits the cells whose clusters changed. The old zone cutting stays as the
  fallback when a band does not tile (not hit in any bench case).
* **Surfaces the build keeps**: every emitted ring passes the game's area shape rules (`AreaRings`) or can be repaired
  by the build (`SurfaceRules.GameKeeps`); a ring the build would skip is regrouped from its cells. Refused rings in the
  gallery: 141 → 0 on the painted lots (21–38 per 12.5 ha lot before). (Emitting the repaired rings instead, so the build
  only copies them, was tried: the repair drops nodes on one ring and not on its neighbour, planned surfaces overlapped by
  up to 1 % — ParkingQualityTests.) Rings the game model trims by less than 0.5 m² are no longer regrouped (that won back
  < 0.1 m² a few times in 100).
* **Inside / distance tests by grid** (`RingGrid`: same answers as `PGeo`): insets (`Eroded` now finds the too-close part
  of each offset line analytically), the layout's `ConvexInside` / `StripInside`, planting probes (`SurfaceIndex` over the
  cells), lanterns, grading, `IsSimple`; `AreaRings.FirstViolation` by grid for rings over 24 nodes (also the build's).
* Results (fastest of 2, on a shared machine): .NET 12.5 ha with 400 corners 640–1 160 ms → 95–165 ms (7–10×), 1 ha
  280–315 → 20–23 ms; plain hexagon 12.5 ha 240–250 → 60–75 ms. Mono 12.5 ha with 400 corners 2.4–3.9 s → 0.32–0.44 s
  (parking step 3.8 s → 0.4 s), plain 12.5 ha 0.8 s → 0.2 s; garbage collections 11–22 → 3–9 per plan. Mall district,
  retail park, shops park, high street: same or a little faster.

### 1.3c Park planting

**Park layer, same designs** (branch `worktree-agent-a2ee16f4d84aac322`, 25 Oct; every change keeps the plan exactly: 778/778
bench signatures over every type × preset at 5 and 12.5 ha, 400 corners, and the 50 park / mall park / botanical garden
gallery pictures are byte-identical). [P] The park now logs its own steps: `park 284 [paths 35, lawns 26, clearings 32,
trees 59, bushes 123, regions 2]`.
- Planting (`ParkPlanting.Vegetation`, ~1 million candidates on 40 ha): a planting grid (`Park/PlantGrid.cs`) answers the
  fixed checks per cell (outline, gates, lake, clearing, paths, reserved polygons: Lipschitz bounds, the full check only in a
  band along each) and keeps per cell the nearest tree and bush, a sure spacing conflict for ~95 % of the candidates. Rows,
  avenues, the bosque and grid fills ask it too. 98–99 % of candidates are now answered by lookups; what is left is drawing
  them (in Mono a grove's Point, the belt noise; the noise lattice and the grove's rotation are now computed once).
- Shared, same answers: clearance searches stop at the first ring of cells that cannot hold a nearer segment or disc;
  segments and discs in plain float arrays (Mono runs `float2` operators as calls); `CrossesBoundary` tests only the edges
  along the segment; `LayerRegion.Contains` uses `RingBands` on boundary cells; a distance-to-outline raster answers
  `ContainsDisc` / `FarFromOutline` in free-space grids; `FreeSpace.Pieces` works each piece in its own box; spot search
  skips points whose clearance bound cannot beat the best; `DeepestPoint` skips points whose bound cannot; the park path
  roadmap keeps its weights beside its neighbours (was a dictionary).
- Mono (game runtime, 400-corner hexagon, fastest of 2×2 runs, A/B alternated): park layer, all presets, 34.6 → 6.1 s
  (**5.7×**), whole plans 42.9 → 11.9 s (3.6×). 40 ha: natural 1544 → 284 ms, garden 2636 → 415, woodland 2339 → 368,
  victorian 2898 → 359, landscape 2506 → 305; 12.5 ha: natural 871 → 170, garden 1437 → 244. Slowest gains: picnic 2.6–3.2×
  (its 7 picnic clearings: spot searches over open lawn), meadow / modern / formal at 40 ha 2.4–3.7× (little planting).
- Planting is still the largest park step on a big park (natural 40 ha: trees 59 + bushes 123 of 284 ms in Mono): the
  candidates themselves cost. Fewer candidates means a different sampler (the budget of 90 per plant is what makes the
  density): per-source "full after N misses in a row" loses 2–10 % of the plants for a 25–60 % cut; not done.

### 1.4 The road to 100× for the design phase

Ordered by gain per effort. Gains multiply.

1. **Plan on a simplified outline** (0.3–0.5 m tolerance instead of 8 cm; the exact outline stays for the build's own
   clipping). 3–10× on painted areas, 10–40× for parking on them. Cheap.
2. **Fix the hot inner loops** (each verified with signatures): the spacing grid (done), `SiteHarmony.Inside` and
   `GroundMask` with a pre-rasterised inside mask instead of point-in-polygon, `DropBaysOffAsphalt` in one pass,
   `ConvexDecompose`/`Inset` on the simplified ring, garbage-free hot paths (Mono's collector stops the game's threads too,
   `gc-max-time-slice=3` in the game's boot.config). 2–4×.
3. **Stop re-planning what did not change.** Cache each layer's result by its inputs (outline, settings, seed, reserved
   space); a slider for tree density re-runs planting only, not paths, lots or terrain. Most edits become 10–50× cheaper.
4. **Stop planning the same thing several times.** Strip mall: rank frames and lot rotations with the cheap estimate and
   plan the winner once; parking candidates share their geometry. 3–6× for shops.
5. **A sketch first, details after.** Publish paths, lots and regions as soon as they exist (tens of ms), then trees,
   decals and site harmony when done. The spinner becomes a fill-in.
6. **Burst for the numeric cores** (grids, rasters, terraform fields, decals): Mono runs Unity.Mathematics as method
   calls; Burst runs them as SIMD. 5–20× on those parts. Biggest effort; do last, where profiles still point.

Expected: a 12.5 ha painted park from ~2.4 s to ~50–100 ms for a full plan, and a few ms for most setting changes.

## 2. The build phase ("Done placing")

From the game runner's 774 runs (1 560 builds, each step's 3 worst frames) and the user's 16 ha parking build
(25 Oct 11:54). [P] No build step logs its own time today; the frames were matched by the gaps between log lines.

- At runner size (1–1.6 ha) every park, parking and shop build has **three 135–190 ms frames**: the three live pit checks.
  Parking adds a ~300 ms terraform setup frame. Roads skip the pit checks: worst frame 47 ms.
- The user's 16 ha parking: **7.8 of 10.2 s are in 4 single frames: 2.7 s, 1.6 s, 1.66 s, 1.86 s.**

### 2.1 Causes, by cost

1. **The live pit check, 3× per build** (`BuildService.Pits.cs:39`, from `Emit.cs:131`, `BuildService.cs:546,605`):
   a 2 m grid × 49 height samples over the whole outline. **Diagnostic only**: its result is used nowhere. [C]
   ~150 ms ×3 at runner size, ~1.6 s ×3 at 16 ha.
2. **Terraform setup in one frame** (`BuildService.Terrain.cs:114`): raster + pit fill, pure Core on the main thread.
   ~300 ms (parking), 2.7 s (16 ha).
3. **Shops' zoning refresh frame** (`BuildService.ZoneBlock.cs:98`): `ZoneGuard.Refresh` up to ~50 full computes, plus the
   diagnostic `LogBlockedPlanCells`. 1.2–8.4 s for big strip malls and mall districts (runs of 16–18 Oct; not re-run since).
4. **The Finish frame**: the pit check again, `VerifyLiveHollows` (diagnostic, 0.3 s at 16 ha), the audit, index rebuild.
5. **Surface ring repair on every emission** (ghost and build): ~0.5 s for 740 rings, and 15 identical log lines each time.
6. **Per-entity tagging and verdict clean-up** one entity at a time: ~250 ms at 8 000 pieces.
7. **Whole-world scans** (every tree, prop, edge, building in the city) several times per build: cheap on the runner's empty
   map, the main risk in a big city. [H: measure in a saved big city]
8. **The game's own work** (temporaries, validation, Apply): ~380 ms over ~4 frames at 8 000 pieces. Only spreadable.
9. **While designing**: every new plan clears and re-emits the whole ghost (`BuildService.Ghost.cs:228`).

### 2.2 The road to a stutter-free build

1. **Cut the diagnostics or put them behind an off-by-default debug setting**: the 3 pit checks, `VerifyLiveHollows`,
   `LogSaggingEdges` ×5, `LogBlockedPlanCells`, `AuditZonePlots`, the repeated ring log lines. Removes every >100 ms frame
   of a normal park build. Cheap.
2. **Terraform raster, pit fill and `ZoneGuard.Refresh` on a worker** (the paint step already works this way). 2.7 s → ~0.
3. **Prepare surface rings in the planner** (worker), so emission only copies them.
4. **Create entities in bulk / Burst jobs** for definitions, tagging and verdicts; smaller batches per frame.
5. **World scans through the game's search trees**, bounded by the outline.
6. **Keep the ghost**: build from the preview's temporaries for every type (today only when no terraform/paint), and update
   the ghost by difference instead of clearing it on each plan.
7. **Apply in chunks** (nets first, objects ~500 per transaction) so the game's own frames stay ~15 ms.

Expected: a runner park's worst frame ~170 ms → ~20–40 ms after 1–2; the 16 ha parking ~2.7 s → under ~50 ms per frame
after all of it.

### 2.3 Done on this branch (25 Oct), seen in the game runner

Runner: `Show.ps1 -Type park|parking -Size 320` (a 10 ha block, ring road). "Ours" = the mod's own main-thread time per frame
(the new `Placemaker build timing` / `ghost timing` / `slow tool frame` lines); the game's own work comes on top.

| | before (same day) | after |
|---|---|---|
| park build, worst frame of ours | 3 × 135–190 ms (pit checks) | **52 ms** (`show-park-default-1025-133213`) |
| park build, companion site search | 183 ms | 25 ms |
| parking build, census + tagging (5 000 pieces) | 89 + 112 ms | **40 + 51 ms** (`show-parking-default-1025-134722`), 2 376/2 376 stalls |
| parking preview (ghost), worst frame | 349 ms | **32–40 ms** |
| assets on every plan, main thread | 30–60 ms | 0 (on the planning worker) |
| terraform target + pit guard | 0.3–2.7 s in one frame | on a worker (5 ms at 10 ha) |
| shops zoning refresh | 1–5 s in one frame (big shops) | on a worker (20 ms here) |

What changed: `Debug: build checks` (off) gates the log-only checks (pit scans, sagging edges, hollows, zoning verify, blocked
cells, plot roads); the terraform raster/pit fill and ZoneGuard.Refresh run on workers from a copy of the height map (the
game's own sampler, so the same heights); surface/area rings are repaired once per ring on a worker before the ghost emits
(cached for the build); the census and tagging read through component lookups, batch their structural changes, and match in
an index sized to its tolerance; the park companion search indexes the park's objects.

Comparing signatures: **pin the catalog** (`--dump <a fixed copy of catalog-dump.json>`). The gallery reads the player's
`ModsData/Placemaker/catalog-dump.json`, which every game run rewrites; the yard, ruin and memorial types pick assets from
it, so their signatures changed between benches although no code had (first taken for a carry-over between plans; with a
pinned dump the full catalogue and the single-type runs agree, 13/13, 25 Oct).

## 3. Measurement to add (so the game runner can judge each step)

- Done: one timing line per build (steps, worst frames, parts), per ghost, and a line for any tool frame over 30 ms.
- The outline's corner count in every plan line (`Placemaker: plan park r77: ... 1180 corners`).
- Re-run big shops (`stripMall-classic-big`, `mallDistrict-dense-big`) and one build in a saved big city.
