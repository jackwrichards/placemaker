# Ground runbook: Industrial (container yard, car yard, junk yard, industrial works, landfill)

The industrial category's makeover (2026-10-29/30, docs/refactor/MAKEOVER-BRIEF.md). The user: "For the industrial, that could
go hard on the decals, because grunginess is sublime for that type of thing", then, correcting: "decals shouldn't do most of
the work. They should complement the current work and lean into what makes the current work good." So the yards keep their
props, heaps, rocks, slabs and painted earth (docs/refactor/industrial.md), and the ground adds only the marks a yard worker
would recognise.

## Status

- Grade from the user: not seen yet
- Branch / last commit: `makeover-industrial` (see git log)
- In the user's game: no

## The shelf (Recipes/shelf/industry.json)

Seen in the clean game first (run `show-42-builds-1029-232950-eebd`, every style on 220 x 200):

| Type | Shown | Hidden, and why |
|---|---|---|
| Container yard | terminal, depot, storage | - |
| Car yard | salvage, crusher, breakers | - |
| Junk yard | sorted | reclaim (sparse goods on bare slabs), tip (rows of identical skips: the "grid" the user gave an F) |
| Industrial works | salvage, works | recycling (the same rows of skips) |
| Landfill | active | capped (a lawn), compost (bare stripes), recycling (a white sheet of boxes), flytip (a wood) |
| Logistics yard | - | the whole type, still after round 5 (run `send-ground-industrial-all-1030-021016`): even with our own lorries and loaders and grey surfaces, its lattice spreads single small props over hectares under the game's bright hatched bay markings. It needs a new layout, not a dressing; the container yard does stacked goods better |
| Scrapyard (the old one) | - | the whole type: empty dirt in the clean game; the car and junk yards replaced it |
| Quarry | openPit, abandoned | gravelPit, sandPit (flat empty floors: a lorry on a sheet of sand), lakeDig, flooded (no water surface in the clean game). Rebuilt on vanilla pieces in round 5: Placemaker's loaders, lorries and mining tractor on the benches and ramp (the pack names were refused), the ore plant and conveyor, rubble heaps with boulders on the working floor, the game's stones and boulders as scree, the pit painted worked earth instead of grass, dust only inside the pit |

## Round 5 (the lead: re-run, slabs, rust, quarry and logistics, IsOwn)

- Container slabs: worn grey pavement instead of bright concrete, grime along every slab edge, oil drips, a darker tone.
- Rust: a grainy soaked stain (eroded streaks over a faint brown soak), dull brown-orange; before, it read as paint.
- `AutoAssets.IsOwn` lets in only `VehicleProps.Prefix` ("Placemaker Vehicle "); `VehiclePropSystem` makes those from
  built-in vehicles only (`p.isBuiltin`). Our other runtime copies ("Placemaker Overlay (", decal copies) can come from
  asset packs and stay out (test `OnlyOurVehiclePropsCountAsVanilla`).
- The logistics yard and the quarry use our vehicle props too (their pack sets were refused).
- A hard cap after the three thinner runs (strips thin slowly; a big hard yard could stay a few percent over).

Construction site and abandoned lot belong to the leftover & rundown agent (left alone here).

## A bug found on the way: the car yards had no cars

`AutoAssets` (the vanilla-only rule, e772fcdc) refused Placemaker's own vehicle props ("Placemaker Vehicle Car 1" …, made by
`VehiclePropSystem` from vanilla vehicles), because they are not built-in prefabs. Every wreck, lorry, loader and tractor took
its fallback: trailer loads of cardboard boxes, cable reels and generators. The car yards in the clean game were fields of boxes.
`AutoAssets.IsOwn`: "Placemaker ..." prefabs count as vanilla.

## The marks of work (Core/Layers/Types/Industrial/YardWear.cs)

Laid by the yard layer itself at the end of its plan (it knows its lanes, gates, ground and style; the plan gives the
objects, fences and slabs). Every type that builds a yard calls it: the four block/organic yards (`IndustrialYardLayer`), the
landfill, and the hidden logistics yard, scrapyard and quarry (so they are not worse if built by id).

| Cause | Where it is read | What it lays |
|---|---|---|
| Wheels | the yard's lanes (aisles, organic tracks, haul roads noted by `YardKit.Haul`) | `yardRut` twin ruts on dirt and gravel, `laneGrime` (rubber and oil band) on concrete and asphalt; turning arcs where lanes cross; water standing in the ruts at the crossings (`puddlePiece` + `dampPatch`); grit at dirt crossings |
| Gate | the gates | swing-in arcs both ways, mud carried out onto the apron (`tyreTrack`), drips at the barrier; the big puddle (flaw) |
| Machines | lorries, loaders, tractors, forklifts (vehicle props and machine categories) | `oilSpot` at the engine end, sometimes drips; tanks and generators soaked at the foot (fuel point); scrap grit behind loaders |
| Wrecks | ground-level wrecks (one per 6 m) | oil (22 %) or rust (14 %) |
| Steel | containers, skips (one per 5 m), harbour stacks (along both long sides), the metal mountain (all round), masts | `rustRunoff` running away from the steel; `rustBleed` at mast feet |
| Heaps | stone, sand, rubble heaps (one per 10 m), the plant | `dustDrift` downwind (wind from the south-west), `pebbles` spill at the foot |
| Scrap | scrap and metal heaps (one per 8-9 m) | `scrapGrit`, now and then rust |
| Rubbish | garbage heaps | `litterMix` downwind, `leachateSeep` |
| Timber | log piles | `barkMulch`, `twigs` |
| Fences | fence nets (or the yard's edge) | `weedStrip` on the inside, heavy against the fence |
| Slabs | paving and asphalt surfaces that are not lanes | `crackConcrete` a step in from the edge, `crackWeeds` at the back of the yard, a little `crackWeb` |
| Markings | hard lanes of hard yards | `fadedLineYellow` along both lane edges |
| Seeps | the live tip's toe (landfill) | `leachateSeep` |
| Flaws (1-2) | picked per yard | big puddle at the gate (dirt yards), oil lake at the fuel point, rust river from the metal mountain to the nearest lane, the corner gone wild (farthest from the gate) |

Budget: 260 instances a hectare, at most 7000; a huge yard is thinned evenly and re-run thinner if it still came out over.
"One mark per heap" (`Room`): a heap of 40 props is one heap, not 40 stains.

## New library kinds (Recipes/decals/library/industrial.json)

| Kind | What |
|---|---|
| `rustRunoff` | orange-brown streaks washed off steel, strongest at the foot |
| `dustDrift` | pale dust and fine grit fanned downwind |
| `yardRut` | one wheel rut in a dirt yard: dark compacted soil, damp bottom, grit pushed up |
| `laneGrime` | the dark band of rubber and oil along a hard lane's wheel path |
| `weedStrip` | weeds and dry grass at the foot of a fence (+z = the fence) |
| `crackWeeds` | a slab crack with weeds growing out of it (chain) |
| `fadedLineYellow`, `fadedLineWhite` | old painted lines worn to a broken trace |
| `leachateSeep` | dark wet seep with an iron stain |
| `scrapGrit` | rusty flakes, grey fragments and dark specks at the foot of scrap |

## Rules the pictures taught

- **The offline bake lies about strength.** Kinds that read well in `PM_BAKE_LIBRARY` pictures were invisible in the game at
  the same numbers (run `send-ground-industrial-all-1030-000349`: ruts, tyre tracks, oil and rust could not be seen from 24 m).
  The game's light and the busy ground of a yard eat a 20 % alpha. Judge strength only in the game.
- **`Show.ps1` pictures can come before the library textures are made** (first use of a kind takes seconds of worker time); a
  scenario with `{"do":"lab","op":"grime"}` waits for them.
- **Cracks under containers are wasted**: a slab's inset ring lies under the rows that fill it.

## Runs that prove it

| Run id | What it shows |
|---|---|
| `show-42-builds-1029-232950-eebd` | BEFORE: every style of the category on 220 x 200, 45 and close |
| `send-ground-industrial-all-1030-000349` | round 1 of the marks: present (10 groups, 8312 instances) but too faint to see; the car yards full of boxes |
| `send-ground-industrial-some-1030-004831` | the car prop fix: the salvage yard full of cars, stacked and on their roofs |
| `send-ground-industrial-all-1030-011341` | round 3, every survivor on 220 x 200: 45, mid and close looks by kind (rust still too orange, dust white on asphalt: fixed in round 4) |
| `send-ground-industrial-all-150x140-slope-1030-015012` | round 4, every survivor on 150 x 140 on a 5 % slope: 10 of 10 built |
| `send-ground-industrial-1030-013345` | frame time, car yard crusher 220 x 200 (816 instances): 8.24 ms with the marks, 8.35 ms switched off (no cost) |

## Numbers

- Instances: 470-760 per organic car yard, about 1100-1200 per block yard on 220 x 200 (budget 260/ha); 45 x 40 m: 71; 600 x 560 m: 4300-6900 thinned.
- Planning: 6-100 ms for the marks (a 600 x 560 m terminal 600 ms).
- Library family: 10 kinds, 46 variants, 8.0 MiB on the GPU (trimmed from 14.0 at the lead's request).

## Left to do

- Not shown to the user yet.
- Container yard slabs are still bright white concrete from 45 degrees (the vanilla surface); the slab tone helps a little.
- The hidden logistics yard, scrapyard and quarry get the marks too but were not reworked: they stay hidden.
- Tiny rust stains on hard ground may still read as paint close up; judge with the user.
