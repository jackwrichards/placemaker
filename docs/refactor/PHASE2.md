# Phase 2: the artistic explosion (agent briefs)

The user's notes (`GRIPES.md`, 2026-10-30) and what Phase 1 built, turned into one brief per agent. The lead starts each as
its own session, reviews every report against the A+ bar below and sends it back until it is A+. Nobody stops at B.

## The agents and the order

Seven agents in two waves. One game serves every agent (the game queue), so at most four run at once.

| Wave | Agent | Owns | Why this order |
|---|---|---|---|
| A | **P2-1 Parking tools** | the parking tool's workflow, editing a placed lot, parity with Parking Lot Tool, *manual touches* (a core piece others reuse) | the other parking and shops work builds on it |
| A | **P2-2 Beaches** | the Beach category, every water edge it builds | independent |
| A | **P2-3 Farms** | the Farms category | independent |
| A | **P2-4 Parks, sports, industrial and rundown polish** | parks and sports on hills, paths, the sports park's plaza at size, the open industrial and rundown items | independent |
| B | **P2-5 Parking looks** | every kind of car park, decals, variety | after P2-1 (same files) |
| B | **P2-6 Shops and strip malls** | the Shops category's decal pass, its ponds and plazas, its parking | after P2-5 (reuses its lots) |
| B | **P2-7 Golf** | the golf course | when a slot frees; independent |

## The shared block (every brief includes it)

**Read first:**
1. `AGENTS.md`.
2. `GRIPES.md` (the user's own notes, every line).
3. `docs/DECALS-PLAYBOOK.md` and `docs/GROUND-RUNDOWN.md`: how to design a ground with terrain paint, surfaces and decals together, the brushes, adding decal kinds, and the lessons of every earlier agent.
4. `docs/ARCHITECTURE.md`, "Controls model": Type → Style → Settings → Add-ons, the Ground section, the dead-control test, requirements and size.
5. The runbooks in `docs/runbooks/` (yours, if it exists, and the nearest one).
6. `docs/RESEARCH-NOTES.md` and `docs/RESEARCH-NOTES-ADDENDUM.md` (engine rules).
7. Your category's `docs/refactor/<category>.md`.

**What Phase 1 already gave you** (don't rebuild it; use it, and check it holds on your types):
- **Controls:**
  - A look is a **setting**, presets set it, add-ons are only real facilities. To turn an add-on into a setting, register it in `LookSettings`.
  - Neglect is its own style, never a slider.
  - Animal spawns are gone.
  - Every shown control must change the plan (`ControlEffectTests`).
- **Panel:**
  - One Ground section (terrain paint, ground cover, shape, and the Decals switch). Every decal you lay checks `DressingControls.GroundDecalsOn`.
  - Requirements (an entrance) are checked before planning.
  - "Too big" warning (`SizeGuide`), the Real-preview note, and Edit on the click panel.
- **Removal:** clean on every type, 10–20× faster, Remove all / Ungroup / Cancel, and a Settings button that readies a city for removing the mod. Anything new you build must still bulldoze clean: build it, bulldoze it, count what's left.
- **Ground:**
  - Paths zig-zag on steep land.
  - Bridge ends join path ends exactly (end every bridge on a path end, and never leave a step between land and deck).
  - No fence around pits, piles never over a pit edge.
  - Parked cars sit on all four wheels.
  - Mowing stripes only on real pitches.
- **Decals:** a shared library (`Recipes/decals/library/*.json`) with a fixed budget for the whole city, brushes, boundary passes, and the decal browser page (https://claude.ai/artifact/BTMZX1HfRAhjkRHdCBzJCN). The library is examples, not a limit: add every kind you need (data only, near-zero cost). Show new kinds to the user in the browser, and keep within the shared memory budget (measure it).

**Stay in your lane:**
- Edit your own category's code, recipes, shelf file, lang files, runbook and docs.
- Shared code (`Core/Build`, `Core/Decals`, `Game/Build`, `Game/Decals`, `UI/src` outside your type's own controls, `Core/Tool`) only for a small, general fix that every type benefits from. Note it in your report.
- Anything bigger in shared code goes in `docs/refactor/PHASE2-CORE-REQUESTS.md` (what, why, which types), and you tell the lead. The lead decides who builds it. We don't want seven agents each inventing their own version of a core piece.
- P2-1 owns *manual touches* (see its brief); others use it once it lands.

**Push to the max:**
- The bar is **A+ in the game's pictures**, judged against real places (look at real references and name them in your runbook).
- Be bold:
  - move terrain confidently;
  - invent styles;
  - make new decal kinds;
  - use the game's vanilla assets you haven't used yet (`.forensics/suite` catalog, decompiled prefabs).
- Every idea in the user's notes is a floor, not a ceiling.
- Hard land is part of the job: test on a city block, 220×200 m, a big site, a 5–15 % slope, and hills or mountains (or a cliff or shore, where it applies). A place drawn on bad land either works and looks good, or the panel says so with a clear warning and the best-practice advice.

**Prove everything in the game:**
- Run `tools/suite/Show.ps1` through the queue as **background** commands with `-Who <your agent id>`. Never `-AllMods`, never `-Direct`.
- Quit the queue's game when your results are in (drop `{"command":"quit"}` in the newest `runs/queue-*/inbox` once `Queue.ps1` shows nobody in line).
- Before-and-after pictures for every change. Look at them yourself and read the logs: errors, timing, pit checks.
- Frame time and memory for anything big.

**Your report** (to the lead, at each milestone):
- For each style: pictures at 45°, top and close, on flat and on hard land.
- A self-grade per style, and the exact gap to A+.
- What you changed, the tests, the languages, and the open items.
- Being honest about weak spots is worth more than a good-looking report: the lead will look at the pictures anyway.

**Rules:**
- Every player-visible text goes into all 12 languages (`node tools/lang/check.js` at 100 %, 0 changed, 0 broken, then `--stamp`), plus `PM_ALL=1 node mock/run.js --test tests/i18n.spec.ts` for panel text.
- Hide, never delete (shelf).
- Run the tests you touch (the full suite takes 40+ minutes; run what you touch). Known failures before Phase 2: three sports park tests (`RespectsReservedSpace`, `EveryPresetPlansOneHectareAndSixHectareLCleanly`, `FieldsFillTheirSizedPlacesAndFaceTheTerrace`), which P2-4 owns, and the SiteHarmony industrial curtain checks.
- Commit per piece, with no attribution lines. Don't push or install (the lead does).
- If a game boot fails with "Out of memory" in Player.log, check free commit memory and report it; don't loop.
- Keep your runbook (`docs/runbooks/<type>.md`, from the template) as you go, and add your lessons to `docs/GROUND-RUNDOWN.md`.
- The user works over Remote Control and is not a programmer: plain, short answers, pictures via SendUserFile, questions in your closing message.

---

## P2-1 Parking tools: parity with Parking Lot Tool, and editing what was placed

**Goal:** the best parking tool in Cities: Skylines II. Nothing Parking Lot Tool (KruemelGames; source in `C:\Users\jack\ParkingLotTool`) can do is missing from Placemaker, and editing a placed lot is easy.

**From the user's notes** (a creator's review compared us with Parking Lot Tool):
- **No management tools:** parking fees can't be set here; the creator had to turn them off elsewhere.
- **Hard to modify once placed:** they couldn't move accessible stalls after placing, so they removed them all.
- **Adding entry points and aligning roads feels worse:** Parking Lot Tool snaps better to drive aisles and has a more see-through preview background.
- **Overlapping buildings** caused problems despite our anarchy features; Parking Lot Tool handled them better.
- The user: "We can't lose any features from that other parking lot mod." "User customization is key." "Maybe even letting them draw on the world." "Pick different angles, or where a certain thing lines up."

**Build:**
1. **A feature audit.** Read Parking Lot Tool's code and README, and list every feature, setting and behaviour (fees, stall kinds, angles, alignment, snapping, overlays, management, overlap handling, everything). Next to each, write what Placemaker has. Close every gap. Put the table in `docs/refactor/parking.md`.
2. **Parking fees and lot management:** set from the panel (and from the click panel of a placed lot if it fits), in the game's own terms.
3. **Manual touches (shared core; you own it).** A placed design can remember small hand edits so they survive Edit and rebuilds:
   - "these stalls are accessible";
   - "this stall removed";
   - "entrance here";
   - "aisle angle / alignment starts here";
   - "this row lines up with that kerb".

   Store them in the design as data (versioned; old designs load). The planner applies them after its own layout, and they stay valid when the outline changes or are dropped with a message. Expose them in the Edit stage as clicks on the world: select a stall, toggle accessible, remove, drag an entrance along the edge. Make it general so other types can use it later (a bench removed, a tree kept). Document it in `docs/ARCHITECTURE.md`.
4. **Entrances and alignment:**
   - entrance points snap to drive aisles and street lanes as well as Parking Lot Tool's do;
   - roads align cleanly;
   - the player can pick the angle (0°, 30°, 45°, 60°, 90° and free) and what the grid lines up with (the street, the longest edge, a chosen edge).
5. **A see-through preview** in the planning stages, so the land and what's under the lot stay readable (match or beat Parking Lot Tool's).
6. **Overlaps:** a lot drawn over existing buildings, props or nets behaves predictably. Say clearly what will be replaced and what will be kept, with an option. Match Parking Lot Tool's handling.
7. **Drawing on the world:** let the player sketch where aisles, islands or walkways go, if it can be done cleanly with the area tools (`docs/AREA-TOOLS.md`). If it can't, explain why and propose the closest thing.

**Done:**
- Every row of the audit table is closed or deliberately declined with a reason.
- Manual touches work in the game (place, edit, rebuild, reload a save, the touches still there).
- Fees work.
- Pictures of the new workflow.

Coordinate with P2-5, which starts after you. Write in your report exactly what it can build on.

---

## P2-2 Beaches: a complete overhaul

**Goal:** every beach style an A+. The user grades the best types B+ and the worst D today. Beaches are where the decal library shines (shells, wet sand, wrack lines, towels, toys) and where terrain has to be bold.

**From the user's notes:**
- "The terrain needs to be more aggressive and confident with moving terrain around to make things look good."
- The player might draw a beach on a cliff: warnings must say when it won't work or won't look good, with best practice for the selection.
- New ideas:
  - a mulchy beach;
  - a Tahoe-style rocky shore;
  - an extreme rocky ravine (barranca);
  - a marshy beach with a boardwalk through the marsh;
  - a cove or harbour where rock juts out into the water with a ton of rocks.
- Decals unlock it: shells, beach towels, beach toys, sand pits, sand runoff. "Your imagination should be exploding here."
- **Bridges** going into the water or out to an island sometimes don't join (the overhang glitch). Phase 1 fixed the mechanism (bridge ends merged onto path ends; see `docs/GROUND-RUNDOWN.md`). Prove it on every beach and marsh style with a **close-up picture of each joint**: Phase 1 only proved it in the log.
- **Never quays** on any water edge: boardwalks, piers and banks instead (user memory).

**Build:**
1. Re-grade every current beach style in the game (flat shore, steep shore, cliff, lake, river, sea). Keep what's good and rebuild what isn't.
2. **Terrain:**
   - real beach profiles: berm, foreshore, dunes with blowouts, rock platforms, coves cut into the shore;
   - carving and building up with confidence;
   - water depth that reads right.
3. **New styles:** the five ideas above and more of your own (a shingle beach, a dune system with fences and marram grass, a family beach with facilities, a wild beach). Each one has its own ground design: terrain paint, surfaces, decals.
4. **Decals:**
   - shells and pebbles along the strandline;
   - a wrack line of seaweed and driftwood;
   - wet and dry sand bands that follow the waterline;
   - sand blown onto paths and car parks;
   - footprint trails to the water;
   - towels and toys near facilities;
   - sand runoff down slopes;
   - tide pools on rock.
5. **Warnings and best practice** for cliffs, no water, too short a shore, too steep.

**Done:** every style A+ on a real shore, on a cliff case, and on a lake. Joint close-ups proven. Nothing ever stands in the water that shouldn't.

---

## P2-3 Farms: a complete overhaul

**Goal:** farms that look like real working farms. Detailing and decals they've never had.

**From the user's notes:**
- Traditional farms come **first** in the list, before animal steads.
- "Think of what farms have: barrancas (ravines), mulchy areas, forests, orchards, ATVs, constructed areas, big water holders, a homestead, all parameters."

**Build:**
1. **Order:** traditional arable and mixed farms first on the shelf (`Recipes/shelf/farms.json`), animal types after.
2. **The farm as a place:**
   - a homestead (house, yard, sheds, machinery, a muddy farmyard);
   - fields with headlands, tramlines and wheel ruts;
   - orchards in rows;
   - woodland edges and shelterbelts;
   - a ravine or drainage ditch that follows the land;
   - a pond or big water tanks;
   - mulch and manure heaps;
   - stacked bales;
   - fences and gates where tracks cross;
   - tracks worn into the ground;
   - ATVs and machinery parked where they would be.
3. **As settings, not add-ons:** orchard share, woodland, ravine or ditches, water (none, pond, tanks), how tidy or worked the yard is, the farm's crop mix. The styles preset them. Real facilities (a farm stall, an apiary) stay add-ons.
4. **Ground:**
   - terrain paint for fields and their seasons;
   - surfaces only for yards and hardstanding;
   - decals for mud at gates and troughs, ruts, spilt straw and hay, dung, puddles in the farmyard, tyre tracks out onto the road.
5. Hard land: farms on slopes (contour fields, terraces), rough ground and a riverside.

**Done:** every farm style A+, traditional ones first, on flat, a slope and big sites.

---

## P2-4 Parks, sports, industrial and rundown: the polish pass

**Goal:** take the categories earlier agents made to A+ where the user still sees problems.

**From the user's notes:**
- **Hills:** "I really want to see a wider test on parks for hilly areas … If you put them on a massive hill or rugged area, it shouldn't break." "I'm seeing certain pathways not work … smoothing the paths out, not having them double back on each other and snap." Phase 1 made paths zig-zag on steep land; prove it across every park type and style on hills, mountains and rugged land, and fix what's still wrong (snapping, doubling back, path ends, steps).
- **Sports parks:** some are great, but they lack **paths to the different facilities** (add a sensible path network). The **sports park's plaza doesn't grow** when the park is drawn very big (it must still look good at any size).
- **Industrial:** quarries and holes have **no fence around the hole** and rubble **never sticks out over the edge**; Phase 1 fixed this, so verify it on every pit type. Construction-site barrier flags along pit edges: decide with the user. **Floating cars** in car parks (fixed in Phase 1; verify). The **active landfill** could be better in places.
- **Rundown:** the same border-around-the-hole problem; verify it.
- **Areas within a park:** "Rocky outcrops shouldn't just be rocks placed in places … I want a whole rocky area or a whole open clear area, like different biomes within a park if it's big enough … I want lake add-ons … more parameterized things rather than just switches." Phase 1 turned rocky outcrop, shore and pond into settings (rocky areas; water: none, pond or lake). Make them look great: real rock zones with outcrops, scree and boulders; open clearings; lakes with shaped shores. A big park becomes several areas, each with its own character, sized by the settings.
- **The decal-memory budget** across categories (nature nearly filled it): measure with all categories at once, and slim it if needed.
- **The three failing sports park tests** (`RespectsReservedSpace`, `EveryPresetPlansOneHectareAndSixHectareLCleanly`, `FieldsFillTheirSizedPlacesAndFaceTheTerrace`): fix the cause or the test, with a reason.

**Build:**
1. A hill matrix: every park and sports style on a 10 % slope, 20 % hills, mountains and rugged land. Fix every break.
2. Sports park paths and a plaza that scales with the site.
3. Industrial and rundown: pit checks on every type, the landfill polish, and whatever else the pictures show.

**Done:** the matrix is all A+ (or warned clearly where a type truly can't work), the user's listed items are closed, and the tests pass.

---

## P2-5 Parking looks: every kind of car park

**Starts after P2-1 lands** (read its report and `docs/refactor/parking.md` first).

**Goal:** car parks that are a joy to look at. "We need to go completely all out on parking lots." A plain lot stays an option, but it is detailed and looks good.

**From the user's notes:**
- **Lots from around the world:** Japanese, German and more. Look at real references and at the vanilla assets available.
- **Districts:** a rich neighbourhood's lot (bright, fine paving, still skid marks), a nice district, a bad district, a rundown lot.
- **Rundown cars:** parked in spots that aren't real stalls, next to real ones (a setting).
- **A homeless camp** (a setting or add-on).
- **An industrial area built in** (under construction), unpaved areas.
- **Layout:** medians, walkways, grass or no grass, special islands.
- **Shapes:** round car parks, terraced car parks that work cleanly on hills.
- **Equipment:** parking signs, parking meters at the entrance, solar shade canopies.
- **Decals:** cracks, oil, skid marks, dashed and dotted crossings for people, worn lines, puddles, kerb grime.
- **Water:** barrancas (dry ravines) and storm drains.
- "Grass areas could be nicer." "The plazas look so nice, draw inspiration from them."
- "Make sure these agents are A+ … don't let them quit until it's perfect." "User customization is big here too."

**Build:**
- Styles for each of the above, each a complete ground design (terrain paint, surfaces, decals), with settings for everything the player would want to tune.
- Use P2-1's manual touches and alignment.

**Done:** every style A+ on a block, a big site and a hill, and the plain lot looks great too.

---

## P2-6 Shops and strip malls: decal pass and the weak parts

**Starts after P2-5** (reuse its lots and decals).

**Goal:** the user grades Shops B− today, and wants A+.

**From the user's notes:**
- They need the decal pass.
- "I don't love how the ponds and plazas work. It's just kind of half-assed."
- "The whole parking situation is a little half-assed": take it from the parking agents' work.

**Build:**
- Weathering and ground design for every shops type: shopfront wear, trolley bays, loading docks, bins and grease, gum, kerb grime, lot wear.
- Rebuild the pond style and the plaza parts of strip malls to the Plazas category's standard (`docs/refactor/squares.md`, `docs/runbooks/plazas.md`).
- Parking from P2-5's styles, fitted to each shops type.

**Done:** every shops type and style A+ on a block and at the sizes the strip-mall work covers (`docs/refactor/STRIP-MALLS-PLAZAS.md`).

---

## P2-7 Golf: a course that's amazing

**Goal:** golf courses that look like real courses (code in `Core/Layers/Types/Golf/`).

**From the user's notes:**
- "Maybe he could use terrain paint rather than surface tooling and decals to make everything look awesome."
- A clubhouse building where you park.
- Settings:
  - a car park or not;
  - how many holes, and how many will fit;
  - and more.
- "Work with the terrain more." The player might paint it over a really rocky area: it needs to be good there.
- "This creativity needs to explode here as well."

**Build:**
- **Holes as designed landscapes,** each with tee, fairway, rough, greens, bunkers, water hazards, and cart paths.
- **Shaped with the terrain:** mounding, a raised green, a sunken bunker.
- **Painted with terrain paint:** fairway and rough bands, mowing patterns on the greens and fairways.
- **Decals:** divots, bunker rakes, worn tees, cart-path wear, ball-mark spots.
- **The clubhouse with its car park and practice areas** (driving range, putting green).
- **Settings for:**
  - the number of holes (planned to fit, with the panel saying how many fit);
  - style (links, parkland, desert, mountain);
  - water;
  - the car park.
- Rocky or steep land handled or clearly warned.

**Done:** every style A+ on a big flat site, a hilly site and rocky land.

---

## How the lead shepherds

- Start wave A as four sessions, from master. When one finishes, review its pictures yourself (montage the 45° and close views) against the A+ bar, and send it back with specific gaps until it is A+. Only then merge, run the touched tests, show the user and start the next agent.
- Handle `PHASE2-CORE-REQUESTS.md`: decide whether a request is a small shared fix, waits for a later agent, or needs its own core session.
- Keep the shared decal budget in check as styles are added.
- **The larger UI rewrite** the user mentioned ("might be needed for some of these core fundamental changes"): collect what the agents need from the panel in the core-requests file. If it adds up to a rewrite, propose it to the user as its own session between the waves.
- Install into the user's game only when the user asks.
