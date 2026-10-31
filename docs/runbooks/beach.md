# Ground runbook: beach (Beach category, P2-2)

The Phase 2 beach overhaul (2026-10-31, `docs/refactor/PHASE2.md` "P2-2 Beaches"). Earlier work: `docs/refactor/beach.md`
(rf-beach). Keep this file true as you work.

## Status

- Grade from the user before: B+ at best, D at worst (GRIPES, 2026-10-30).
- Branch: `claude/p2-beaches-d87490`. In the user's game: no.

## The user's brief (their words)

> The terrain needs to be more aggressive and confident with moving terrain around to make things look good ... the user
> might draw it on a cliff or whatever, and the warnings need to pop up if it doesn't work or if it's not going to look good
> ... a mulchy beach, sort of like a Tahoe type rocky area, or like extreme amount of rocks where it's like a barranca ... a
> marshy beach with like boardwalk marsh ... a cove or a harbor type rock situation where it sticks out with a ton of rocks
> ... shells ... beach towels or beach toys ... Sand pits, sand runoff. Your imagination should be exploding here.

Also: bridges into the water and onto islands must join (close-up of every joint), never quays.

## Styles and the real places they are judged against

| Style (id) | Real reference | Landforms | Ground | Things |
|---|---|---|---|---|
| Family beach (`sandy`) | a town beach on the Baltic (Binz, Sellin) | carved bay, berm 0.7 m, dunes 2 m behind | sand into the water, dunes lighter | umbrellas, loungers, lifeguard chairs, swimming jetty, boats; towels, toys, castles, pits |
| Promenade beach (`resort`) | Nice, Rimini lido | bay 16 m, berm 0.5 m | sand, lawn behind | umbrella rows, wide promenade, palms, long pier |
| Wild dunes (`cove`) | Schiermonnikoog, Studland | foredune 5 m with 3 blowouts, hummocky back dunes, berm 0.9 m | sand, ripples, sand thrown out of the blowouts | marram, fire rings, a boardwalk over the dunes to the water |
| Wild beach (`wild`) | an undeveloped strand (Rossbeigh, Holkham) | low dunes 3 m with 2 blowouts, berm | sand, heavy wrack, kelp heaps, driftwood | fire ring, nothing else |
| Shingle beach (`shingle`) | Brighton, Chesil Beach, Aldeburgh | 11 % face, 3 storm ridges, cusps | earth paint under a shingle carpet, ridge crests of cobbles | a row of huts, boats hauled up, wide promenade |
| Pine lake beach (`mulch`) | a pine-fringed lake (Muskokas, Bavarian Seen) | short sand strip, berm 0.25 m, two rock knolls | sand giving way to forest floor, pine duff and needles over the sand | pines down to the sand, jetty, kayaks, fire ring, towels |
| Granite shore (`granite`) | Sand Harbor, Lake Tahoe | 7 granite domes on the beach and in the shallows, 130 boulders/ha in clumps out into the water | coarse sand (earth through it), wet weedy rock at the water | Jeffrey pines, jetty, kayaks |
| Rocky ravine (`ravine`) | a Canary Islands barranca | bluff 6 m behind a pocket beach, 2 ravines cut to the water | earth on the ravines and the bluff, scree aprons, stones carpets | boulders choking the ravines, footbridges where the walk crosses |
| Marsh shore (`marsh`) | Norfolk saltmarsh, the Wadden Sea | a near-level flat just above the water, 4 creeks cut below it (they fill), salt pans | forest floor (dark), salt pans, creek mud | reed beds along creeks and pans, boardwalk out to the water with a T |
| Harbour cove (`harbour`) | a Cornish cove (Porthgwarra, Kynance) | two rock headlands 30 m out into the water, rock platforms on 35 % of the shore, bay 28 m | sand between, earth on the rock | boulders piled on the headlands, tide pools, boats, jetty |

Hidden (shelf): `rocky` (superseded by harbour cove and granite), `river`, `volleyball`.

## Landforms (Core/Layers/Types/Beach/BeachForms.cs)

Laid on top of the shore profile as functions of the distance from the real waterline and the arc length, so they follow the
bay. Each is a setting (any style can use it): `berm`, `storms`, `headlands` (length), `platforms` (%), `bluff`, `ravines`,
`knolls`, `boulders`, `channels`, `blowouts`, `hummocks`, `boardwalk`. The earthwork limits follow the forms (the old 1.5 m
fill cap had flattened the 3.5 m "taller dunes" to 1.5 m: the reason wild dunes read flat).

## Decals (Core/Layers/Types/Beach/BeachLayer.Decals.cs; family `Recipes/decals/library/beach.json`, 22 kinds)

| Cause | Kinds |
|---|---|
| water | wetSandBand (strip, heavy side to the water), wrackLine (strip at the high-water mark), shellScatter, strandPebbles, kelpHeap, driftwood |
| wind | sandRipples (dry sand, dunes), blownSand (promenade's beach side), sandFan out of each blowout |
| rain | sandFan down the bank behind the beach |
| feet | sandFootprints (trails from each landing to the water), trampledSand |
| families | beachTowels by half the umbrellas, beachToys and sandPit by the water |
| rock | rockPlatform, tidePool, wetRock (platforms, headlands, boulders at the water), rockOutcrop (knolls, headlands, ravine walls), screeApron and stonesCarpet (ravines), gravelFan (ravine mouths) |
| style | shingleCarpet and shingleRidge, pineDuffSand and pineNeedlesCarpet, saltPan, marshMud, shoreMud along creeks, sedgeLitter, ashScorch at fires |

New stamps (DecalGenerator): `shell`, `towel`, `toy`, `kelp`.

## Biome mix (the land behind the beach)

The beach offers lawn (its own backdrop), dunes, shingle, heath, meadow, forest, rocky, wetland (`BeachLayer.Biomes`), planned
behind the dunes / the promenade / the bluff. New biomes: `dunes`, `shingle` (Biome.cs). Plant-only on the beach (`shape:
false`): the beach shapes its own land.

## Warnings and best practice (Lang/en-US.beach.json, `Placemaker.Beach.Issue.*`)

No water, short shore (< 40 m), narrow land (< 25 m), steep bank (> 22 %), cliff advice (> 30 % cliff).

## Bridges and joints

Every deck starts on a path end: jetties on the access in line (or a spur off the promenade), the boardwalk on a stub off the
promenade, footbridges between the walk's two ends at a ravine. BuildService joins ends within 0.15 m into one node. The game
runner's camera target `joint` (index `all`) frames each joint for a close-up (`tools/suite/beach-scenario.js`, view `joints`).

## Runs that prove it

| Run id | What it shows |
|---|---|
| `show-12-builds-1030-231137-c657` | BEFORE: the six old styles on a lake and a cliff |
| `20261031-003718-beach-r2-lake` | round 1/2 code on the lake: headlands show, boulders white popcorn, marsh without boardwalk |
| `20261031-015913-beach-r2-hard` | every style on a 7 m cliff and a 3 m steep bank: cliff-top walk only, no beach (advice now a warning) |
| `20261031-023318-beach-r3-lakein` | outline over the water: islands with footbridges (harbour, granite, marsh), headlands, marsh boardwalk |

## What failed and why

- Baseline: the family beach's carved bay left a band of grass under the shallow water (sand stopped 20 m out from the new
  waterline); fixed by covering the whole bay floor.

- Headlands were absent in the game: the setting `headlandLength` no longer existed, so every headland was 0 m long; the
  carved bay also swallowed them (harbour has no bay now).
- Boulders read as white popcorn: `Boulder01-08` were sunk only 0.3 x scale; they now sink 0.4-0.6 of their 1.6 m.
- Islands and rocks in the water landed outside the drawn outline; they now need the outline drawn over the water (an
  island only appears where its disc fits inside it).
- The marsh read as a sandy beach with bushes: its base paint is now Lush (marsh), not sand.
- Joint close-ups: the runner schema did not know the `joint` target, and after a build the tool has no plan; the runner
  now keeps the built nets.

## Left to do

- Ravine: the barranca reads as boulder streams on brown ground from 45°; steeper ravine walls and a visible bluff face.
- Hinterland: lawn with palms behind family and harbour looks like a park; dunes biome patches read as sand blobs.
- A hint when Islands is set but the outline does not reach over the water (12 languages).
- Cliff: the beach could cut a pocket beach into a cliff instead of only a cliff-top walk.
