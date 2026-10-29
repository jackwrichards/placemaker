# Strip malls back, Plazas its own category (2026-10-28)

## The user

> We're going to bring back strip malls and we're going to refine them to be excellent, even better than they currently are
> in every way imaginable. Get inspired by real strip mall photos. [...] And then plazas, we're going to be doing that as well.
> Strip malls and plazas are basically hand in hand. But plazas is going to be its whole new category, because we're going to
> really outdo ourselves with that.

Three photos came with it:
1. A corner strip mall in Los Angeles: one storey of shopfronts in an **L** behind a single aisle of parking, a low fence
   along the sidewalk broken only at the drives, a tall **pylon sign** listing the tenants by the entrance, a big shade tree,
   bins at the kerb.
2. A lifestyle centre's promenade: a paved pedestrian street between two rows of shops, a glass arcade, string lights,
   benches, lamps, trees, people.
3. A dining plaza by a department store: patterned paving, yellow parasols over tables, a piano, light poles.

## Baseline (game runs, 150 x 140 m city block)

- `show-8-builds-1028-205715-ccbc` (strip mall, every style): classic, plaza and pond rows about three quarters parking with
  a thin row of shops at the back and a **forest** behind them; corner retail and rear alley still on the old code (a lot in
  a corner and a park, nothing grown on the street).
- `show-16-builds-1028-205900-0fd2` (outdoor mall, shopping plaza, high street) and `show-15-builds-1028-215424-e538`
  (plaza, market square, mall park, mall district): the plazas are empty paved slabs with a tree ring; the shopping types
  mostly lawn and parking; mall park and mall district grow shops round their lanes.

## What changed

### Strip malls (`Core/Layers/Commercial/LotCentre.cs`, `MallFront.cs`, `StripMallLayer.cs`)

- **The L.** The U's side rows can now be on one side only (`Variant.LegA/LegB`): the lane runs along the back and turns
  down one side, the lot runs out to the other side of the site. An entrance on that open side street drives straight into
  the lot (`BesideLeg`). An L whose open side faces a street scores a little more (the lot opens to both streets of the
  corner, as in the photo).
- **Corner retail** is that L (`LotLayout.Shape = "L"`, no double rows in its lot), planned from the player's entrances like
  the classic. The old corner code (an L lane round a corner lot) is no longer used.
- **No forest behind the shops.** A layout now pays for the ground it leaves behind the back row and beside the legs
  (`BehindWeight`), so the row goes to the back of the block; what is left there is the shops' back side
  (`Mall.BackOfHouse`): dry ground, a loose line of trees on the property line only, a dumpster behind every other deep shop.
- **The street front** (`Mall.StreetFront`): the lots keep a 2.5 m planted edge (`LotSetback`); a low hedge runs along it
  wherever the lot meets a city street, open 7 m either side of every drive and footpath; a vanilla pylon sign
  (GasStationPylon, the game's own brands) stands beside the first car entrance, bins at its foot. With the Hedges extra off
  the lot runs to the kerb as before and only the sign is placed.

### The shelf

- `Recipes/shelf/shops.json`: the category is **Shops**. Strip mall: classic, corner retail, big box, plaza, pond shown
  (neighbourhood, rear alley and park row still hidden). Mall district (shopping district, downtown) shown again.
- `Recipes/shelf/plazas.json` (new): **Plazas**. The squares group (plaza, market square) and mall park; outdoor mall, high
  street and shopping plaza moved in, hidden until the lifestyle centre is right.
- Category labels in 12 languages (`Lang/*.shelf.json`): Shops / Plazas.

### Plazas: the dining plaza (first draft)

- Mall park's new middle style **plaza** and preset **Dining plaza** (`MallParkLayer.cs`): a car-free pedestrian ring of shops
  round a paved café square (Plaza planner `cafe`: tables, pergolas, kiosks, a fountain where one is installed, trees round the
  edge), parking round the outside. Game run `show-2-builds-1028-222908-dbb7` (plaza-dining1): built, 4 shops grown, all facing the
  ring (Pedestrian Street Small), 0 on city streets.
- The outdoor mall's lifestyle, loop and promenade styles on 220 x 200 m (`show-3-builds-1028-221707-c75b`): mostly lots and trees, few shops;
  kept hidden.

## Game runs (after)

- `show-5-builds-1028-221649-57cd` (sm-front1): classic, plaza and pond rows build the L; corner retail the L along the back and
  one side; pylon sign at every build; hedge 84-121 m; dumpsters behind the rows; no forest; every shop faces the build's lanes.
- `show-4-builds-1028-222709-b7d5` (sm-narrow): two-cell inline plots tried and taken back (as many plots grew, the classic lost
  its L).

## Not done yet

- Few shops grow per row on a city block (3-5 buildings in 40 s): the rows still read thin against the lot.
- Dining plaza: only 4 shops round the plaza on a city block, warm paving reads sandy, the lots round it are big.
- Neighbourhood strip mall: its frame turns the row onto the side street on a city block (game run
  `show-5-builds-1028-220502-2083`); still hidden.
- Pre-existing test failures not from this work: `ShoreSculptTests.PresetIsTerrainAware` (waterfront pier, river steps).
