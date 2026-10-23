# Teardown 12: Outgrow speed (22 Oct)

The user: "zone outgrow seems to have some regression in its speed". Then: as fast as possible, gardens never over each other,
fences continuous, nothing about the look changed. Measured in the game runner (`suite/scenarios/outgrow-speed.json`: nine
blocks of houses grow with Outgrow off, then every lot is outgrown at once, redone, and redone again after a type with every
reach at 500 m is saved) and offline on recorded passes, in .NET and in the game's own runtime. **[P]** = proven by a
measurement below.

## 1. Measurements

| 241-house town, every lot outgrown at once | outgrow | redo | redo, a 500 m type saved |
|---|---|---|---|
| 20 Oct (`queue-1020-001217`, 246 houses) | 5.1 s | | |
| master, 22 Oct morning (`send-outgrow-speed-1022-193555`) | 55.1 s | 53.6 s | 53.6 s |
| first round: reach per lot, planner (`-195129`) | 12.7 s | 11.2 s | 11.4 s |
| plans at once, checked (`-202222`, `-202619`) | 2.7 s | 2.4 s | 2.3 s |
| barrier cells, engine, 7 plans (`-211353`) | 1.9 s | 1.6 s | 1.8 s |

Frames while it works: median 8 ms, worst 77 ms (outgrow) and 26 ms (redo); master: worst 75 to 485 ms. Fence maps of every run:
7 to 16 gaps (corners by a road; master 10), 0 crossings (one T-junction counted once in nine maps), 0 fences inside a garden,
0 gardens over each other. `outgrow-overpass`: nothing in the cracks under low decks, no tree by a deck.

Fewer plans at once (the knob `maxPlans`, a smaller computer): 3 at once 1.8 s, 1 at once 9.4 s (`send-outgrow-speed-small-1022-212618`),
fences the same. Patch size (knob `bigPatch`): 70 to 150 m all within 1.6 to 2.2 s.

Planner alone, in the game's runtime (Mono) on the recorded passes of today's town: 346 ms a pass on master, 134 now; a test
town's passes 163 ms -> 60 ms. Plans exactly the same pass by pass (signatures) on .NET and in Mono, on every recorded set.

## 2. What was slow

* **[P] One plan at a time.** Since 21 Oct each running plan kept every other plan away by the reach of both gardens (about 490
  m with the looks as they ship), so in a town only one ran: 51 s of planning in 55 s.
* **[P] The widest reach of any type, used or not.** Every pass looked as far as the furthest-reaching type in the city
  (`ReachInUse`); one type at 500 m (the user's "Shops 2") made each pass take in 400 m of town, and anything built re-planned
  every outgrown house within 530 m of it.
* **[P] Each plan about 4x slower in the game than on 20 Oct**: the rules added since (about 1.6x on the same passes) and Mono:
  a vector operation is a call there, lists are calls, and the polygon engine made dozens of small arrays per boolean (300
  booleans and 400 offsets a plan, 34 edges each on average).
* **[P] About 10 ms of the frame per plan** reading every road, building and lot area in a box that covered most of the town.

## 3. What changed

* Reach per lot (its type with its own changes, `LotReach`) for the neighbourhood, the barrier box and what something built
  re-plans; each type's settings worked out once and shared.
* Up to 7 plans at once (half the threads less one, at least 1), patches 110 m while many houses wait (50 m otherwise), kept
  apart by their patches only. The plan finishing later checks what the others wrote since it gathered its houses
  (`NeighbourPolishSystem.Writes`): a garden written meanwhile within 2 m of one it would write (as it was or as it is) and
  nothing of it is written, its houses are planned again with what stands. Waiting neighbours a pass edits anyway are planned
  in it. A big plan written on one frame, the next patch gathered on the next.
* Barriers read once per 128 m cell and kept until something there is built, removed or changed
  (`NeighbourPolishSystem.BarrierCache`): 1 to 2 ms a pass on the frame.
* Planner and polygon engine, exact (the same plans): fence lines, walls, barriers, gardens and footprints looked up by grid
  cell or bounds instead of all of them; caches by a ring's numbers (insets, grown barriers) with counters, not
  ConcurrentDictionary.Count; feature spots searched without measuring points that cannot win; booleans skipped for shapes far
  apart; the engine's working arrays reused per thread, duplicate edges merged through a stamped table, points and edges in
  plain arrays, its sweep and intersection maths written out.

## 4. Tools

* `Tests/Placemaker.OutgrowBench` (.NET) and `Tests/Placemaker.OutgrowBench.Mono` with `tools/monohost` (the game's own Mono,
  offline): replay a run's `polishPassDump` passes, time them, compare signatures with an older Core's.
* `outgrow-speed.json`; polishIdle reports plans, planner time, plans planned again (`retries`), houses taken (`asked`) and why
  fence lanes put houses in line (`linesHow`). Knobs for tuning in one session: `maxPlans`, `bigPatch`, `coreAll`.

## 5. A city of 15 000 (the user's save "123", the same evening)

The user, after the merge: about 10 FPS less with every building outgrown (fine) but stutters (not fine: "there should be no
stutter whatsoever"), gone the moment Outgrow is paused; and outgrown gardens growing into streets. Measured in the game
runner on the save itself, loaded read-only (`suite/scenarios/outgrow-city-*.json`; lab op `outgrowFrames`: every frame's time,
each Outgrow system's main thread time, the garbage collector, why houses were planned).

* **[P] Gardens over streets: a cap.** Every pass took at most 4 000 barriers; in the city every pass had more (a 600 m patch:
  its roads alone), so the roads read last were left out, and with them every other building's lot and the water. 141 of
  2 500 saved gardens lay over roads (52 900 m²); planned again with the cap: 180; without it: 68 slivers of 2 m² (156 m²
  in all). Planning the whole city again took 29 s instead of 26.5. After a load, a garden saved over a road is planned
  again (`GardenOverRoad`, with the empty ones).
* **[P] Stutter 1, the garbage collector.** 105 collections in 45 s with Outgrow on (82 ms each frame they ran in), 11 paused.
  Their cause: about 270 plans every 45 s in a city where nothing was built, each with its whole neighbourhood (60 to 280
  houses). Why houses were planned (`putInLine`, 30 sampled lanes): level-ups, every one. A level-up swaps in another model at
  once (BuildingUpkeepSystem, BuildingConstructionSystem.UpdatePrefab): new fence lanes, sub-objects and sub-areas.
* **[P] A level-up cascade.** The grid of houses keeps each house's model from when it was read; a house that levelled up was
  described with its old model (its fences, footprint), and every fence the game touched next looked like another level-up: in
  the save, 49 level-ups made 336 plans in 45 s. The model is now read again for each pass: 107 plans, 43 collections, 32
  frames over 50 ms (paused: 3, 2 collections; `send-outgrow-city-ab-1022-233110`).
* **[P] A building site re-planned every garden reaching near it** (one row house: 252 gardens). Only the gardens over its lot
  are planned again now (they give it back); the rest keep theirs.
* **[P] What a plan costs.** On 25 plans recorded in the save (`polishPassDump`, OutgrowBench): 38 MB of garbage and 152 ms
  each on average, though most edit 2 to 6 houses: a plan works out every house of its neighbourhood (60 to 280), and only the
  edited ones are written. That is what is left of the stutter with level-ups planned.
* **[P] Stutter 2, hidden pieces judged at once.** About 1 100 of our pieces the game hides were looked at again every 2 s, all
  on the same frame: 150 to 200 ms. Now each at its own frame after about 30 s, 24 a frame, and at once where a plan was
  written: 5.5 s -> 1.0 s of 45.
* **[P] Stutter 3, surfaces.** Each frame that made a lawn, bed or path read every surface of the city to avoid a double: 15
  to 80 ms. Now looked for around the spot in the area search tree: 2 s -> 0.06 s of 45.
* **[P] The outgrowCheck itself** compared every fence with every garden edge (hours in a city): fences by 8 m cell.
* **New switch, "Outgrow again when a building levels up"** (this city's, on by default). Off, a level-up keeps its garden
  without a plan (`NeighbourPolishSystem.LevelUps`): pieces, beds, paths and the lawn past the lot made again where they stood;
  each new fence that lines up with an old one takes its place (moved, hidden or as it was), a new fence the old garden had
  nowhere is hidden, an old fence nothing took the place of is laid in pieces. Compared from the same state (the whole city
  planned again with this build first: `outgrow-city-ab.json`), 80 level-ups kept: open fence of the city +4 m (29 605 m),
  hidden fence -2 m, bare lawn ±0; 2 plans in 45 s, 3 frames over 50 ms (paused: 3), 2 collections
  (`send-outgrow-city-ab-1022-233110`; the run before, 64 kept: open fence ±0, hidden +5 m, bare -5 m²).
* Comparing runs: the save was outgrown by an older build, so a run that plans more houses changes the city's fence and lawn
  numbers by itself. Looks are compared within one run, after planning the whole city again (Redo), as differences.
* After a load every outgrown house's model and fences are remembered as they stand (`SeedLaneMemory`; 3 104 moved fences in
  the save): a level-up is told from a fence the game only marked.

## 6. Left

* A plan that works out only the houses it edits and what touches them (garbage and time in proportion to the change): what
  is left of the stutter while level-ups are planned, and what "outgrow a city without a hitch" needs.

* Fence runs as one stretchable lane per run instead of 4 m pieces (fewer objects, perfect joins) is possible
  (researched: LaneSystem keeps no extra lane on a building's update, lanes are fitted to the ground at 4 points, nothing
  hides them under a later road); not done.
* About 1 s for the town needs the planner about twice as fast again: the engine is half of a plan in Mono, the rest is the
  per-garden steps (fence closing, lawns, land between angled lots, planting).

## 7. The stutter with every switch on (23 Oct)

* **[P] The planner's garbage, 38.4 -> 13.2 MB a plan** (the user's city; 2.5 to 4.6x less on every recorded set, about 1 400
  plans, every one planned exactly the same in .NET and in the game's Mono: 51 -> 16 collections for the city's 25). By type
  (OutgrowBench --alloc-types): the polygon engine's arrays made for every boolean (now its instances and arrays kept per
  thread: Arrangement.Rent/Release), a delegate for every vertex's sort (sorted in place), IsSimple's arrays, min/max helpers,
  line grids per plan, barriers copied and filed again to trim fences (now looked up in their own grid), text for every
  neighbour turned down.
* **[P] In the game, everything on:** collections 43 to 49 -> 7 to 16 in 45 s.
* **[P] Standing fences remembered** (NeighbourPolishSystem.StandingOf) until a lane or piece of the house is made, moved,
  placed again or deleted: gathering a whole city's plans 1 349 -> 488 ms. Knob checkStanding reads them again and compares:
  0 of 10 657 had changed unseen (once pieces the game sets down a frame later are counted).
* Left: a plan's gathering is still on one frame (up to about 30 ms), hidden pieces shown again after a plan (bursts of about
  20 ms), the barrier cells dropped when many buildings update at once (8 to 12 ms), ground paint (2 to 5 ms a stamp).

## 8. The fewest neighbours a change plans again (23 Oct)

The user's pillars: Outgrow fast, little memory, off the main frame, and a change plans again only the neighbours it must.

* **[P] A building removed** (or a lot unpolished, or an unpolished one rebuilt) gives its land only to the gardens that touch
  it (NeighbourPolishSystem.Removals: TouchingDirty), zoned or not. Before: every house within 70 m, and every unpolished
  one among them every polished house within 70 m of itself. 4 houses bulldozed in the game runner: 28 gardens planned again,
  now 8, and none across the street (outgrow-removals.json).
* **[P] A removed house's own surfaces and paths** (its driveway, its lawn) no longer plan again every garden within reach of
  them as "something bulldozed nearby" (CollectNewNearby: what a building removed this frame took with it is its own). This
  was most of the user's 20 lots across the street.
* **[P] A plan never takes along a house across a road, path or track** from the ones it is asked for (RoadBetween; knob
  coreAcrossRoads 1: as before). Every house planned again with and without it: 0 of 76 gardens changed.
* **[P] A level-up of the same lot size** (its garden comes out the same) takes along only the gardens touching it (knob
  coreTouching 0: as before). The model a house was last planned with is kept apart from the one last read
  (_plannedModel): a street levelling up at once read every neighbour's new model in the first plan (1 of 32 level-ups
  recognised; now 15 of 16). In the runner's suburb the 40 m was already only touching houses once roads are left out; the
  user's denser city gains more.
* Tried and dropped (the user: too much nuance): keeping a plot still zoned for the next building. It worked (nothing planned,
  the new house outgrown once) but left the gardens beside it open meanwhile, and zoning that shrinks or goes needed more.
