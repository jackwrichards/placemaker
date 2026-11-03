# Industrial category (2026-10-19)

The user: "a whole new Industrial category ... industrial awesomeness ... it shouldn't just look like a random scattered
blotchy patch of garbage. Like, it should be like someone handcrafted it." And: "not just surface drawn ... surface painted
elegantly ... dirt pile, mud puddle, garbage, clean, dirty ... mound of dirt that's painted, certain sections of concrete,
dead trees".

## The shelf

`Recipes/shelf/industry.json`: shown again, named **Industrial**, last in the Make row (order 9). It shows the `industrial`
group: the four new yards first (generator order 64–67), then the old industrial types that were hidden before (logistics
yard, scrapyard, landfill, quarry, construction site, abandoned lot). Nothing is hidden yet; hide styles there after the
user has seen them. The zoning type **Industrial estate** (`zoningIndustrial`) stays in Zoning: it zones lots, it does not
build a yard (ask the user before moving it).

## The new types (Core/Layers/Types/Industrial)

| type | styles |
|---|---|
| Container yard (`containerYard`) | terminal (harbour stacks, 2 high at the street to 6 high at the back), depot (20 ft rows, 2-high stacks, 40 ft lines, container wall), storage (self storage rows, trees) |
| Car yard (`carYard`) | salvage (pick-your-part double rows, parts bays), crusher (crusher pad with the metal mountain, crushed-metal heaps), breakers (carports with a car under each, parts bays) |
| Junk yard (`junkYard`) | sorted (concrete-block bays, one material each, the mountain, skips), reclaim (timber, pipes, pallets, drums, crates in blocks), tip (skips, bin lorries, rubbish bays) |
| Industrial works (`industrialWorks`) | recycling (plant, skips, lorries, sorted bays), salvage (wrecks one side, containers the other, mountain), works (plant with tall chimney, stacks, rubble heaps) |

One layout for all (`IndustrialSite`): a frame square to the street at the gate (the frame direction with the most yard
ahead of the gate), a front yard (barrier, gate booth, office row end-on to a paved forecourt, staff parking, weighbridge),
a spine, a header aisle, cross aisles between storage bands, column aisles on wide yards, storage blocks that lie wholly
inside the yard, and the front corners beside the office used as a band too. Every filler (`YardFill`) lays props on an
exact grid, one kind per row or bay, filling from the spine outward. The ground (`YardGround`): painted terrain instead of
a flat surface (dirt or gravel base, mud strips along the aisles, mud at the crossings and the gate, oily rings round heaps
and machines, worn dirt under rows, dry grass at the fence), concrete only as pads; spoil mounds (real terraform) only on
left-over ground with a dead tree, dead bushes and a fallen log; dead trees in the edge corners; puddle / oil / litter /
crack decals placed on purpose (asset packs only).

## The props and why (vanilla first; better ones preferred where they exist)

- **Containers**: `ShippingContainer01-03` (20 ft, 2.44 × 6.04), `TrainCargoCar01_Container01` (40 ft, 2.64 × 12.58), the
  harbour stacks `ShippingContainerPile{Small,Medium,Large}{Low,Medium,High}01` (13.9 / 34.9 / 70 m long; the two-high
  small and medium stacks sit 1.32 m off their pivot), `ContainerGridDecal02` for empty painted slots.
- **Skips and bins**: `LargeOpenContainer01` (roll-off skip), `TrashContainerEmpty03/04`.
- **Vehicles**: none in the base game as props, so Placemaker makes them (`Game/Assets/VehiclePropSystem.cs`): the game's
  own cars, vans, trucks, bin lorries, tractors, campers and loaders as static props, named `Placemaker Vehicle <Kind> <n>`.
  Fallbacks: the `Prop_*` vehicle props of asset packs, then a vanilla stand-in of the same size.
- **Offices**: a container (`ShippingContainer01`) as a site office, `ParkingLotBooth01/02` (gate booth), `ToolShed03`,
  campers as mobile homes, `PortableToilet01`, `FlagPole01`, `Trashbin01`.
- **Edge**: `ConcreteBarrier01-03` walls (bays, wheel stops, the edge), `FenceIndustrialLow02` (chain-link, GritAssets),
  `LightpoleIndustrial01/02` masts at the aisle ends.
- **Heaps**: `OreMiningTruck01_OrePile02/03` (rusty metal), `PileOreSmall01` / `PileOreMedium01` (the mountain),
  `RubblePileConcrete/Mixed/Wood`, `PileStoneSmall01`, `CoalTruck01_*Pile01` truck loads (tyres stand in as the dark
  loads), `Trashpile*`.
- **Goods**: `Pallet01-04`, `CratePile01-03`, `BarrelPile01-03`, `Barrel01-03`, `GasBottlePallet01`, `CableReel02`,
  `OilPipe01`, `ForestForwarderTrailer01_LogPile*` (timber), `FirewoodPile*`, `CardboardBoxPile*`.
- **Plant**: `IndustrialManufacturingDecoration0x_*` (the industrial buildings' machinery), `OilTankSmall*`,
  `IndustrialChimney{Small,Medium,Large}01 Agriculture`, `OreConveyorBelt01`, `Carport02` (dismantling bays).
- **Nature**: dead trees (any tree at age 4 = dead, `PlannedAge.Dead`), `FlowerBushWildDead02`, `Log02/05/08`.

## After the first game run (2026-10-19, the user: "a little too perfect ... like a mathematician ... it should look like a photograph of a cool yard")

- **Mess with a reason** (`YardFill.Mess`): rows stay rows, but each prop wanders a little and turns a little, gaps come in
  clumps (value noise), wrecks pile two and three high (more towards the back) and some lie on their roof or side
  (`PlannedObject.Elevation`, `Roll`; the build raises and tilts them, Declutter checks their real height). Containers
  stack in exact columns with an uneven skyline. The game hides one of two props whose boxes meet, so the wander is
  always smaller than the gap the row leaves (`IndustrialSite.Fits` probes the corners too).
- **Less white**: dirt and gravel yards drive on their own trodden ground (paint), no concrete under bays; concrete stays
  for the terminal, self storage and the tip; aisles there are the dark vanilla pavement.
- **The vanilla `Pile*` props (PileOreSmall01 …) render empty on their own** (seen in the game): the mountain is tipped from
  ore truck loads on a terrain mound; sand, dirt and spoil heaps are terraformed cones painted their material; rock piles
  are boulders with stones at their feet (`YardFill.Stockpiles`).
- **Tiny plots** (< 4000 m²) get a compact yard; 20 ha plans in under a second (up to ~11 000 props for a car yard).
- The runner waited 4 minutes per build for a plan while no type was chosen (since d4467e3); `SuiteSteps.PlanReady` no
  longer waits then.

## The organic scrapyards (2026-10-19, after "perfectly laid out scrap piles in a grid ... F")

`OrganicYard`: the car yards (all three styles) and the scrap-metal junk yard are no longer blocks. Same frame, edge and
front yard; then dirt roads that wander from the gate to the back (a lumpy off-centre loop on a bigger yard, a spur or two
to the fence), painted compacted gravel, no surface strip; a work pad kept clear (loader, skips, the crusher's plant and
conveyor); wrecks nose to the roads both sides, the second row piled higher; then the scrap field: piles of 5–10 m (wrecks
piled two and three high, some on their roofs or sides, metal and rubble, trash, drums, crates, by drifting zones) with
trodden ground between them and the odd lone wreck; a terrain swell under every big pile; rubbish and rusty ground in
ragged blobs (kept off the roads); junk spilling onto the roads now and then. The user: "Very good" (then: spill onto the
road, dirt roads, both done). The container yards, the reclamation yard, the tip and the works keep their blocks.

In the game: stacked cars rest 1.3 m up (cars are 1.4–2.0 m tall); tipped props keep their tilt (`Roll`); the build hides
one of two props whose boxes meet, so piles touch but never overlap. 20 ha organic yards plan in about 1.5 s (11 000 props).

## The earth of a yard: the game's own ground pieces (2026-10-19)

The user: "3D objects that make like a dirt pile on the ground ... Let your mind take you to amazing lands", and "we're not
going to use any add-on packs". So every yard is built from the game's own assets only: the add-on names that stood first
in a few fallback chains (asset-pack vehicle props, surface packs) are gone, and the wear decals (puddles, oil, litter) are
off, since every decal of that kind comes from an add-on pack.

Surveyed in the game first (the runner's lab "lineup", `suite/scenarios/lab-ground.json`, `Game/Suite/SuiteLab.Lineup.cs`:
every match of a name pattern in rows, sizes in the log, optional sink/turn/roll):

| Piece | Size (m) | In the game |
|---|---|---|
| Rock01-30 | 1 x 0.3 x 1 | small stones, grey; read as scree in clumps |
| Boulder01-08 | 5 x 2 x 5 | big grey boulders; set 0.3-1 m into the ground they look bedded in |
| OreMiningTruck01_OrePile03 | 7.6 x 1.2 x 9 | a smooth clay-orange mound, the only heap that draws on its own |
| RubblePileConcrete01-04 | 9 x 1.6 x 9 | grey crushed concrete; sunk 0.5 m it reads as rubble strewn in the earth |
| RubblePileMixed/Wood01-04 | 8 x 1.5 x 6-9 | plank and mixed debris heaps |
| Log01-18 | 0.6 x 10 | single logs |
| Pile{Sand,Stone,Coal,Ore,Garbage,Crop}{Small..Massive}01 | 9-50 x 4-16 x 15-200 | draw NOTHING alone (they follow a stored resource) |
| any ...Random01 | | a blue placeholder: never use |
| StandingStone01-08, StoneHead01-03 | 2 x 5 x 1 | monuments, not yard stone |

Sinking works: a prop lowered into the ground (PlannedObject.Elevation < 0, emitted lower without the game's Elevation)
stays where it is put.

`YardEarth`: a bund of boulders inside the fence (edge "boulders", laid before the fill: a 5 m boulder reaches past the edge
band; the band is 6.4 m deep for it), dirt banks (terraformed banks along the back and side fence, spoil worked into the crest: sunk
mixed and concrete rubble, boulders, rarely the clay-orange heap, since a row of them read as rugs in the game; stones
and a dead bush at the foot; option "Dirt banks"), stones at the foot of everything (round the heaps and the
pad, along the dirt roads and wherever free ground lies within a few metres of something, in drifting patches; slider
"Stones", a third on a concrete yard), rubble worked into the ground (sunk concrete, mixed and plank rubble in drifting patches; slider "Ground rubble";
none on concrete, half in a block yard), crushed concrete heaps by the work pad, a burn barrel with pallets to sit on by
the office. No stones on a block yard's pads.

Sunk pieces get the game's negative Elevation: ObjectUtils.GetCollisionMask then gives them the Underground mask only, so
they hide nothing standing on them (seen in the game runner, `lab-ground3`: a car on sunk rubble, barrels in it, all drawn;
sunk without it, the game hid one of each pair). Declutter leaves them out. The junk yard's sorted style and the works style have the boulder bund.
