# Sports: the category agent's decisions (rf-sports, 2026-10-16)

The user's grade: **D**, "weird, but I like how they're unique". Brief: don't cut much; cut only what is bad **and** not
unique; make the rest work cleanly.

Sports holds three types: **Sports park** (`sportsPark`), **Golf** (`golf`) and **Urban ruins** (`urbanRuins`, the skate park
with a broken highway). Shelf: `Recipes/shelf/sports.json`.

All judgements below come from the game runner in the clean profile (the game's own assets only), plus the gallery
renderer for layout. Run ids are under `.forensics/suite/runs/`.

- BEFORE: `show-19-builds-1016-062550-d800` (every type and style, 45° + top + plan + terrain, flat 150 x 140 m block).
- AFTER: `show-17-builds-1016-081020-8a4f` (the same request for the kept styles, commit `3163571`).

## Decisions

| Type | Style | Before | Keep / hide | Why |
|---|---|---|---|---|
| Sports park | community | D | keep | The everyday sports park. Broken before: pitch without lines, courts plain grey slabs, tennis "nets" were red benches. |
| Sports park | stadium | F | keep | Unique (running track with a pitch inside, stands, floodlights). Broken before: on a city block the track never fit, so it was a community park with a car park. |
| Sports park | tennisClub | D | keep | Unique (a club of tennis blocks). Broken before: bench "nets", garden picket fences, no lines. Now on clay. |
| Sports park | campus | F | **hide** | Not unique (community + baseball + volleyball + skate) and bad: on a 1.5 ha block five of its facilities never fit (baseball, skate bowl, volleyball, fitness, playground). |
| Sports park | neighbourhood | D+ | keep | Street sports without a big pitch: courts, skate, table tennis. Distinct from community. |
| Golf | parkland | C+ | keep | Reads as golf: fairways, greens, bunkers, ponds, trees. |
| Golf | links | C+ | keep | Treeless, gorse and deep bunkers, out-and-back routing: a different look from parkland. |
| Golf | pitchPutt | C | keep | The small-course format that fits a city block; short holes. |
| Golf | miniGolf | F | keep | Unique. Broken before: every lane was a white slab (the felt drew under the kerb). |
| Golf | drivingRange | D | keep | Unique (tee line, target greens). Broken before: the target greens vanished under the range's grass, only their bunkers showed. |
| Golf | discGolf | C | keep | Unique woodland course with tee pads and baskets. |
| Urban ruins | diySpot | D | keep | The skate spot. Before: two huge bright white slabs with a dozen identical boxes. |
| Urban ruins | highwayRuins | D- | keep | The most unique style. Before: white slabs, yellow no-parking hatching on the "carriageway", nothing that said highway. |
| Urban ruins | graffitiPark | F | **hide** | Not unique (the DIY spot plus a wall row) and bad: its point, graffiti, needs decal packs; without them it was one bare white slab. |
| Urban ruins | parkour | D | keep | Block field on gravel: different from the skate spot. |
| Urban ruins | dirtJumps | D | keep | BMX lines are unique. Before: the jump mounds were built but painted "dry grass", so the track was invisible. |

## What changed (all seen in the game runner; see the runs below)

Iterations in the game runner (clean profile, flat block unless said): it1 `show-17-builds-1016-064719-4f1a`,
it2 `show-17-builds-1016-070443-c8ac`, then the AFTER run and the land variants listed at the end.

**Sports park**
- Tennis courts (every type that builds them): the game's `TennisNet01` across each court, `TennisBench01` by the net and
  `TennisUmpireChair01` at the other post, a chain-link cage (`FenceIndustrialHigh01`) instead of a garden picket fence,
  and the game's own court decal (`TennisCourtDecal02`, orange court with white lines) on each court when no line-piece
  pack is installed (with one, the lines are painted piece by piece as before). Seen in it1/it2: orange courts with
  nets, chairs and cages.
- Basketball courts: the game's `BasketballCourtDecal01/03` (blue and yellow court) named explicitly. Before, the decal
  was an automatic ref of a category without an automatic pool and was dropped on every build. Seen in it2.
- Pitches of at least 66 x 50 m: the game's `FootballFieldDecal01` when no line pieces are installed. Seen in it1: a fully
  lined pitch in the community park.
- Stadium: the running track is the hero again on a city block. The track ladder ends in a compact oval (84 x 50 m, bends
  shrunk to the place), the stadium's own pitch is only the stand-in when no track fits (a pitch is drawn inside the
  track), and the stadium uses a single tree belt and 95 % breathing room. Before: no track on any block (gallery: "no room
  for running track" on 1.5 ha); it1: a sand-coloured oval with goals in the infield.
- Tennis club on clay: new host option **Court surface** (hard / clay / grass, `tennisSurface`), passed to the courts as a
  place tag; the tennis club preset is clay (sand under the orange court decals). Seen in it2.
- Bike racks at the entrance plaza are single racks (`BikeRack01`), 1.9 m apart. Before, the automatic bike pool gave six
  overlapping bicycle shelters on every build (the build kept them only by anarchy).
- Skate bowls: dug on purpose (`intentionalPit`); the pit guard had raised the bowl back to flat ground ("pit guard: raised
  40 texels … deepest bowl 2.00 m"). Obstacles from a mix of the game's ramps, boxes and rails instead of one box repeated.

**Golf**
- Mini golf: the kerb is four strips round the felt (not a slab under it: the concrete drew over the felt and every lane was
  white), lanes grow with the site (up to 1.6x) and the preset plants fewer random trees. it1: green felt lanes in white
  kerbs.
- Driving range: each target green sits in a sand collar and is drawn with the overlay copy of the grass; before, only the
  bunkers showed.
- Parkland, links, pitch & putt and disc golf: unchanged; they read as golf in every run.

**Urban ruins**
- Ground: the ruins' own surfaces (weathered asphalt when the EAI pack is there, else `Pavement Surface 01`; cracked asphalt
  or `Oil Surface 01` for the fallen carriageway). Before: `Concrete Surface 02`, a bright white slab.
- Skate pads are filled to their size (about one obstacle per 100 m²) in up to four lines, ramps at the ends, boxes and
  rails between, and a half-pipe as the centrepiece of a big pad. Before: a dozen identical boxes on a 120 m slab.
- Highway: the fallen carriageway gets broken runs of jersey barriers along both edges and the game's road arrows and speed
  numbers; the old marking pool painted yellow no-parking hatching. (Abandoned cars were tried: the game has no vehicle
  props, the build skipped them.)
- Dirt jumps: the BMX lines are painted dirt at full strength. Before they were painted "dry grass" (layer 1) at half
  strength and the track was invisible. Seen in it2.
- DIY bowls dug on purpose (the pit guard filled them in).
- Parkour keeps a smaller pad (20 %) and no skate fill.

## Shared code touched (kept minimal)

- `Core/Layers/AddOns/Fields/*` (the sports-field add-ons, also used by parks' amenities): the fixes apply everywhere a
  field is built, as the brief asks ("fix every bug everywhere it occurs").
  - Tennis: the game's tennis net, tennis benches and umpire chair by default (the automatic tennis pool starts with a
    bench, so every net was a bench); a chain-link cage instead of the automatic garden fence; a host can ask for clay
    or grass through a region tag (`courtSurface:clay`).
  - Whole-field marking decals of the game (`TennisCourtDecal02`, `FootballFieldDecal01`, `BasketballCourtDecal01`)
    named explicitly. The basketball decal used an automatic ref of a category without an automatic pool, so it was
    dropped on every build; tennis and pitches had no lines at all without a line-piece pack.
  - `FieldAddOn.Build`: a host-sized place smaller than the smallest format gets the smallest format laid out to the
    place (only the sports park's compact running track asks for that).
  - `SkateBowlAddOn`: sets the plan fact `intentionalPit` when it digs its bowl (the core pit guard reads it), and mixes
    the game's skate props when the player chose none.
- `Core/Layers/Types/UrbanRuins/DirtJumps*` (also the "dirt jumps" feature of other types): dirt paint (layer 3, was 1 =
  dry grass) at full strength; the DIY bowl sets `intentionalPit`.

## Runs

| What | Run |
|---|---|
| BEFORE, every type and style (19 builds incl. the three defaults) | `show-19-builds-1016-062550-d800` |
| Iteration 1 | `show-17-builds-1016-064719-4f1a` |
| Iteration 2 | `show-17-builds-1016-070443-c8ac` |
| AFTER, the kept styles (17 builds), same request as BEFORE | `show-17-builds-1016-081020-8a4f` |
| Golf on 260 x 190 m (parkland, links, pitch & putt, disc golf) | `show-4-builds-1016-081029-a507` |
| Mini golf and driving range on a small 90 x 70 m site | `show-2-builds-1016-081029-84d8` |
| Sports parks on an L, 170 m, 5 % slope | `show-4-builds-1016-081029-cda5` |
| Urban ruins on a pinch, 180 x 110 m, 6 % slope | `show-4-builds-1016-081029-1465` |

In the AFTER run every build is built (17 of 17, 0 step errors, no exceptions in Player.log). The six bicycle shelters kept
by anarchy on every sports park are gone; the pit guard no longer fills in skate bowls.

## Done

- **Kept (14 styles):** Sports park community, stadium, tennisClub, neighbourhood; Golf parkland, links, pitchPutt,
  miniGolf, drivingRange, discGolf; Urban ruins diySpot, highwayRuins, parkour, dirtJumps. All three types stay.
- **Hidden (2 styles, code kept):** `sportsPark/campus` (not unique, five facilities never fit a city block) and
  `urbanRuins/graffitiPark` (not unique, its graffiti needs decal packs; a bare white slab without them).
  `Recipes/shelf/sports.json` hides exactly these two and no types.
- **Changed:** tennis nets/chairs/cage and the game's court decals; basketball and pitch decals; stadium track fits a
  block; tennis club on clay (new `tennisSurface` option); single bike racks; skate and DIY bowls dug on purpose;
  skate pads filled with mixed ramps/boxes/rails and a half-pipe; ruins on dark pavement with jersey barriers and road
  arrows; dirt-jump lines painted dirt; mini golf kerb strips on bare ground; driving range targets in sand collars.
  Commits `93f1ca9`, `00d963a`, `56f797a`, `3163571`, `ec9a342` (details above).
- **Runs:** BEFORE `show-19-builds-1016-062550-d800`, AFTER `show-17-builds-1016-081020-8a4f` (17 of 17 built, 0 step
  errors). Pictures, e.g.:
  - `.forensics/suite/runs/show-17-builds-1016-081020-8a4f/shots/03-sportsPark-stadium.05-45-clean.png`
  - `.forensics/suite/runs/show-17-builds-1016-081020-8a4f/shots/10-golf-miniGolf.05-45-clean.png`
  - `.forensics/suite/runs/show-17-builds-1016-081020-8a4f/shots/15-urbanRuins-highwayRuins.05-45-clean.png`
  - the matching shots under `show-19-builds-1016-062550-d800/shots/` for the before.

## Not done / known problems

- **Last commit not run on its own:** every AFTER and land-variant run is labelled `3163571-dirty`; `ec9a342` (mini golf
  on bare ground, compact basketball decal) was committed after them and has had no run at its own commit.
- Stopped early for the wrap-up (the shared game queue made each request wait about 45 minutes); no further iterations.

- **Highway ruins still has no deck in the air.** The highway is told by pillars, a fallen carriageway with jersey
  barriers and road arrows, and skate pads where the deck would stand. A real elevated deck would need an elevated net
  (a disconnected highway or path viaduct); not tried.
- **Stadium on an L-shaped or narrow site:** the track does not fit and the stadium falls back to a pitch (plan issue "no
  room for running track"). Seen in `show-4-builds-1016-081029-cda5`.
- **Stadium infield:** a compact track's infield is too narrow for the game's pitch decal: goals, no lines (in the clean
  profile; with a line-piece pack the lines are painted).
- **Pitches smaller than 66 x 50 m** (5-a-side) have no lines in the clean profile (no decal of that size in the game).
- **No stands:** the game has no grandstand props; stands are benches on the terrace.
- **Dirt jumps and the highway embankment** still log "terraform incomplete" (the mounds do not reach their full height
  in four passes); present before too.
- **Urban ruins on big sites** leave much empty lawn round the central band.
- **Mini golf** is still rows of lanes along one walk (bigger lanes, bare ground under the felt); no themed layout.
- Everything was judged in the clean profile. The user's own asset packs (line pieces, EAI surfaces) take the other
  branch of several fixes (painted lines instead of the game's court decals, weathered asphalt instead of
  Pavement Surface 01); that branch was not run (`-AllMods`).
- Not installed in the user's game: the branch is `rf-sports`, for the integrator to merge.

## Sports parks, round 2: designed grounds (2026-10-29, branch claude/sports-parks-explode)

The user: "we just make amazingly detailed plazas by looking into how ground work works and looking into more assets
in the game ... let's now channel this effort into making sports parks just as amazing ... explode the box". Same
recipe as the designed plazas: the plaza levels kit for the ground, the game's own props, judged in the game runner.

**The game's own sports props (they were all there).** The clean game ships real grandstands, dugouts, scoreboards,
full-size goals and 22 m light masts, catalogued as "decoration", so no automatic pool ever dealt them: every "stand"
was a garden bench and every "floodlight" a 3 m park lamp. Now named explicitly (`Core/Layers/AddOns/Fields/SportsProps.cs`):
`SportParkStandSmall01-03` (19-21 m long, 8-14 m deep), `SportParkStandLarge01-04` (22 or 41 m, 24 m deep, 11-16 m high,
covered), corner pieces, `LightpoleIndustrial03/04` (22 m masts for fields), `LightpoleCommercial03/04` (11 m for
courts), `SportParkBench02` (covered dugout), `SportParkFootballGoal01/02`, `SportParkAmericanFootballGoal01`,
`SportParkScoreboard01`, `SportParkFlag01` (corner flags), training cones, hurdles, poles and dummies. A stand looks along
its −Z (game runner: turned along +Z they faced away from the track). Stand rows are laid end to end, long pieces first,
centred, each piece's footprint taken as a polygon (as a corridor the next piece of the row was always refused).

**Shared fixes (every field, every type that builds one):**
- Pitch markings: `FootballFieldDecal01` is HALF a regulation pitch (68 m across, 52.5 m from halfway to the goal line).
  Stamped once in the middle and turned a quarter, every pitch since rf-sports had its penalty box on a touchline. Now two
  halves on a full-size pitch only (the lines do not scale; smaller pitches stay unlined in the clean game).
- Goals: the game's full-size goal on 11/9-a-side pitches, the small one on small pitches, H-posts for gridiron and rugby
  (the automatic goal pool dealt 2 m mini goals and ice hockey goals onto full pitches); corner flags; a scoreboard.
- Dugouts instead of four garden benches; stands with their front row 1.5 m off the touchline as deep as the place
  allows (a row of sports benches when no stand fits); real masts; a show court in a host's stands has no cage.
- Hosts can seat and light a field themselves: place tags `hostStands`, `hostLights` (FieldAddOn).
- Sports park (classic composer): the spectator terrace is one levelled platform (FieldGround) with the game's
  grandstands on it facing the hero field (covered grandstands on a big stadium site), deep enough for a small stand.

**SiteHarmony on rough ground (the failing `CoreTypes_OnRoughGround_PlayNice(sportsPark)`).** The spectator benches stood
in a row on the long paved terrace. A paved surface over 45 m long or 2000 m² is smoothed by the harmony pass (bumps out,
the hill kept), never planar-fitted, and benches on it are not levelled as free furniture: on the test's rough ground two
of them stood at 12-13.5 % (limit 12 %, one allowed per 20). A spectator terrace is level in the real world: it now is
(see above), and the test passes without any change to it. The four other failing cases (`AllTypes_OnBumpyHill_With
TerrainFeature_PlayNice` for carYard, junkYard, industrialWorks, containerYard) fail on master too (master export run,
2026-10-29), not touched.

**Ground kit, generalised (`PlazaDesigns.Kit.cs`, `Levels`, shared, plazas unchanged):** `AddFloor` (a level floor at an
absolute height, its banks blending from whatever ground is under it: sports floors are level on a slope too),
`FloorBank`/`RimRange`, `LevelBase`, `Skirt` (along the box's edge the base plane's residual is taken out, so a design on
hills meets the ground as it is), `FineCorners` (non-planar quads as four triangles round their true centre, and a cut
every 8 m: two crossing banks in one triangle came out at 56-80 %). `Kit.TurnQuarter` (the design's long axis on the box's
long side). `Occupancy.TakePolygon`.

**Five new styles (designed sports grounds, `Core/Layers/Leisure/SportsDesigns.cs`, subtype = style id):**

| Preset | What it is |
|---|---|
| arena (Stadium bowl) | A pitch sunk 1.8 m (2.6 m on a big site) into a bowl: grandstands on all four sides (covered 41 m grandstands on a big site; small stands on a block), 22 m light masts in the four corners, stone stairs from the bowl up to concourses of food stalls, kiosks and vending machines, tall flags, scoreboards on the end banks, walled banks with a plinth and a railing. On a slope the bowl is dug into the hillside. |
| centreCourt (Centre court) | A tennis show court at the bottom of an amphitheatre: 4-8 rows of seating steps on all four sides (a stone lip on every tread, benches on every other), stairs down, a railed promenade with tall flags and masts, practice courts on clay in chain-link cages, a pergola members' terrace with tables, on a big site a grass hill with trees and benches. |
| hillside (Terraced ground) | Two to three level terraces 2.4 m apart (the ground's own rise on a slope, up the slope): a pitch, a block of tennis courts, basketball cages and a skate bowl each on its own terrace, walled banks with three stone stairs each, seating steps on the bank above the pitch, a walk along every bank top, a pergola cafe on the top terrace. |
| cageCourts (Cage courts) | A grid of chain-link cages on dark asphalt (five-a-side pitches and basketball courts), 11 m masts on the shared cage corners, small stands in the wide alley, a raised level viewing deck with a stone plinth, a railing, a wide stair, cafe tables under parasols and food carts, vending machines along the grid. |
| athletics (Athletics park) | A running track (400 m on a big site, compact on a block) on a level floor, a grandstand along the home straight (covered on a big site), small stands opposite, light masts round the oval, a boulevard of flags between lime trees to two "Olympic flame" fountain drums, a warm-up lawn of hurdles and a sand pit with training dummies. |

A site too small or too narrow for a design falls back to the classic sports park (the stadium bowl on the 77 m arm of the test L; in the game runner's L it builds a five-a-side bowl). The leftover of an L becomes the plaza kit's railed gardens. Every design sets `ownGround` (the
harmony pass leaves its banks alone). Tests: `SportsDesignTests` (every design designed on block / big / L / slope / hills,
fields laid by the add-ons, plan time; every bank under the sheet grade at the heightmap's 1.75 m; every level change has
stairs; the bowl has grandstands and four masts).

Game runs (clean profile): first look `show-10-builds-1029-085523-03cc` (new styles, block + 220 x 200),
`show-10-builds-1029-090236-87c5` (new styles, L + 5 % slope), `show-10-builds-1029-090630-0d51` (classic styles,
block + 220 x 200), final `show-40-builds-1029-091028-dfbc` (40 of 40 built) (every shown style on block, 220 x 200, L, 5 % slope).

Weak points: pitches smaller than full size have no lines in the clean game (no decal that size); the classic styles
(community, tennis club, neighbourhood) are better dressed but keep their composition (a park with fields in it); the
stadium bowl falls back to the classic park on a very narrow L arm; on rough hills (±3 m every 30 m) the terraced
ground's outer banks reach 65 % in one spot (test case `hillside/hills` fails); the corner stand pieces are not used
(their footprint was not verified); not installed in the user's game.
