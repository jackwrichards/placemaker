# P2-5 Parking looks: every kind of car park (the full brief)

Phase 2 wave B, agent 1. Read `docs/refactor/PHASE2.md` first: its **shared block** (reading list, what Phase 1 gave you,
stay in your lane, push to the max, prove in the game, report format, rules) is part of this brief, word for word. This
file replaces the short P2-5 section there.

## The user's words (2026-10-31)

> "Let's go all out. They're going to look nothing like they do now. I guess there'd still be some settings to make it
> look like it does now and some presets, but **curbs need to be there**. **Decals need to be very adamant.** Think of
> all different types: Japanese, German, meters, signs, solar shade, round, terrace lots, all that."

And from `GRIPES.md` (read every line of it yourself):

> "We need to go completely all out on parking lots. User customization is key. All different types of parking lots,
> Japanese parking lots, German parking lots. Look at the different assets. What do we have access to? Do we want a
> parking meter on entrance? … medians, walkways, grass, maybe no grass at all, maybe special types … Decals, cracks, all
> customized … a crossing for people crossing, like a dotted line or dashed line … circle parks, terraced parking, so it
> works on hills cleanly. parking signs. parking with solar shade overlays."

> "Barrancas, storm drains … rundown cars could be placed in spots that aren't real parking spots versus real parking
> spots next to them … a rundown parking lot, a nice district, a bad district … A homeless camp … rundown cars, I guess
> that'd be a slider … an industrial area kind of built into it where it's like constructing it, or unpaved areas …
> Grass areas could be nicer … The plazas look so nice. I think that's where you could draw your inspiration from …
> a rich neighborhood's parking lot where it's like white and the pavement is nicer, but there's still skid marks …
> Not that a boring parking lot shouldn't be an option, but at least it should be decaled and look nice."

> "Make sure these agents are A+ even if they come back. I really like what you did before where you didn't let them
> quit until it was perfect."

## Where parking stands today (start here)

- **One type, `parking`**, three styles shown: `standard` (B−), `compact` (B−), `angled` (C+); `green` hidden
  (`Recipes/shelf/parking.json`). Code: `Core/Layers/Parking/`. History and grades: `docs/refactor/parking.md`.
- **P2-1 Parking tools just landed** (merged on master, `docs/runbooks/parking-tools.md`, the audit table in
  `docs/refactor/parking.md`): slow lanes, Edit hides the old build, Manage (occupancy, fees, the list of every lot),
  the "Zone grid" row direction with corners snapping to zone cells, **hand edits** (accessible groups, removed stalls,
  islands, chargers, the grid's edge), **sketch lines** (`Core/Tool/LineSketch.cs`: "Draw aisle" adds a drive lane with
  rows along it, "Draw walkway" a footpath), "Space markings" off (invisible stalls). Build on all of it; don't redo it.
- **P2-1's hand-over to you:**
  - drawn walkways are paved like the aisles and need a look of their own;
  - median trees are still sparse.
- **Open items in `docs/refactor/parking.md`** ("Not done / known problems") that are yours now:
  - On a slope, the entrance connector climbs a bank between the graded lot and a street more than 6 m away.
  - The angled style's wedge islands get loose shrubs.
  - The upkeep companion is off by default; verify it in the game, then switch it on.
- **Biome mix** (`docs/runbooks/biomes.md`) gives noise-shaped areas of character; use it for the green parts (verges,
  big islands, a lot's planted edges), not for the asphalt.
- **The shared ground tools:** `docs/GROUND-RUNDOWN.md`, `docs/DECALS-PLAYBOOK.md`, the decal library
  (`Recipes/decals/library/*.json`, 65+ kinds, the browser at https://claude.ai/artifact/BTMZX1HfRAhjkRHdCBzJCN), the
  brushes, and the plazas (`docs/runbooks/plazas.md`), which the user loves: study how they build their paving, edges and
  decals, and bring that richness to car parks.

## The goal

Car parks that look nothing like today's: every lot reads as a real, specific place, with real kerbs, confident decals
and the details you'd photograph. **A "Classic" style keeps today's look** (and settings can bring any style close to it),
but even Classic gets kerbs and a proper decal pass. Nothing is a boring grey rectangle any more.

## What to build

### 1. Kerbs everywhere (the user's first must)

- Every island, median, planter, walkway and lot edge gets a **real raised kerb**, visible at 45° and close up: a
  kerbstone line with its shadow and a height step, not a painted stripe.
- **Research first:** what the game offers. Vanilla kerbed nets (pedestrian, sidewalk and path pieces with kerbs), the
  plaza and pavement surfaces with edge geometry, kerb props, the ways Parking Lot Tool and our own plazas make edges.
  Write the options and the choice in your runbook.
- Kerb **types** as a setting:
  - granite or concrete kerbs;
  - flush or dropped kerbs at walkways and accessible stalls (with tactile paving decals);
  - painted kerbs (red and white, yellow), wheel stops;
  - none, for a gravel or rundown lot.
- Kerbs must bulldoze clean and survive Edit, rebuild and reload like everything else.

### 2. Decals, "very adamant"

Confident, plentiful, designed. Not a few stains: the asphalt itself should tell you how old and how used the lot is.
- **Wear that follows use:**
  - oil drips centred in every stall (darker in busy rows);
  - tyre polish and skid marks in the aisles and at turns;
  - worn and faded paint where cars cross the lines;
  - kerb grime and leaves along the kerbs and in the corners.
- **Surface:**
  - cracks (alligator, longitudinal, at joints);
  - patched and resealed strips, tar-snake crack seal;
  - sealcoat colour by age, puddles and storm-drain stains at low points;
  - grit and gravel spill at unpaved edges.
- **Markings:**
  - stall lines in each country's style (single, double hairpin, T marks, full boxes, numbered stalls);
  - arrows, "STOP" and "SLOW", EV and accessible symbols;
  - **dashed and dotted crossings and zebra walkways** for people;
  - hatched no-parking areas, fire lanes, loading bays, reserved, motorbike and bicycle bays.
- **Make new decal kinds freely** (data only), and show them in the decal browser. Keep within the shared memory budget:
  measure it with every category at once.
- Every decal checks `DressingControls.GroundDecalsOn` (the Ground section's Decals switch), and the look settings below
  tune amounts (wear, cleanliness, paint age).

### 3. Styles: car parks from around the world

Each is a complete ground design (terrain paint, surfaces, kerbs, decals, planting, equipment), with its own character at
45°, at the top and close up. Look at real references and name them in your runbook. At least:
- **Classic** (today's look, kerbed and decaled).
- **Japanese coin park** (コインパーキング):
  - tight lots with lock plates under each car and a pay station;
  - yellow and white markings, tall price signs, white numbered stalls;
  - neat, almost no planting, often on an odd-shaped corner.
- **German** (Parkplatz):
  - grey concrete-block or paver stalls with asphalt aisles;
  - granite kerbs, hedged and tree-lined islands;
  - a Parkscheinautomat (ticket machine), blue P signs, bicycle stands.
- **American big-box / mall:** vast, light poles in a grid, cart corrals, worn paint, landscaped islands at the row ends.
- **Rich district:**
  - bright pale concrete or fine pavers, crisp kerbs, lush planting, valet or EV bays;
  - still skid marks and oil, just fewer.
- **Bad district / rundown:**
  - faded and missing lines, potholes, weeds in the cracks;
  - rundown cars in spots that aren't real stalls (next to real ones), a broken barrier;
  - optionally a **homeless camp** in a corner (tents, tarps, carts, litter).
- **Unpaved / gravel:** gravel or packed earth, timber or rope edges, puddles and ruts, an overflow or event lot.
- **Under construction:** part of the lot built, part graded earth with barriers, cones, a site cabin, materials.
- **Green / eco:**
  - grass pavers or permeable paving, bioswales (planted storm-water channels);
  - **solar shade canopies** over the rows, EV chargers.
- **Round:** a circular or ring car park with radial or curved rows around a central island.
- **Terraced, for hills:**
  - stepped terraces with retaining walls or planted banks between them, and ramps that link the levels;
  - it must work cleanly on a 5–15 % slope and on hills. Grading must leave no funnels, banks over 50 % or stalls on a
    tilt; check the pit and dip logs.
- **Park-and-ride or stadium**, if you have room: huge, numbered zones with tall zone signs, bus bays.
- **Barrancas and storm drains** (a setting, or part of eco and terraced): a dry ravine or concrete drainage channel
  through or beside the lot, grates and inlets at the low points, little culvert bridges for the aisles.

Invent more if you find them in the references (a French one with plane trees, a Dutch canal-side lot, a ski resort's
snow-edge lot). These are a floor, not a ceiling.

### 4. Settings (user customization is key)

Type → Style → Settings → Add-ons (`docs/ARCHITECTURE.md`, "Controls model"). Styles are presets of these settings:
- **Paving:** asphalt colour and age, concrete, pavers, gravel, grass pavers; stall surface different from the aisles.
- **Markings:** the country set, line style, paint age, numbered stalls, crossings on or off.
- **Kerbs:** the kerb type, as above.
- **Wear:** cleanliness (pristine to neglected) and wear amount. (Neglect as a whole is its own style, never a slider,
  but wear and dirt amounts are fine as settings.)
- **Planting:** islands and medians (none, grass, hedge, trees, bioswale), tree species, density; **grass or no grass
  at all**.
- **Rundown cars:** a slider for how many sit in the odd spots.
- **Equipment:** each on its own:
  - signs (entrance P sign, price board, directions, zone signs);
  - a parking meter or pay machine at the entrance, or pay stations along the rows;
  - barriers at the entrance;
  - light poles (type and spacing);
  - solar canopies, EV chargers, cart corrals, bicycle stands, bins.
- **Shape:** rectangular rows, angled, round, terraced (where the land allows).
- **Add-ons**, only for real facilities: a homeless camp, a construction corner, a kiosk or pay booth, a car wash, a
  bus or taxi stop.
- **Use P2-1's hand edits and sketch lines** for the rest: drawn walkways get a look of their own (paving, kerbs,
  crossings where they meet aisles); islands placed by hand get kerbs and planting.

Every shown control must change the plan (`ControlEffectTests`), and every label is short and in all 12 languages.

### 5. Equipment from the game's own assets

Look through the vanilla prefabs (`.forensics/suite` catalog, the decompiled game) for parking meters, pay machines, sign
props, barriers, canopies and solar panels, chargers, bollards, cart corrals, lamp posts, cones, tents and tarps. Use what
exists. Where nothing fits (a solar canopy, for example), say what you found, then build the closest good thing from
pieces (a roof or shade prop, a panel prop on posts) or decals, and tell the lead. Anything that needs new shared code
goes to `docs/refactor/PHASE2-CORE-REQUESTS.md`.

### 6. The fixes you inherit

- Median trees: denser, and in proper lines.
- The angled wedges: no loose shrubs.
- The slope entrance: a gentle ramp from the lot to a street that sits further off.
- The upkeep companion: verify it in the game, then switch it on by default.
- The strip mall and mall district lots share the parking planner: whatever you change must still look right there (run
  them too), and P2-6 (Shops) builds on your lots next. Write in your report exactly what P2-6 can reuse.

## Where to test (every style)

A city block, 220×200 m, a big site (a mall-sized lot), an odd shape (an L and a triangle, for the Japanese corner lot),
a 5–15 % slope, and hills (terraced must shine there). Pictures at 45°, top and close for every style on flat and on hard
land. Before-and-after pictures for the Classic style. Check every build's log: errors, timing, pit checks, net sag,
survivors. Build, bulldoze, count what's left.

## Done means

- Every style is **A+** in the pictures: on a block, a big site and a hill. You judge them against named real places,
  and the lead will look at them too and send it back until it is true.
- Kerbs are everywhere they belong, the decals are confident and designed, and Classic still looks like today, only
  better.
- Every setting does something visible, and every text is in 12 languages.
- The inherited fixes are closed, the strip mall and mall district still look right, and the decal budget holds.
- Your runbook (`docs/runbooks/parking-looks.md`) has the references, the asset research, the kerb choice, and the
  lessons; `docs/GROUND-RUNDOWN.md` gets the general ones.
- The hand-over to P2-6 is written.

Don't stop at a milestone. Report at milestones, but keep going until every style is A+.
