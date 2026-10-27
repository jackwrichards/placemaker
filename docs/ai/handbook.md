# AI builder handbook: what the premade types know

What the hand-written planners (`Core/Layers/**`) encode, and what the user's grades (`docs/refactor/GRADES.md`,
`docs/refactor/<category>.md`, `docs/teardown/*.md`) praised or rejected. It is source material for the AI builder's
system prompt (`Core/Ai/AiPrompts.cs`, "Agent") and for planning new AI tools (`Game/Ai/AiTools.cs`). Paths are
relative to the repo root. Distances are design metres; the planners scale them by `Layout.Scale` (`ctx.M`).

Note on path widths: the planners' normal path is 3 m and the wide walk is 8 m (`Core/Composition.cs:426-427`). The
AI's `place_path` draws 4 m (normal) or 6 m (`wide`).

## 1. Design rules that matter

### Paths and entrances
- **Number of entrances by area** (`Core/Layers/Common/Entrances.cs:113-119`):

  | Area | Entrances |
  |---|---|
  | under 0.25 ha | 1 |
  | under 1.5 ha | 2 |
  | under 4 ha | 3 |
  | under 10 ha | 4 |
  | larger | 5 |

- **Entrances:** each path comes straight in from the street edge, then joins the network (`Core/Layers/Park/ParkPathStyles.cs:143-147`). A main gate has a pair of planters and a pair of lamps (`ParkLayer.cs:289-317`), and only the main entrance gets the wide walk.
- **The network:** join the entrances with the shortest network first, then add loops for coverage, then one shortcut (`ParkPathPlanner.cs:1-7`).
  - Stay at least 5 m (preferably 12 m) inside the outline.
  - Keep parallel paths at least 8 m apart.
  - Branch at about 30° or more (`ParkPathPlanner.cs:29-33`).
  - Path setbacks: 3 m from the outline, 4 m from water, 3 m from a plaza (`ParkLayer.cs:386`).
- **Curves are gentle sideways waves, not zig-zags:**
  - natural: 5 m amplitude over a 34 m wavelength
  - garden: 6.5 m over 18 m
  - botanical: 5.5 m over 16 m
  - playground: 3.5 m over 24 m
  - Sources: `ParkLayer.cs:431-432`, `BotanicalGardenLayer.cs:167`, `AdventurePlaygroundLayer.cs:134`.
- **Grades:**
  - Walking paths: 8 % at most (`Common/TerrainSurvey.cs:22`).
  - Hill trails: 9 % by default, 4-20 % range (`LookoutHillLayer.cs:67`).
  - Hiking: 10 % by default (`HikingTrailLayer.cs:47`).
  - Waterfront walk: 2 % or less (`WaterfrontLayer.cs:8-17`).
- **Bad paths (user):**
  - A path going nowhere, or a track stub ending in the grass (`STRIP-MALL.md:5-9`, `farms.md:40`).
  - An 8 m walk on a hill or in a nature type: it "reads as a road" (`nature.md:38,42`).
  - A path hooking round a summit instead of walking onto it (`nature.md:62-71`).
  - Kinked chord chains instead of smooth curves.

### Planting
- **Trees go in groves, not in grids or a ring.**
  - In the natural park, groves cover 28 % of the area (`ParkStyle.cs:39`), with radius clamp(√area·0.16, 12, 30) m and wobbly outlines at least max(10, 0.8·(r1+r2)) m apart (`ParkPlanting.cs:122,130-147`).
  - Tree shares: 55 % in groves, 30 % in an 8 m edge belt, 12 % or less as solitaires (`ParkStyle.cs:39`, `ParkLayer.cs:31`).
- **Spacing** (`ParkPlanting.cs:177-238`):
  - Trees in a grove: 7.5 m × 0.85-1.35.
  - Trees along a path: 8 m.
  - Solitaire trees: 14 m.
  - Bushes: 4 m, or 3.2 m beside paths.
  - Clearance from the boundary: trees 4 m, bushes 2.2 m (`ParkPlanting.cs:159`).
  - Tree bounds cover only the trunk. Space trees by their crowns.
- **Leave a central clearing:** radius min(inscribed radius − 2, √lawn·0.3, 30) × the style's factor, at least 6 m. No grove covers it (`ParkLayer.cs:495-502`).
- **Room kept clear for features:** playground 20 m, picnic 13 m, fountain 11 m, restrooms 6 m (`ParkLayer.cs:95-96`).
- **Densities per planted hectare (water and plazas excluded):**

  | Type | Trees/ha | Bushes/ha | Source |
  |---|---|---|---|
  | natural park | 26 | 65 | `ParkLayer.cs:194` |
  | garden walk | 22 | 95 | |
  | urban pocket | 45 | | |
  | sports park | 25 | | `SportsParkLayer.cs:140` |
  | wetland | 60 | 80 | `WetlandLayer.cs:73-74` |
  | campground | 110 | | `CampgroundLayer.cs:72` |
  | greenway | 110 | 120 | `ExtraLeisureLayers.cs:255` |
  | golf parkland | 140 | | `GolfLayer.cs:115` |
  | reserve forest | 260 | 120 | `NatureReserveLayer.cs:48-49` |
  | reserve heath | 20 | 340 | |
  | hiking forest | 300 | | `HikingTrailLayer.cs:96` |

- **Lines of trees:**
  - Street-tree row inset 5 m with a tree every 9 m (`ParkPlanting.cs:170`).
  - Allées every 9 m (`MemorialParkLayer.cs:88`).
  - Greenway avenue every 16 m at half-width + 3.2 m from the path (`ExtraLeisureLayers.cs:283,302`).
  - One species per line.
- **Bad planting (user):**
  - A wall of trees, a boundary ring of single trees, or a tree grid that reads as an orchard (`parks.md:51`).
  - Flowerbed tiles scattered "like confetti". Mass them into beds instead (`parks.md:84`).
  - 70 pruned trees/ha in a tea garden reads as a forest (`parks.md:122`).
  - An axis buried under lawn trees (`parks.md:91`).
  - Trees clumped over parking aisles (`parking.md:28-33`).

### Furniture rhythm
- **Benches:** every 100/furniture m, so 25 m at the default of 4 and about 14 m in urban (furniture 7) (`ParkLayer.cs:514-516`).
- **Lamps:** every 0.68 × the bench spacing, about 17 m. Bins: one per 90 m of path (`:517`).
- **Placement:**
  - Alternate the sides of the path.
  - Set each item back half the path width + its footprint + 0.2 m, facing the path (`ParkPlanting.cs:536-551`).
  - Keep at least 14 m between benches and 10 m between lamps.
- **Default assets:** GardenBench01-04, LightpolePark01/02/04, Trashbin01-03 (`ParkArt.cs:15-19`).
- **Other rhythms:**
  - Plaza: furniture at least 7 m clear of entrances, 0.65 m off the edge and 0.55 m apart (`PlazaPlanner.cs:54-57`). Keep 12.5 m clear around a fountain (`PlazaLayer.cs:126`).
  - Bollards: every 14 m (`MemorialParkLayer.cs:96`).
  - Parking lamps: every 30 m on the medians (`ParkingLayer.cs:163`).
  - Greenway: a rest stop every 60 m (2 benches and a bin) and a lamp every 25 m (`ExtraLeisureLayers.cs:321`).

### Ground, slopes and terraces
- **Base ground:**
  - Parks lay Grass Surface 01 over the whole outline (`ParkLayer.cs:536`).
  - Nature types use no lawn; they use forest-floor paint patches with r 8-15 m (`NatureReserveLayer.cs:173-177`).
  - Beach and greenery: paint lush grass with soft edges. A neon grass-surface rectangle looks wrong (`beach.md:202-204`).
- **Hills are one smooth landform.** Levelled 5 m squares look like stairs (`nature.md:73-76`).
  - Heights: hilltop 18 m, terraces 16, sledding 14, kite hill 12, mound 8 (`LookoutHillLayer.cs:100-109`).
  - Max slope 30 %, and 25 % or less within 15 m of the outline (`HillField.cs:53-54`).
  - Tree line at 65 % of the peak height (`:176`).
- **Lots and pads:**
  - Parking bays: 2 % fall or less; rows 5 % or less; ramps between tiers 8 % or less; banks 1:2.5 (`LotGrading.cs:48-56`).
  - Farm yard: 15 % slope at most (`FarmsteadLayer.cs:833-840`).
  - Furniture never stands on slopes over 12 % (`BeachLayer.cs:16`).
  - Sports pads get a levelled apron of clamp(0.12·half-side, 2.5, 7) m (`FieldAddOn.cs:142`).
- **Earthworks:**
  - Gorge sculpt walls stay at 30 % or less (`HikingTrailLayer.cs:479,493`).
  - An earlier staircase sculpt left pits 15-80 m deep (`nature.md:84-92`).
  - Never leave a pit next to a road (`00-user-feedback-1215.md`, req. 5). An entrance dip of 4-7 m was a known defect (AGENTS.md).

### Water edges (all need real water; see the gaps table)
- **Pond:** water level = lowest shore − 0.5 m, basin lowered 3 m more (`ParkLayer.cs:884-897`). Without a water source it reads as a dry sand pit (`parks.md:50`, `farms.md:55-56`).
- **Wetland** (`WetlandLayer.cs`):
  - Water covers 35 % of the area in 3 ponds, 1.2 m deep (`:57-60`). The paths come first and the water fills what they leave (`:151`).
  - Ponds keep 6 m or more from the edge, 3 m from paths and 5 m from each other (`:164`).
  - Reed belt 4 m, reeds on a 1.5 m pitch from 3 m into the water outward (`:507-524`).
  - Shallow shelf 0.35 m deep (`:232`).
  - Boardwalks 0.6 m above the water, straight and level (`:202-211`).
- **Beach** (`BeachLayer.cs:35-64,316-345`):
  - Sand runs from 20 m under the water to 44-60 m inland, at a 5 % slope.
  - Wet band 6 m, the blend fades over 14 m, and the bank behind is 25 % or less.
  - Umbrellas sit between wet band + 3 m and path − 3 m (`:918`).
- **Waterfront:** one smooth walk with a radius of 40 m or more, railings only where the drop exceeds 1 m (`WaterfrontLayer.cs:8-17`).

### Scale and composition
- **Every type has one readable idea and a focal point:**
  - a monument at the end of an axis
  - a summit plaza
  - a hub at the gate
  - a hero field facing the spectators
- Round 3 of the AI builder lacked a focal point, and the user marked it down.
- **Variety without chaos:** industrial piles placed at random were "a random scattered blotchy patch of garbage". Perfect grids were "like a mathematician" and earned an F (`industrial.md:90,144,160`). Use ordered structure with a little jitter: position ±0.15 m, yaw ±0.03, 12 % missing (`Industrial/YardFill.cs:54`).
- **Never overlap props:** the game hides one of any two props whose boxes meet (`industrial.md:149-150`).
- **Unbuildable assets:**
  - Vanilla `Pile*` props draw nothing.
  - `…Random01` props render as blue placeholders.
  - 40 ft containers render empty, so use two 20 ft containers instead (`industrial.md:153,192`; `ContainerYardLayer.cs:73`).
- **No farmland-looking crop textures outside farms.** No fences across an entrance. Every stall reachable (`07-commercial-defects.md:452-486`).

## 2. Categories and types

Visible types and styles come from `Recipes/shelf/*.json`. The user's grade is in brackets.

### Roads (leave alone) — `roads`
- **Road classes** (`Roads/RoadClasses.cs:18-22`): lane 8 m, minor 16 m, medium 24 m, major 32 m. Never join a highway.
- **Geometry:**
  - Junctions at least 45 m apart; city links at least 30 m apart (`RoadTopology.cs:272,280`).
  - Side streets meet at 62° or more (`:21`); a bend over 14° gets a fillet (`:19`).
- **Blocks:** no sliver block under 1200 m² or 12 m wide (`RoadsPlanner.cs:41`). Grid 100 m by default (range 60-300), max grade 10 % (`RoadsSettings.cs:211,303`).
- **Fit:** streets line up with the neighbouring city streets and never modify a city road (`docs/ROADS-SMART.md:27-28`).
- **Styles** (`RoadsLayer.cs:188-242`):
  - downtown: 90 m grid, boulevards
  - suburb: cul-de-sac tree
  - village: organic 75 m
  - hillside: along the contours
  - garden-city: radial with rings
  - walkable-grid: 80 m, paths 100 %
  - terraced-grid
  - suburban-loops
  - rural-village: gravel
  - superblocks: car-free insides
  - old-town: circus core
  - strict-grid
  - terraced-hillside, serpentine-village, vineyard-terraces, hill-town: terraces
- strict-grid and superblocks currently build nothing.

### Parking [D-] — `parking`
- **Dimensions** (`ParkingSettings.cs:92-106`): stall 3.0 × 5.9 m; aisle 7 m; cross lane 3 m every 9 stalls; median 2.5 m; setback 1 m.
- **Stall angles:** 90°, 60° or 45°. Rows run parallel to an edge. Every stall opens onto an aisle.
- **Entrance:**
  - Off a minor road within 40 m (`Roads.cs:36`), away from junctions (15 m) and lot corners (10 m) (`EntranceSuggest.cs:57-72`).
  - Lots over 6000 m² get an entry and an exit at least max(40 m, 15 % of the perimeter) apart (`:80-82`).
- **Planting:**
  - Trees in straight lines down the medians, one every 3 stalls, at least 7 m apart, one species per line, with shrubs between the trees.
  - Shrubs only on row-end caps. Nothing in a stall (`parking.md:37-49`).
- **Lamps:** StreetlightSingle02 on the edges, Double02 in medians shared by back-to-back rows, every 30 m (`Lanterns.cs:4-16`).
- **Ground and fences:** level the lot; no fences, ever.
- **Styles:** standard (90°, cross lanes, a lane around the lot); compact (most stalls, planting only at the edges); angled (60°, wedge islands). Hidden: green.

### Parks — `park`, `botanicalGarden`, `memorialPark`, `adventurePlayground`, `zenGarden`, `amphitheatre`

**Park [B to B+]** (`Park/ParkLayer.cs`, `ParkStyle.cs`)
- Grass base, curvy paths from the street entrances, groves with a clearing, furniture along the paths. A playground, picnic, restrooms and flowerbeds are on by default (`ParkLayer.cs:228-229`).
- Trees: a broadleaf mix of Oak, Linden, Chestnut, LondonPlane, Hickory and Birch (`ParkArt.cs:25`).
- Styles:
  - natural: loose groves, curvy walks, a playground clearing, 26 trees/ha.
  - garden (Garden walk): meandering paths with 55 % of the shrubs as flowering borders along the walks, 95 bushes/ha (`ParkStyle.cs:117-121`).
  - urban (Urban pocket):
    - spoke-and-ring paths
    - a central paved plaza covering 9 % of the area (12-90 m) with a kiosk and no trees
    - a street-tree belt of Linden/Plane
    - 45 trees/ha
    - furniture 7 (`ParkLayer.cs:35,160,215,241`)
  - picnic: a perimeter circuit, an open lawn, broad shade clumps, and 8 picnic groups (3 tables, a shelter and shade trees each) (`ParkLayer.cs:225-246`).

**Botanical garden** (`Leisure/BotanicalGardenLayer.cs`)
- Narrow meandering loops. Each lawn between the paths is one themed collection (`:1-4`).
- A glasshouse range of 3 × Greenhouse01-03 beside the main path (`:171-196`).
- A HedgeLow boundary. Lamps every 22 m. Every 4th lawn is a wildflower meadow (`:223,254`).
- Styles:
  - flowerShow: 35 beds/ha, plus 10 carpet beds (round, r 3.6-5.2 m), made of Flowerbed tiles on a 2.4 m grid, one kind per bed, edged with shrubs every 1.5 m (`:65,98-160`).
  - shrubbery: 140 beds/ha, 60 % of them within 6.5 m of the walks, and 8 accent trees/ha (`:236-245`).

**Memorial park [A-]** (`Types/MemorialPark/MemorialParkLayer.cs`)
- A long axis from the main gate to a big monument on a paved forecourt (10 m), with the walk splitting around a reflecting pool (`:1-15,66`).
- Allées every 9 m; hedged rooms of 14 m built from HedgePieceHigh pieces (about 4.1 m each); bollards every 14 m (`:83-96`).
- No lawn trees on the axis. Bosque groves are mirrored across it, spaced at least 8 m (`:718-722`).
- The monument is a StandingStone scaled 220-350 %. Always place one and make it big (`parks.md:100-105`).
- Plaques: OldCemeteryTombstone. Flags: FlagPole01/02 (`MemorialAssets.cs:13-22`).
- Styles:
  - garden: flower rooms, no pool.
  - war: forecourt raised 2 m, 12 plaques, 10 flags, poppy borders.
  - reflection: pool 70 % of the axis length and 7 m wide.
  - sculpture: no axis; a meandering trail with 14 sculptures on pads against hedges (`:114-124`).

**Adventure playground [A-]** (`Types/AdventurePlayground/AdventurePlaygroundLayer.cs`)
- A paved parents' hub of r clamp(0.16·inscribed radius, 4.5, 10) m. Age zones ring it, youngest nearest, with tree-free sightlines from the hub (`:1-14,139-150`).
- Item radius 2.1 m (toddler 1.4 m, jungle 2.8 m). Spacing = 2r + clearance (`:334-336`).
- Play surfaces: sand, rubber (Tiles Surface 02) or bark (`PlayArt.cs:24-33`). Toddler zones are fenced.
- Styles:
  - adventure: 24 items.
  - toddler: 14 items on rubber, jets, 14 benches.
  - nature: 6 items on bark, 18 logs, 14 boulders, a 2.5 m mound, a willow tunnel, 75 trees/ha.
  - jungle: 30 items around a central ClimbingFrame04 tower (`:100-108`).

**Zen garden [ok]** (`Types/ZenGarden/ZenGardenLayer.cs`)
- A stroll loop inside a wall or bamboo screen.
- Stepping stones every 0.9 m (`:79`).
- Odd-numbered rock groups on moss rings, with moss painted under the garden (`:728-755`).
- Bad (`parks.md:120`): painted gravel that reads as dead lawn, and rock "specks".
- Styles:
  - karesansui: 55 % gravel surface, 5 rock groups of Boulder01-06, 20 pruned trees/ha, 3 lanterns.
  - stroll: pond 14 % of the area, 2 bridges, 2 islands, 10 lanterns.
  - tea: tea house and basin, 3 rock groups of three, 35 pruned trees/ha (`:111-118`).

**Amphitheatre [F]** (`Types/Amphitheatre/AmphitheatreLayer.cs`)
- A stage at the focus; stepped seat rows in an arc facing it; radial aisles; a top promenade; rim trees and a tree backdrop.
- Size the bowl to the site: one too small, with an empty corner, was the defect (`squares.md:72,162`).
- Styles:
  - classic: 10 rows over a 160° arc, each 1.8 m deep and 0.45 m higher, benches every 2.5 m, 3 aisles, a 7 m stage.
  - bandstand: 4 rings over 360° of deckchairs every 3.5 m around a gazebo (`:101-109`).

**Hidden parks:**
- dogPark: fenced runs, double-gate airlocks, agility course.
- picnicGrounds: folded into park as the picnic style.
- cemeteryGrounds: grid paths with graves in rows.
- orchardPark: a fruit-tree lattice with mown aisles.
- formalGarden: mirrored parterres with a fountain round-point.
- communityGarden: allotments, sheds, a fence.

### Beach — `beach`, `waterfront` (best: promenade B-)

**Beach** (`Types/Beach/BeachLayer.cs`)
- Bands are measured from the real waterline: sand, then a promenade, then dunes, lawn or woods.
- A lifeguard post; boats in groups of 2-4, 2.6 m apart (`:891-899`); a jetty 0.8 m above the water (`:834`).
- Styles:
  - sandy: umbrellas scattered at 110/ha, 2 lifeguards, 2 m dunes, a 26 m jetty.
  - resort: umbrellas in rows at 150/ha, a wide promenade, a palm avenue on a lawn, a 44 m pier.
  - cove: 3.5 m dunes, marram grass, fire rings, no amenities (`:136-154`).
  - Hidden: rocky, river, volleyball.

**Waterfront** (`Types/Waterfront/WaterfrontLayer.cs`)
- One smooth paved walk on the edge, deck 1.0-1.8 m above the water.
- Styles:
  - promenade: a 14 m walk, timber boardwalk edge, railings, benches every 14 m, lamps every 21 m, a tree row, a park behind.
  - pier: two 50 m T-piers.
  - harbor: a 22 m quay apron, bollards, 3 kiosks, two 36 m T-piers (`:145-155`).
  - Hidden: riverSteps, fishing.
- Bad (`beach.md:185-204,249`): piers that were never built, flat yellow paving that reads as sand, an empty lawn behind the resort.

### Shops & squares [F] — `stripMall`

**Strip mall** (`Commercial/StripMallLayer.cs`, `Mall.cs`, `docs/refactor/STRIP-MALL.md`)
- It is a parking lot with shops. The lot lies between the street and the shops and is entered only from the player's entrances.
- One shop lane runs along the back. The shops stand behind it and face the lot. Aisles join the lane at both ends: no loop, no dead end (`STRIP-MALL.md:22-26`).
- Plots are 8 m zone cells, 32 m deep (`StripMallLayer.cs:55`). The target is about 50/50 shops to parking; "90 % parking" was rejected.
- Lot depth = aisles·18.8 + (aisles−1)·2.5 + 2.5 m (`Mall.cs:610-612`). Lane junctions keep 12 m from street corners (`Mall.cs:27`).
- Lawn is painted (not a surface) and kept 2 m off roads. The site needs at least 1600 m².
- Bad: shops facing the city street, fenced or unreachable stalls, "green rectangles", farmland textures (`00-user-feedback-1215.md`).
- Styles: classic; plazaRow (a plaza in the middle of the back row); pondRow (a pond there). Hidden: corner, neighbourhood, rearAlley, bigBox, parkRow.

**Hidden types, one line each:**
- shoppingPlaza: a U lane around a court.
- shopsPark: a park woven through shop plots.
- outdoorMall: a pedestrian street ending on a plaza.
- highStreet: twin lanes with a market strip.
- retailPark: big boxes on a horseshoe lane.
- plaza:
  - a Pedestrian Area of paving with a centrepiece
  - a bosquet on a 7 m lattice
  - an edge allée inset 5.8 m with a bench between every other pair of trees (`PlazaDressing.cs:682,737`)
- fairground.
- mallDistrict: lane grid, courtyard gardens; "nearly A".
- mallPark: a loop lane around a real park; liked.
- marketSquare:
  - a tree ring
  - back-to-back stall grid, stall count set for 4000 m² and capped at 240
  - two 4.8 m cross aisles (`MarketSquareLayer.cs:89-100`)

### Nature [A] — `campground`, `natureReserve`, `greenwayPark`, `lookoutHill`, `hikingTrail`, `wetland`

**Wetland [A+, don't break]** (`Types/Wetland/WetlandLayer.cs`)
- Path loops come first; ponds fill the rest.
- Islands; boardwalks over the largest ponds; hides on spur platforms at least 35 m apart (`:218-221,337`).
- Thick reed belts. Marsh paint reaches the belt + 3 m. Wet woodland at the margins; half the trees more than 25 m from water are dropped (`:546-549`).
- Hummocks 1 m high, at least 8 m from water (`:262-266`).
- Styles:
  - wetlandPark: the defaults.
  - waterGarden: 25 % water, 5 ponds, zig-zag boardwalks.
  - reedMarsh: 55 % water, 7 m reed belt.
  - rainGarden: long narrow basins.
  - birdReserve: one deep pond, 6 islands, 4 hides, a fence (`:104-114`).

**Lookout hill [A to A+]** (`Types/LookoutHill/*`)
- One smooth landform with a single path from the street to a paved summit plaza with a centrepiece.
- Benches on the plaza rim; trees below the tree line.
- Trail: 4 m steps along the contour at a constant grade; legs clamp(0.3·√area, 24, 70) m, at least 2·half-width + 3 m apart; round hairpins; smoothed with Catmull-Rom (`HillPaths.cs`, `LookoutHillLayer.cs:261-327`).
- Styles:
  - hilltop: 18 m, spiral path, tower.
  - sledding: a dome with an open 26 m run, bales, a pavilion.
  - terraces: 4 levels, switchbacks, lamps.
  - kiteHill: 12 m dome, direct path, cairn.
  - Hidden: viewpointMound.

**Campground** (`Park/CampgroundLayer.cs`)
- A loop of narrow paths. Pitches of 10 m spaced pitch + max(2.5, 0.45·pitch) along the path, the two sides staggered by half a step, half-width + 0.8 m off the path (`:45,119-128`).
- Each gravel pad holds a tent, a fire and a table. Pines between the pitches.
- One restroom per 25 pitches. Reception 9 m inside the gate (`:144,186`).
- Visible style: forest. Hidden: meadow, caravan (mundane).

**Nature reserve** (`Leisure/NatureReserveLayer.cs`)
- Narrow meandering trails only.
- Clearings of r 9-15 m with one bench each. Fallen logs at 3/ha. Forest-floor paint. A lookout shelter on the highest point. No lawn (`:91-177`).
- Styles: forest (260 trees/ha); heath (birch and pine over flowering scrub). Hidden: wildwood.

**Greenway** (`Leisure/ExtraLeisureLayers.cs:226+`)
- A bike-lane spine under a single-species avenue, plus a footpath 32 m to the side, joined at both ends (`:350`).
- Rest stops every 60 m; clumped copses.
- Style: leafy. Hidden: open.

**Hiking trail** (`Types/HikingTrail/*`)
- An A* route that costs grade, giving switchbacks. Rest stops every 220 m, markers every 150 m, 3 m clear either side (`HikingTrailLayer.cs:53-70`).
- Styles:
  - forestTrail: 300 trees/ha.
  - meadowWalk: worn trail paint.
  - ridgeHike: the ridge lifted 3 m, 4 viewpoints.
  - canyonTrail: a 6 m gorge with rocks.
  - Hidden: summitClimb, lakeshoreLoop.

### Farms — `animalPark`, `farmstead`

**Farmstead [good]** (`Types/Farmstead/FarmsteadLayer.cs`)
- **Yard:** at the entrance, aspect 1.5, 8 % of the site.
- **Yard buildings:**
  - silos in a battery at a pitch of 2r + 1.2 m
  - barns along the back, 2r + 3 m apart
  - trailers nose-in every 6.5 m
  - a 2×3 hay-bale block
  - a windpump
  - two old trees outside the front (`:599,860-938`)
- **Tracks:** run unbroken from the yard to the outline.
- **Fields:**
  - On a grid aligned to the spine, 1.6:1, with 3 m headlands (`:354-355,742`).
  - Grain fields get straw, vegetables rows, fallow ploughed ground, hay green ground.
  - Hay bales in rows every 16 m.
  - A lone tree in 60 % of the pastures (`:440-487,816`).
- **Paddocks:** start 12 m out from the yard. Each has a 4 m gate facing the yard, worn dirt inside the gate, a trough, hay and a shelter in the far corner (`:658-986`).
- **Hedgerows:** pieces every 2.6 m with a 7 m gap in the middle of every side (`:1127-1131`).
- **Styles:**
  - dairy: 25 % arable, 4 paddocks.
  - arable: 100 % arable, 3 ha fields, irrigation rigs.
  - mixed: 60 % arable, 1.2 ha fields.
  - ranch: fenced pastures, 6 paddocks.
  - Hidden: hobby.

**Animal park** (`Types/AnimalPark/AnimalParkLayer.cs`)
- A visitor hub at the gate, r clamp(√area·0.09, 10, 18) m, on gravel, with a barn or shed, a kiosk, picnic tables and bins (`:150,584-608`).
- A loop walk inset 9-18 m (`:176`). Up to 12 pens.
- Each pen has a shelter at the point farthest from the walk, hay and a trough by the gate (`:522-552`).
- Arena: 40×20 m on sand, with jumps in two lines (`:611-640`).
- Never grey sand pens or gazebos as aviaries (`farms.md:18-44`).
- Styles:
  - pettingFarm: 6 pens around a meadow with a pond.
  - paddocks: 4 pens on a spine walk.
  - horsePaddock: 3 pens and an arena.
  - deerPark: 1 large wooded pen, a pond, a hide.
  - Hidden: aviaryGarden.

**Hidden:** urbanFarm: market garden, vineyard, greenhouses, lavender, rooftop beds, city farm ("ugly, repetitive").

### Sports [D, keep the uniqueness] — `sportsPark`, `golf`, `urbanRuins`

**Field sizes with run-off** (`AddOns/Fields/FieldAddOn.cs:33-63`):

| Field | Sizes |
|---|---|
| Pitch | 121×86 (full), 87×62, 67×51, 47×39 |
| Track | 190×106, 143×83, 97×57 |
| Tennis | 38.6×20.3 per court |
| Basketball | 34×25 |
| Skate | 34×26 |

**Sports park** (`Leisure/SportsParkLayer.cs`, `SportsParkComposer.cs`)
- Layout from the outside in: a tree belt of clamp(5.5 % of the span, 6, 15) m, a promenade loop, then the hero field on the far side, aligned with the dominant edge. A spectator terrace and a clubhouse face it.
- Courts cluster by type. Big fields keep 6-18 m apart (`SportsParkComposer.cs:173-175`).
- Bad (`sports.md:271-331`): white concrete slabs, benches as nets, picket fences around tennis, six overlapping bike shelters at one entrance.
- Styles:
  - community: pitch, tennis, basketball.
  - stadium: a track around a pitch.
  - tennisClub: 3 two-court blocks on clay.
  - neighbourhood: courts, skate, table tennis.
  - Hidden: campus.

**Golf** (`Types/Golf/GolfLayer.cs:114-124`)
- Styles:
  - parkland: 9 holes of about 180 m, 30 m fairways, ponds, 140 trees/ha.
  - links: treeless, 40 m fairways, bunkers.
  - pitchPutt: 65 m holes.
  - miniGolf: 18 felt lanes.
  - drivingRange: 16 bays, target greens.
  - discGolf: wooded 10 m corridors, baskets.

**Urban ruins** (`Types/UrbanRuins/UrbanRuinsLayer.cs:101-110`)
- Pillar pairs every 16 m along a deck line; fallen spans on dark paving with jersey barriers. Skate pads with about one obstacle per 100 m².
- Bad: big sites left as empty lawn.
- Styles:
  - diySpot.
  - highwayRuins: 65 % collapsed.
  - parkour: blocks 4.5 m apart on gravel.
  - dirtJumps: 3 lines on full-strength dirt paint, not dry grass.
  - Hidden: graffitiPark.

**Hidden:** skatePark (street, bowl).

### Industrial — all visible

**Shared site frame** (`Industrial/IndustrialSite.cs:1-14`)
- One frame square to the street at the gate. Layers in order: an edge band inside a high chain-link fence (FenceIndustrialHigh), a front yard, a spine, aisles of 6-14 m (8 by default), and storage blocks that each hold one kind of thing.
- Gate: an asphalt apron, a boom barrier and a booth. Floodlight masts at the aisle ends (`YardFill.cs:686-835`).
- **Ground paint** (`YardGround.cs:28-129`):
  - dirt over everything
  - gravel worn into 2 m strips along the aisles
  - one ragged slab per block: concrete under containers, rusty ground under wrecks, oily ground under heaps
  - never forest floor (it reads green), never white concrete everywhere, never grass inside the yard
- Spoil mounds with a dead tree in the leftover corners (`IndustrialYardLayer.cs:152`).
- Sunk rubble 0.5-0.95 m down.

**Container yard** (`Industrial/ContainerYardLayer.cs`)
- Stacking: 20 ft boxes (2.44 × 6.04 m) in exact columns, 2.62 m per tier. The outer columns step down (`YardFill.cs:64,175-183`).
- Styles:
  - terminal: 2 high at the street rising to 4 at the back.
  - depot: mixed rows, gravel.
  - storage: tidy single units 1.2 m apart.

**Car yard** (`Industrial/CarYardLayer.cs`, `OrganicYard.cs`)
- 6.5 m dirt roads that wander, with a loop if the yard is over 0.6 ha. Wrecks nose to the road, second row higher, piled higher toward the back.
- Wrecks stack 1.3 m per tier, 3 high at most.
- A work pad with a loader and skips.
- Rated "very good" (`industrial.md:162-168`).
- Styles: salvage, crusher, breakers.

**Junk yard:** sorted, reclaim, tip. **Industrial works:** recycling, salvage, works (a plant with a chimney as the centrepiece).

**Older types, one line each:**
- logisticsYard: parking-style grid.
- scrapyard.
- landfill: terraformed mound.
- quarry: terraced pit with a spiral haul road.
- constructionSite.
- abandonedLot.

### Zoning — 9 types (`Types/Zoning/ZoningTypes.cs`)
- They zone 8 m cells from the road and let lots outgrow their borders. The AI cannot do any of this (see the table below).
- The types: suburb, village, townhouses, mainStreet, townCentre, downtown, businessPark, industrialEstate, seaside. Each has 3 styles (`ZoningTypes.cs:32-159`).
- Hidden: the polish looks.

### Hidden category: Transport
- restArea: a car park with an amenity hub.
- transitPlaza: a kerb lay-by with bus and taxi bays, shelters, and a plaza.

## 3. Capability gaps

What the AI tools (`search_assets`, `place_objects` [asset, x, z, yaw, elevation], `place_path` [4/6 m footpath], `scatter_objects`, `shape_ground`, `paint_ground`, `place_surface`) cannot do today:

| Type(s) | Needs | How the planner does it |
|---|---|---|
| roads | Roads as nets attached to city roads; terrain fit; junction rules | PlannedNet: `Roads/RoadConnections.cs`, `RoadCityLinks.cs`, `RoadTerrainFit.cs`, `RoadTopology.cs` |
| parking, stripMall, restArea, transitPlaza, sports/hiking car parks | Drive aisles and entrance connectors as invisible road nets; stall marking decals snapped to stalls; an owner lot area; terraced grading | `Parking/ParkingAssets.cs:17-25`, `PlanBuilder.cs:520,607-654`, `LotGrading.cs:48-56`. Decals are placeable as objects, but stall fitting (0.85-1 scale, `ParkingMarkings.cs:14-20`) needs scale |
| stripMall, mall*, highStreet, zoning types, edge zoning | Zoning cells, growables, lanes with zoning switched off on one side, outgrow | `Commercial/Mall.cs:39-47`, `Core/Build/LaneSides.cs`, `Game/Build/BuildService.ZoneBlock.cs`, `Types/Zoning/PolishLookLayer.cs` |
| plaza, marketSquare | Pedestrian Area with access markers | `Plaza/PlazaLayer.cs:5-6`, `Leisure/MarketEntrances.cs:246` |
| park (pond), wetland, zenGarden stroll, memorial pool, golf, animal park, farmstead ponds | Water bodies with a water source (a Lower terraform with WaterLevel/WaterAnchor) | `ParkLayer.cs:884-897`, `WaterBodies.cs:605-610`, `ZenGardenLayer.cs:422`, `GolfLayer.cs:617`. Fallback: a water-look surface plus a fountain rill (`MemorialParkLayer.cs:483-499`) |
| beach, waterfront | An existing waterline; piers, jetties and boardwalks (LeisurePier01* nets); quays (QuaySmall*) | `Beach/BeachLayer.cs:820-856`, `Waterfront/WaterfrontArt.cs:21-30` |
| wetland, zenGarden stroll, hikingTrail | Elevated, level boardwalks (FollowTerrain=false); footbridges | `WetlandLayer.cs:286-296`, `HikingTrailLayer.cs:391-414`, `ZenGardenLayer.cs:416` |
| farms, animalPark, adventurePlayground (toddler), zenGarden wall, sports (tennis cage), industrial, park fence, amphitheatre rails, dogPark | Fences as nets with gate gaps | `ctx.StraightNet(..., NetRole.Fence)`: `FarmsteadLayer.cs:541,961-1060`, `AnimalParkLayer.cs:500,625`, `IndustrialYardLayer.cs:162`, `ParkLayer.cs:546`; emitted in `Game/Build/BuildService.Emit.cs:142,210`. Hedge pieces (HedgePieceHigh) are props the AI can place |
| park, greenway, nature | Path kinds: 3 m narrow, 8 m PedestrianPathWide01, bike-lane path, dirt or gravel trail nets, painted desire lines | `Core/Composition.cs:426-427`, `ParkLayer.cs:125-134,816`, `ExtraLeisureLayers.cs:270` |
| memorialPark, industrial | Object scale (monument 220-350 %, fountains); roll (cars on their roof or side) | `MemorialParkLayer.cs:497`, `YardFill.cs:109-127`, `OrganicYard.cs:399` |
| sportsPark, urbanRuins, transitPlaza | Line decals aligned to fields (FootballFieldDecal01, TennisCourtDecal02, BasketballCourtDecal01) and levelled field pads | `AddOns/Fields/FieldAddOn.cs:33-173`; decals are placeable but need exact fit or scale |
| amphitheatre, lookoutHill, hikingTrail ridge/gorge, memorial (war), landfill, quarry | Smooth graded landforms (bowls, domes, blended mounds, spirals); polygons give terraces | `AmphitheatreLayer.cs:473-510`, `LookoutHill/HillField.cs`, `Mounds.Raise`, `HikingTrailLayer.cs:479-493` |
| park, animalPark, farmstead, wetland | Animal spawners | `ParkLayer.cs:530`, `AnimalParkLayer.cs:441-450,574`, `WetlandLayer.cs:269` |
| urban park, industrial | Placemaker vehicle props; street shops and kiosk plots | `industrial.md:128-129`, `ParkLayer.cs:241` |
