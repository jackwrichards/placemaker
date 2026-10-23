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

## 5. Left

* Fence runs as one stretchable lane per run instead of 4 m pieces (fewer objects, perfect joins) is possible
  (researched: LaneSystem keeps no extra lane on a building's update, lanes are fitted to the ground at 4 points, nothing
  hides them under a later road); not done.
* About 1 s for the town needs the planner about twice as fast again: the engine is half of a plan in Mono, the rest is the
  per-garden steps (fence closing, lawns, land between angled lots, planting).
