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

## Runs

| Run id | What it shows |
|---|---|

## Lessons

## Open
