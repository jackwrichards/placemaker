# Ground runbook: parks (the Parks category makeover, 2026-10-29)

The Parks category (`Recipes/shelf/parks.json`): park, botanical garden, memorial park, adventure playground, zen garden.
Brief: `docs/refactor/MAKEOVER-BRIEF.md`. The user: "Regular parks are going to need a massive makeover ... rock sections,
barrancas, all that ... 10 times better ... an artistic masterpiece touch"; "consistency across multiple seeds, but
inconsistency across a single park" (in vibes: "it should just be dynamically whatever it should be").

## Status

- Grade from the user: not yet (before: park styles B to B+, memorial A-, adventure A-, botanical and zen "okay")
- Branch: `makeover-parks` (see git log)
- In the user's game: no

## The idea: one park, many places (ParkMoods)

A park is not one texture. After its paths and lawns are known, a park picks a few places in its lawns and gives each a
mood read from the land (`Core/Layers/Park/ParkMoods.cs`):

| Mood | Ground (Sculpt, permanent) | What stands there (vanilla only) | Paint | Decals (ParkGround) |
|---|---|---|---|---|
| gully (barranca) | a winding channel 1.1-2.4 m deep along the fall of the land, banks ≤ ~45 %, floor falling downhill, ragged | half-buried, tilted `Boulder01-08` in clumps along both banks, cobbles (`Rock01-30`) on the floor, a fallen `Log` across, alders, birches and scrub on the rim, sometimes a dead tree | dry banks, dirt floor | `gullyGravel` floor, `runoffRill` down the middle and down the banks, leaves caught in it, `screeFan` at the foot of the banks, moss on the shady bank, rock round the boulders |
| outcrop (rocky knoll) | a dome 1.1-2.4 m, lumpy | a crown of big boulders (one tilted against the others), stones breaking the turf, scree downhill, a pine or birch, flowering scrub between stones | dry, dirt core | `screeFan` downhill, `rockOutcrop`/`rocks` round boulders, moss north side, dry grass |
| hollow (wet) | a dip 0.6-1.15 m | a crescent of alders, poplars, birches; low green scrub as rushes; a half-sunk log, stones | lush, dirt heart | `wetSilt`, rain puddles, damp, clover on the rim, wet leaves |
| meadow | none | 1-3 old solitary trees, drifts of wild flowering shrubs | dry grass | `meadowFlowers`, long dry grass, molehills |
| glade (copse) | none | a ring of mixed trees (thicker on one side) round a clearing, an old oak or chestnut in it, logs where they fell, scrub | dirt (forest floor) | woodland floor, twigs, moss, floor round the logs |
| stones (rural) | a low rise | a ring of `StandingStone01-08`, a gap where one fell | dry | a worn ring path, long grass, rock |

Places come from the land: the lowest ground holds the hollow, a slope the gully (along its fall), high ground the
outcrop. A style's palette weights them (`ParkMoods.Palette`); a mood repeats only when every mood of the palette is used.
The first place is the hero (22-28 % of the lawn's side, up to 38 m), the others 12-18 %. One `Sculpt` over all of them
(free only inside the places and 2.5 m clear of every path), `LimitGrade(0.42)`, emitted once. The site harmony pass
protects the places from its smoothing (`SiteHarmony`: regions tagged `mood:*`).

## Styles (park)

| Style | Character | Moods palette (gully, outcrop, hollow, meadow, glade, stones) |
|---|---|---|
| natural | the park of the city: curvy walks, groves | 1.2, 1.1, 1.0, 1.0, 1.0, 0.15 |
| garden (Garden walk) | flowering shrub borders, now painted lawn (was a plastic grass surface) | 0.5, 1.4, 0.8, 1.2, 0.8, 0 |
| urban (Urban pocket) | plaza, street trees, kiosk | 0, 1.5, 0.2, 0.6, 0.9, 0 |
| picnic | a big open lawn ringed by shade trees | 0.4, 0.9, 0.6, 1.3, 1.4, 0 |
| wild (Country park, new) | native groves, long grass, painted dirt trails, rustic benches, a fence, a car park, no lamps | 1.4, 1.0, 1.0, 1.4, 1.0, 0.7 |
| neglected (new; its own version, not a slider) | thickets and old trees close in, dead trees, dead and elderly bushes, cracked mossy paths, litter, molehills | 0.8, 0.5, 1.2, 1.2, 1.2, 0 |

Options (all optional): places with character (`moods`), shape the ground (`moodGround`), car park (`carPark`, a small lot
on the street side by the main entrance, `ParkCarPark.cs`), fence, dog run and every amenity (already there), picnic.

## The ground design (ParkGround)

`Core/Decals/Dynamic/ParkGround.cs`, registered in `Weathering.Designs` for every Parks type. The shared causes of
`Weathering.Scene` run first (tone, leaves, drifts, path-edge wear, gates, shortcuts, benches, bins, puddles); then:
moods, `pathGrit` along every path's lip, runoff (rills traced downhill over the built ground from path edges until it
flattens, a silt fan where they stop; not on formal types), play scuffs under equipment and round picnic tables, and the
neglect pass (litter, molehills, dead leaves, cracks and moss on paths).

Leaves: the sports park's summer carpets read as mustard yellow under green birches in the game; parks lay
`leafLitterSummer` (dark olive-brown leaves and twigs, sparse) at 42 % cover (`Scene.LeafScale`, `Scene.LeafOverride`).

## New library kinds (`Recipes/decals/library/parks.json`)

`runoffRill` (strip), `gullyGravel`, `screeFan`, `meadowFlowers` (pale: saturated yellow read as orange rust), `wetSilt`,
`leafLitterSummer`, `rakedGravel` (strip, the dry garden's rows), `rakedRing` (short strip, rings round the stones),
`pathGrit` (strip).

## Other types

- Botanical garden: places too (a rock garden on a knoll, a wildflower meadow, a bog garden in a hollow, a glade), painted lawn.
- Memorial park: places on the side lawns before the groves, never on the vista along the axis (stones, meadow, copse,
  knoll); painted lawn; one control only (`moods`; its schema test allows 41 controls).
- Adventure playground: wild places between the play zones (a knoll to scramble, a ravine, a copse with logs); lawns that
  hold a zone or the hub stay open.
- Zen garden: the dry garden's gravel is raked (rows along the field, two rings round each stone group). Tea garden hidden.

## Palette lessons

- `FlowerBushWild01` and `FlowerBushWildElderly01` are pale teal in the game (bush test run `20261030-003549-bushes`): out of
  every parks palette (`ParkArt`, botanical, memorial, moods). `FlowerBushWild02` / `Child02` are dark and quiet. (Other
  categories still use them: parking, beach, golf, lookout hill, hiking, waterfront, animal park.)
- Summer leaves: dark olive-brown litter, sparse.

## Shared code touched (small, additive)

- `Weathering.cs`: `Designs` (per-type designs; `Wants` includes them), `Scene.Design/Host/LeafScale/LeafOverride` and
  read accessors; `Run` calls the design after the shared causes; a park without goals never picks the bald goalmouth; a
  type with a design has its own budget a hectare (`ParkGround.PerHectare` 700; the sports park unchanged at 320).
- `SiteHarmony.cs`: mood regions keep the Terrain feature's decor out and are protected from the change smoothing (softly,
  over 4 m inside their rim).
- `Lawn.cs`: botanical garden and memorial park default to painted terrain; Garden walk no longer forces a grass surface.
- `Game/Build/TerrainCullGuard.cs` + one line in `BuildService.Emit.cs` (the crash, below).
- Tests that assume a park plans no terraform switch `moods` off (ParkTests, FeatureTests, SiteHarmonyTests, the adventure
  jungle check); lawn-default tests follow `Lawn.cs`; GroundWear's bare-lawn test checks the decal ground.

## Runs

| Run id | What it shows |
|---|---|
| `show-22-builds-1029-232925-79c4` | BEFORE: every visible Parks style, 45 + close |
| `show-6-builds-1029-235043-0788` | first moods: places too small, boulders buried out of sight, terrain smoothed by site harmony, yellow leaf carpets |
| `show-5-builds-1030-000121-ff10` | discs in the open: 2-3 tiny places per park, invisible from 45°; leaves fixed |
| `show-24-builds-1030-001417-7e24` | places as whole lawns: the rooms differ; big lawns spread stones thin (memorial, shrubbery); teal bushes |
| `20261030-001417-bushes` | the CullForCascades crash at the 4th build (no terraform in that run) |
| `20261030-003549-bushes` | with the guard: 10 of 10 built; the road cull list was at 3988 sections in a capacity of 4096 |

## What failed and why

- Boulders vanished: the model already sits 1.35 m in the ground; sinking it by half its height on top buried it. Sink
  0.35-0.55 of 1.6 m × scale; broken stones at its foot so it never reads as a dropped prop.
- The knoll came out at half its height: SiteHarmony smoothed the change field (20 passes). Mood regions are protected now.
- Discs of 8-38 m rarely fitted between the paths: places are lawns between the paths; knolls, hollows and stone rings take
  a piece (inscribed radius 15 / 14 / 10 m) of a big lawn.
- Banks at 42 % read as curtains to the harmony checks on a hill: the change is limited to 28 % and the result to 34 % over
  the ground as it is, 5 m off the outline.
- Mood facts doubled (a layer can be planned twice on one plan): facts are set from local counts.

## The CullForCascades crash (AGENTS.md "known bugs")

Met again in run `20261030-001417-bushes` (a fresh game, the 4th build, tiny parks with no terraform at all): a native
crash freeing memory in `NativeList<LaneSection>.set_Capacity` inside `TerrainSystem.CullForCascades`.

Why (decompiled `Game.Simulation.TerrainSystem`): the road cull list is sized 6 sections per road-group entity
(`m_RoadsGroup` count × 6 + max(n/4, 250)) and filled by `CullRoadsJob` with `AddNoResize`, which does not check the
capacity in a release build. One edge can write up to 18 sections (`AddSegment` writes 1-3 per segment; 2 segments for the
edge and up to 2 for each end node; the extra ones for terrain-shifting nets with offsets). Builds of many short paths push
the list past its capacity; the job writes over the next heap block; the crash surfaces at the next resize. The 250
margin explains "the 4th build".

Workaround (`Game/Build/TerrainCullGuard.cs`, called at the start of `EmitNets`, build and ghost): complete every job that
touches the list (`m_CullFinished`, `m_LaneCull`, `m_ClipMapCull`, each cascade's `m_LaneHandle`), then make its capacity
at least 2 × its current length + 18 × the nets we add + 2000. The vanilla rule still grows it; we only keep it from
overflowing. It logs when it grows the list and warns if it ever finds the list already longer than its capacity.

## Frame time, memory, planning

- `send-ground-parks-1030-004853` (natural park, 220 x 200, 1330 library instances, 207 variants, 47.2 MiB on the GPU):
  median frame 6.80 ms before the build, 8.08 ms with the park and its ground, 8.04 ms with the ground switched off:
  the ground costs about 0.04 ms (the rest is the park's trees and props).
- Planning (`ParkGroundTests.TinyAndHugeParksStayQuick`, natural, rolling ground): 24 x 24 m 0.2 s; 600 x 600 m 1.5-1.6 s
  compose with 4660 instances at 83 % density, 8 places, weathering 0.7 s (most of it the shared trees pass).
- A city block: 600-1000 instances (budget 700 a hectare).

## Final runs (branch head)

| Run id | What it shows |
|---|---|
| `show-23-builds-1030-012225-3eac` | every shown Parks style on a city block, 45 / close / low / terrain: 23 of 23 built |
| `show-4-builds-1030-012225-9daf` | park natural, wild, picnic, urban on 220 x 200 hills: 4 of 4 built |
| `show-4-builds-1030-004839-bb67` | park styles on a 6 % slope, 220 x 200 (knolls and gullies read in the terrain maps) |
| `send-ground-parks-1030-004853` | frame time (above) |

GPU: the `parks` family is 47 variants, 9.7 MiB (whole shared library 178.1 MiB in this branch; `DecalLibraryTests`
now prints MiB per family).

## Left to do

- The user has not graded it. From the pictures: the places read clearly in the close and low views and in the rooms of
  a park with many paths; from 45° over a whole city block they are still quieter than the trees (the knolls rise 1-2.5 m,
  the gullies cut 1-2 m; deeper would need steeper banks than the harmony checks allow).
- Community garden is still its own hidden type (the user said it "can" come into parks): an amenity would need a park
  place for the allotments' layer; not done.
- `SiteHarmonyTests` park on a bumpy hill with the Terrain feature: 0.84 m step (limit 0.8) at a picnic shelter's pad
  whose spot moved because the places took lawns; the places do not terraform with the Terrain feature on.
- Last commits after `show-23-builds-1030-012225-3eac` (memorial groves before places, creamier meadow flowers, leaner
  decal family, botanical bed edging, massed boulder crowns) passed the tests but were not seen in the game.
