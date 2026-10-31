# Runbook: parking looks (P2-5)

Car parks that look nothing like the old grey rectangle: real raised kerbs, a ground that tells you how old and how used
the lot is, car parks from around the world, the game's own equipment, and settings for all of it. Brief:
`docs/refactor/P2-5-PARKING-LOOKS.md`. The tools (hand edits, sketch lines, fees, Manage) are P2-1's
(`docs/runbooks/parking-tools.md`).

## Status

- Branch: `claude/p2-parking-looks-5bc3c5`. Not in the user's game (the lead installs).
- Seen in the game runner: every style on a city block (runs below), the terraced style on a 12 % slope.

## What the user asked (2026-10-31)

- "Curbs need to be there. Decals need to be very adamant." Japanese, German, meters, signs, solar shade, round, terraced.
- A rich district's lot "white and the pavement is nicer, but there's still skid marks"; a bad district; rundown cars in odd
  spots next to real ones; a homeless camp; a lot still being built; gravel; barrancas and storm drains.
- "Not that a boring parking lot shouldn't be an option, but at least it should be decaled and look nice": Classic.

## The styles (references)

| Style (id) | Real reference | What makes it |
|---|---|---|
| Classic (`standard`) | any suburban supermarket lot | today's layout and the game's white stalls, now kerbed: concrete kerbs, sealcoat by age, oil in the stalls, tyre polish, cracks, patches, grates, dashed crossings |
| Japanese coin park (`japanese`) | Times / Repark コインパーキング lots in Tokyo | tight 2.5 x 5 m stalls, no planting, black sealcoat, white boxes, a yellow front line, a lock plate under every car, wheel stops, a coin pay station |
| German (`german`) | a Parkplatz at a Rewe or a Bahnhof P+R | grey block pavers in the stalls, asphalt aisles, granite kerbs, clipped hedges with lindens down the medians, boxed stalls, pay machines at the row ends, bike racks |
| Big-box store (`bigBox`) | a Walmart / Home Depot lot | a vast open field, yellow lines, flood light poles in a grid, cart corrals, yellow-painted kerbs, zebra crossings, more wear |
| Upscale (`rich`) | a Beverly Hills or Zurich shopping street lot | pale concrete with saw-cut joints, hairpin double lines, granite kerbs, lush planting, park lamps, many EV bays, little wear |
| Run-down (`rundown`) | a closed mall's lot | faded asphalt, worn lines with some missing, alligator cracks, potholes, weeds and litter, dumped cars on the verges at row ends, a homeless camp in the far corner |
| Gravel lot (`gravel`) | a fairground or ski-hill overflow lot | gravel over packed earth, ruts down the lanes, puddles, no paint, no lamps, planted edges |
| Under construction (`construction`) | a lot being extended | a third of the lot behind a line of barriers: graded gravel, ruts, a loader and a truck, a site container, toilet, sand and stone piles, cones; nothing planted or kerbed there yet |
| Green with canopies (`eco`) | a solar-canopy lot at a German business park | grass pavers in the stalls, rain gardens (bioswales) in the medians, canopies over every row, many chargers, flush kerbs |
| Round (`round`) | the circular car parks of Disneyland's old lots, a stadium ring | rings of radial stalls round a planted island, T marks, a ring of trees round the circle |
| Terraced (`terraced`) | hillside lots in Lisbon or Genoa | level tiers stepping down the slope, dark masonry retaining walls between them, ramps at the row ends, a drain channel down every lane |
| Angled (`angled`) | as before | 60 degree stalls; the wedges no longer get loose shrubs |

Hidden (shelf): `green` (rf-parking), `compact` (the big-box style does the open field properly).

## Kerbs: the choice

Research (asset agent and lab `send-lab-parking-looks-1031-123914`):
- The game has no kerb prop and no kerbed path or sidewalk net; road kerbs are part of the road meshes. Surfaces are
  flat decals. Terrain is a 3.5 m raster: a 0.15 m step comes out as a soft slope.
- Our plazas already make stone edges from the game's garden walls sunk until a lip shows (`PlazaDesigns.Kit.Wall`,
  the theatre tiers and stair nosings).

Choice: a kerb is a garden wall (0.2 x 0.9 m, along its local Z) sunk until 0.14-0.16 m shows, its face flush with the
asphalt (`Core/Layers/Parking/Kerbs.cs`). It casts a shadow and has a real step at 45 degrees; it bulldozes with the build;
set into the ground (negative Elevation) it collides with nothing and Declutter leaves it alone. `EU_Wall02` reads as pale
concrete, `EU_Wall05` as granite (pale grey in the game too). On a slope the pieces get shorter so their ends stay within
3 cm of the graded ground. Kerbs drop at connectors, walkways and the back of accessible stalls (tactile paving there).

Kerb types: concrete, granite, red and white (paint on the asphalt at the kerb's foot), yellow, flush (a level stone band,
no wall), none. Wheel stops: two 1 m wall pieces at the back of every stall, 0.12 m lip.

## Equipment from the game's assets

| What | Prefab | Note |
|---|---|---|
| Pay station / ticket reader | `ParkingReaderStand01` | entrance, row ends, or one per lot (coin park) |
| Pay booth | `ParkingLotBooth01/02` | the Pay booth setting |
| Boom gate | `ParkingLotGate01/02` | the Barrier setting |
| Entrance sign | `ParkInfosign01-03` + the painted `ParkingIconDecal01` (a P in the lane) | no P-sign pole, price board or road sign exists |
| Canopies | `Carport01-04` (5.9 m roofs), one per two stalls | no solar panel prop exists; the carport roofs read as canopies |
| Lamps | `LightpoleCommercial01` (big-box), `EU_LightpolePark01` (upscale, round), street lights (Classic) | |
| Chargers | `ParkingLotCharger01-04` | P2-1 |
| Bikes, bins | `CommercialBikeRack01`, `BikeRack01`, `Trashbin01/02` | at planted row ends |
| Cart corrals | two `EU_Railing02` in a stall given up | no corral prop exists |
| Dumped cars | Placemaker's vehicle props (`Placemaker Vehicle Car n`) | the game's own vehicle props are not placeable |
| Camp | `MakeshiftTent01-04`, `BarrelFire01`, `OutdoorSofa01`, `Trashpile`, `CratePile`, `Trashbag`, `Pallet` | no tarp, cart or sleeping bag exists |
| Works | `ConcreteBarrier01`, `SafetyBarrier01`, cones, `PortableToilet01`, `ShippingContainer01`, piles, loader and truck props | no portacabin exists |
| Ravine stones | `Boulder01-08` sunk | |

## The ground (`Core/Layers/Parking/ParkingGround.cs`)

Laid by the layer at the end of its plan (like `YardWear`), from causes; every decal checks the Ground section's Decals
switch. Paint first and never thinned; the wear has its own cap (1400 a hectare, at most 9000) and is thinned evenly.

| Cause | Kinds |
|---|---|
| Coat | `sealcoat` per lane and per stall (strength = 1 - asphalt age), `concreteTone` + `sawCut` joints on concrete, `paverBand` / `grassPaverBand` / `concreteBand` per stall, `gravelLot` tiles + ruts on gravel, `asphaltTonePatch` |
| Paint | `paintLineWhite` / `paintLineYellow` / `paintLineWorn` stall lines per country, `lockPlate`, `zebraCrossing` / `dashedCrossing` at entrances and walkways, stop bars, `hatchWhite` on blank stalls, `kerbPaintRedWhite` / `kerbPaintYellow` / `flushKerb`, `tactilePaving` at dropped kerbs |
| Cars | `stallOil` in the back half of every stall (more and darker near the entrances), `oilDrips`, `tyrePolish` down every lane, `skidMark` at junctions |
| Age | `crackAsphalt` / `crackConcrete` chains along lanes and kerbs, `crackSealed` tar snakes on a kept lot, `asphaltPatch`, and when dirty `crackWeb`, `potholePatch`, `weedyCrack`, `litterSprinkle` |
| Kerbs | `kerbGrimeStrip` at every kerb's foot, `leafSquareSummer` in the gutters under the lot's trees |
| Water | `stormGrate` at the low point of every 35 m of lane, `drainStreak`, `puddlePiece`; or `drainChannel` down the lanes; or `dryStreamBed` + sunk boulders (the dry ravine) in the long planted strips |

## Settings

Shape (rows, round, terraced), Paving, Space paving, Asphalt age, Cleanliness, Wear, Line style, Paint age, Crossings,
Stop lines, Curbs, Wheel stops, Islands (grass, shrubs, hedge, rain garden, gravel), Signs, Payment, Barrier, Canopies,
Cart corrals, Bike racks, Bins, Drainage (none, grates, channel, dry ravine), Abandoned cars, Homeless camp, Under
construction, Pay booth. Every style is a preset of them and names every key any style sets, so switching styles never
leaves one style's look behind (`ParkingLayer.BuildPresets`).

## Lessons (also in `docs/GROUND-RUNDOWN.md`)

- **Dark greys draw purple.** Near-neutral decal colours between about 32 and 42 (sRGB) came out violet in the game: the
  albedo is linear, and in BC3 their red and blue land on the 5-bit step above while green (6 bits) stays below. Use 0-12
  or 50 and above (50, 72, 87 are exact neutral steps); darken with alpha, not with a 38 grey. Not smoothness (tried).
- Sheets (`xy` tiles) must lie edge to edge on a grid with no feather, one variant, never turned; an overlapped or turned
  grid shows every seam (gravel, round 1 and 2). Break the repeat with scattered stones and patches.
- A band as wide as a lane and its two rows also coats the row-end islands: coat per lane and per stall.
- Leaves loose in the stalls read as yellow blotches: only in the gutters.
- The game's own stall marking cannot fade: a worn lot paints its own lines over invisible bays.
- The game's concrete surfaces are paper white: a warm grey tone and saw cuts make them read as concrete.

## Runs

| Run | What |
|---|---|
| `show-3-builds-1031-123345-f890` | BEFORE: standard, compact, angled on a block (no kerbs, no decals, teal bushes) |
| `send-lab-parking-looks-1031-123914` | lab: kerb walls sunk, the game's stall markings, equipment |
| `show-9-builds-1031-125942-543c` | first pass of every new style |
| `show-8-builds-1031-131417-0724` | round 2: purple fixed, round lot, gravel tiles, works ground |

## Open

- Timber edges for the gravel lot (log and fence piece sizes need a lab).
- A tall price sign and P sign pole: no such props in the game.
