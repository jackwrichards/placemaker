# Runbook: farmstead (Farms, P2-3)

A working farm seen from the road: a homestead with its muddy yard, fields with headlands and tramlines, orchards in rows,
woods and rough grazing where the land is poor, ditches or a ravine, heaps, bales, gates and worn tracks.

## Status

- Grade from the user: not yet. Lead review: pending.
- Branch: `claude/p2-farms-9b48f2`. In the user's game: no.

## What the user asked (GRIPES.md, 2026-10-30)

> More traditional farms should be more of the first options in the list rather than animal steads. Think of what farms
> have: barrancas, mulchy areas, forests, orchards, ATVs, constructed areas, big water holders, a homestead, all parameters.

## Real references

- English mixed farm (Cotswolds, Shropshire): hedged fields of 2-6 ha, a yard of barns by the lane, a muck heap, hay bales.
- Arable East Anglia / the Beauce: big fields, tramlines every 24 m, golden stubble, few hedges, grain bins.
- Orchards of Kent, the Altes Land, Normandy: trees 4-5 m apart in rows 6-7 m apart, a bare strip down each row, mown alleys.
- Welsh and Pennine hill farms: walled in-bye fields low down, rough grazing ("ffridd") with gorse and stones above, a
  stream gully (a "dingle") with alders and scrub; Andalusian barrancas for the dry ravine.

## The farm as settings (Type → Style → Settings; add-ons only for the farm stall and the apiary)

| Setting | Group | What it does |
|---|---|---|
| Landscape mix (`biomeFields`, `biomeOrchard`, `biomePasture`, `biomeForest`, `biomeWetland`, `biomeWater`, `biomeRocky`) | Landscape | the shared Biome mix: the farm's own fields are the host; orchards, rough grazing, woods, wet ground, ponds and rock go where the land wants them; the field lattice is cut round them |
| `season` | fields | the fields' paint: spring (drilled earth), summer (green crop, dark rows), harvest (pale stubble, straw windrows), autumn (dark ploughing) |
| `terraces` | fields | steep fields (≥ 7 %) stepped into level benches (Sculpt, banks ≤ 34 %) |
| `rowAngle` auto | fields | rows follow the contour on a slope (≥ 4 %) |
| `farmhouse` | farmyard | the farmhouse beside the yard, facing the road: a house (HouseProps, made from the game's own low-density houses), a levelled lot, a low fence on the road, a hedged garden with a gate toward the yard, fruit trees, a tool shed |
| `vehicles` | farmyard | machinery shed (carport) with the farm tractor, the pickup, a tractor in the yard, more along the barns |
| `yardMud` | farmyard | how worked the yard is: churned mud, puddles, tyre tracks (decals; fact `farmMud`) |
| `heaps` | farmyard | muck heap and compost windrow (low mounds, dark surface, straw), firewood |
| `drainage` | water | ditches along the field boundaries (culverts under the tracks) or a ravine down the fall line with scrub, alders and stones |
| `tanks` | water | water tanks beside the barns |

## The ground plan

| Area | Ground | Paint | Surface | Decals |
|---|---|---|---|---|
| Fields | bare terrain | by season (slots 2 forest floor, 3 earth, 4 sand) and row stripes | none (the brief: paint for fields) | tramlines (24 m), headland wheel line, straw windrows at harvest |
| Pastures, paddocks | grass | none | none | dung, molehills, trodden ground near gates |
| Yard | levelled | earth under gravel/dirt | gravel, dirt or paving | hoof-churned mud, puddles in its low ground, straw, mottle, tractor tracks from the gate, edge wear |
| Tracks | path net | none | none | trailDirt (packed earth), twin wheel ruts, puddles |
| Gates | – | – | – | field: mud and tyre tread; paddock: hoof churn and dung; road: soil and tyre mud out onto the street |
| Heaps | mounds | – | dark forest floor | manure seepage, straw |
| Orchard (biome) | grass | none | none | orchardStrip down each row, windfalls, alley wheel lines |
| Rough grazing (biome) | broken | none | none | long grass, sheep trails, dung, molehills, stones |

## New library kinds (`Recipes/decals/library/farm.json`)

strawLitter, dungPats, hoofChurn, tramline, manureHeap, mudTyreTrack, orchardStrip, windfalls.

## Shared code touched (small, general)

- `Biome.cs`: `RowPitch`/`RowSpacing`/`RowGaps`/row scale on `BiomeDef`, `BiomeWhere.Rows`/`Alleys`; biomes fields (host),
  orchard, pasture. `BiomeBuilder.Rows` (planted rows), `BiomeSite.RowAxis`, `BiomeHost.Plan(rowAxis)`; `BiomeGround`
  lays Rows/Alleys passes from the row frame the builder writes into the plan's facts.
- `Weathering.Designs` registers `FarmGround`.
- `VehicleProps`: a `FarmTractor` slot (the game's field tractor; the `Tractor` slots keep the mining tractor).
- `HouseProps` (IndustrialAssets.cs) and `VehiclePropSystem.MakeHouses`: "Placemaker House 1-4", static props of vanilla
  low-density houses (EU first); `AutoAssets.IsOwn` lets them through like the vehicle props. Any type can use a house now.
- `SiteHarmony`: regions tagged `shaped:*` are kept (not smoothed) when the layer sets the fact `shapedTerrainCells`
  (terraces, the ravine, ditches, heaps). Any type that sculpts designed ground can use it.

## Runs

| Run id | What it shows |
|---|---|
| `show-10-builds-1030-231154-0c65` | baseline before the overhaul (220x200 flat): crop surfaces, a beige slab yard, a dry pond, no life |
| `show-7-builds-1031-000929-4a92` | round 2, every style flat: wooden fences, crop rows, woods, orchard rows; pale painted yards and blotchy ploughing |
| `show-3-builds-1031-003306-233b` | slope 10 %: terraces smoothed away by the site harmony, no ravine (the walker stopped at the yard) |
| `show-3-builds-1031-005331-af71` | slope 10 % after the shaped:* hook: terraces kept (benches on the terrain map), dark yards; the ravine still missing in game |

## Lessons

- Terrain paint over grass is weak: a growing crop painted with the forest-floor slot is invisible from 45°. Growing crops
  need the crop surface; paint is for bare soil only (ploughing, drilling, fallow).
- The earth slot (3) reads as pale blotches at strength: bare soil is forest floor (2) first, earth (3) under it at
  0.3-0.55. A dirt yard painted with earth reads as a beige slab; the Forestry surface plus forest-floor paint round it
  reads as a worked yard.
- The site harmony smooths every sculpted shape it is not told about. Tag the regions `shaped:*` and set
  `shapedTerrainCells` (SiteHarmony.ShapedFact).
- A fall-line walker must steer round obstacles, not stop at them: the yard sits right where water would go.
- The paddock fence default is the wooden post-and-rail (AnimalParkArt.PenFence), never the urban railing.

## Open

- Ponds fill only while the simulation runs: judge them with `-Grow 45`. The farm pond lies on mud (the park lake's sand bed read as a sand pit).
- Gates: the fence kind's own piece hung open in the gap (paddocks, ranch pastures, the garden); the game has no gate object.
- Shelterbelts: the forest biome gives woods; a straight belt along the windward edge is not built.
