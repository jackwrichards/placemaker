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

`runoffRill` (strip), `gullyGravel`, `screeFan`, `meadowFlowers`, `wetSilt`, `leafLitterSummer`, `pathGrit` (strip).

## Shared code touched (small, additive)

- `Weathering.cs`: `Designs` (per-type designs; `Wants` includes them), `Scene.Design/Host/LeafScale/LeafOverride` and
  read accessors; `Run` calls the design after the shared causes; a park without goals never picks the bald goalmouth.
- `SiteHarmony.cs`: mood regions are protected from the change smoothing.
- `Lawn.cs`: botanical garden and memorial park default to painted terrain; Garden walk no longer forces a grass surface.

## Runs

| Run id | What it shows |
|---|---|
| `show-22-builds-1029-232925-79c4` | BEFORE: every visible Parks style, 45 + close |
| `show-6-builds-1029-235043-0788` | first moods: places too small, boulders buried out of sight, terrain smoothed by site harmony, yellow leaf carpets |

## What failed and why

- Boulders vanished: the model already sits 1.35 m in the ground; sinking it by half its height on top buried it. Sink
  0.35-0.55 of 1.6 m × scale.
- The knoll came out at half its height: SiteHarmony smoothed the change field (20 passes). Mood regions are protected now.
- Places 8-20 m read as nothing from 45° in a 150 m block: hero places 22-28 % of the side.
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
