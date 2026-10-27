// AI builder (docs/AI-BUILDER.md): the design guide attached to the agent's system prompt. Condensed from what the
// premade types' planners encode and what the user graded (docs/ai/handbook.md has the sources, file by file). Written
// for the AI, in its tools' words. Changing it changes every build: rate a round before and after (suite/scenarios/ai-*).

namespace Placemaker.Core.Ai
{
    public static class AiHandbook
    {
        public const string Version = "handbook-11";

        public const string Text =
@"# Design guide (what good builds in this game look like)

Numbers are starting points for a typical site; scale them with the site and the request. The player's words always win.

## Every build
- One readable idea and a focal point: a monument at the end of an axis, a summit plaza, a fountain where paths meet, a hero field facing its spectators, a hub at the gate. Keep clear space around the focal point (a fountain about 11 m, a playground 20 m, a picnic group 13 m) so it reads.
- Ordered structure with a little natural variation. Perfect grids look mechanical; random scatter looks like garbage. Lines and rows get small jitter (position about 0.15 m, angle a few degrees, an occasional gap); groves and meadows are irregular but grouped.
- Never let two props' boxes overlap: the game hides one of them. Never block an entrance with a fence or a tree. Everything people use must be reachable by a path.
- Assets that do not render: vanilla props named Pile*, props ending in Random01 (blue placeholders), 40 ft containers (use two 20 ft). Do not use farmland crop textures outside farms.

## Entrances and paths
- Entrances by area: under 0.25 ha 1, under 1.5 ha 2, under 4 ha 3, under 10 ha 4, larger 5. Put them where people come from: the streets around the site, corners, crossings. Each path comes straight in from the street edge, then joins the network; the main entrance may get a wide walk with a pair of planters and lamps.
- Network: link the entrances by the shortest network first, add a loop for coverage, then at most one shortcut. Keep paths at least 5 m (better 12 m) inside the outline, parallel paths at least 8 m apart, branches at 30 degrees or more.
- Curves are gentle sideways waves (about 5 m over 30-35 m in a natural park, tighter in a garden), not zig-zags or chains of kinks. Use enough points or smooth: true.
- Grades: walking paths at most 8 %, hill trails about 9 % (up to 20 % for hiking), waterfront walks 2 % or less. On a slope run paths along the contours with round hairpins.
- Bad: paths that go nowhere or stop in the grass, a wide walk on a hill or in nature (it reads as a road), a path hooking round a summit instead of arriving on it.

## Planting
- Trees in groves, never in grids or a ring along the boundary. A natural park: groves over about 28 % of the area, radius 12-30 m, ragged outlines, at least 10 m between groves; of all trees about 55 % in groves, 30 % in an edge belt about 8 m deep, the rest as solitary feature trees.
- Spacing (by crown, not trunk): trees in a grove about 6-10 m, along a path 8 m, solitary trees 14 m apart; bushes 3-4 m. Trees at least 4 m and bushes 2 m inside the outline.
- Leave a central clearing (an open lawn at least 12 m across, up to 60 m) that no grove covers.
- Trees per planted hectare: natural park 26 (bushes 65), garden walk 22 (bushes 95), urban pocket 45, sports park 25, wetland 60, campground 110, greenway 110, golf parkland 140, forest reserve 260, hiking forest 300; heath 20 trees with 340 bushes.
- Lines of trees: street-tree rows 5 m in from the edge every 9 m; allees every 9 m; avenues along a path. One species per line.
- Flowerbeds in massed beds, one kind per bed, never scattered like confetti. Do not bury an axis under trees.

## Furniture
- Benches about every 25 m of path (every 14 m in an urban park), lamps about every 17 m, a bin per 90 m. Alternate sides; set each back half the path width + its own depth + 0.2 m, facing the path (or a view). At least 14 m between benches, 10 m between lamps.
- Plazas: furniture 7 m clear of entrances, 0.65 m off the edge; 12 m clear around a fountain.
- Furniture never on slopes over 12 %.

## Ground
- Parks: a grass base (paint lush grass or a grass surface) under the whole park. Nature: no lawn; forest-floor style patches (dirt or dry grass paint, radius 8-15 m, soft ragged edges). Beaches and greenery: paint with soft edges; a hard-edged neon grass rectangle looks wrong.
- Hills are one smooth landform: build them from sloped patches (corner_heights) or many thin rings with small steps, never a stack of tall flat steps. Max slope 30 %, 25 % within 15 m of the outline; trees below about 65 % of the peak height. Typical hills: 8 m mound, 12 m kite hill, 18 m lookout.
- Levelled pads: parking 2 % fall, sports fields level with an apron 2.5-7 m, farm yards under 15 %. Never leave a pit or a step next to a road; blend every shaped patch into the ground around it.
- Paint is permanent in the city, so paint only what the design needs.

## Water
- A pond: water level about 0.5 m below its lowest shore, the basin 3 m deeper. Without water it reads as a dry pit. Ponds at least 6 m from the outline, 3 m from paths; reeds in a 4 m belt at the edge; boardwalks level, about 0.6 m above the water.
- Beaches: sand from under the water to 40-60 m inland at about 5 %, a wet band, dunes or a promenade behind; umbrellas between the wet band and the path.
- Waterfront: one smooth walk with radius 40 m or more, railings only where the drop is over 1 m.

## Types players ask for (what makes them right)
- Park: grass base, curving paths from the street entrances, groves around a clearing, furniture along paths; often a playground, a picnic lawn, restrooms, flowerbeds. Garden walk: meandering paths with flowering borders. Urban pocket: spoke-and-ring paths, a central paved plaza (about 9 % of the area) with a kiosk, a street-tree belt. Picnic: a perimeter loop, open lawn, shade clumps, picnic groups of 3 tables with a shelter.
- Botanical garden: narrow meandering loops, each lawn between paths one themed collection, glasshouses beside the main path, a low hedge boundary, flower beds of one kind each.
- Memorial park: a long axis from the main gate to one big monument on a paved forecourt, allees every 9 m, mirrored groves, hedged rooms, bollards, flags and plaques; no trees on the axis.
- Playground: a paved parents' hub in the middle, play zones ring it (youngest nearest) with clear sightlines, items about 4-6 m apart on sand, rubber or bark, toddler zone fenced, benches round the hub.
- Zen garden: a stroll loop inside a wall or bamboo, stepping stones 0.9 m apart, odd-numbered rock groups on moss, raked gravel, a few pruned trees and lanterns, maybe a pond with a bridge.
- Amphitheatre: a stage at the focus, curved stepped seat rows facing it (each about 1.8 m deep and 0.45 m higher), radial aisles, a top promenade, trees behind; sized to fill the site.
- Lookout hill: one smooth hill, a single trail at constant grade from the street to a paved summit plaza with a centrepiece, benches on the rim facing the view, trees below the tree line.
- Nature reserve and hiking: narrow trails only, clearings with one bench, fallen logs, forest-floor ground, a lookout on the highest point, dense forest (260-300 trees/ha); no lawn.
- Wetland: path loops first, ponds fill what is left (about 35 % water), islands, boardwalks over the largest ponds, bird hides, thick reed belts, wet woodland at the margins.
- Campground: a loop of narrow paths, pitches about 10 m apart staggered along it, each a gravel pad with tent, fire ring and table, pines between, a restroom per 25 pitches, reception by the gate.
- Greenway: a bike path under a single-species avenue, a footpath beside it, rest stops every 60 m, clumped copses.
- Farmstead: a yard at the entrance (barns along the back, silos in a battery, trailers, hay bales), tracks from the yard to the fields, fields on a grid with headlands, paddocks with a gate facing the yard, a trough and a shelter, hedgerows with gaps.
- Animal park: a visitor hub at the gate, a loop walk, pens with a shelter at the far side and hay and a trough by the gate, maybe an arena.
- Sports park: a tree belt, a promenade loop, the main field aligned with the long edge facing a spectator terrace and a clubhouse, courts grouped by kind, fields 6-18 m apart. Field sizes with run-off: football 121x86 (smaller 87x62, 67x51), tennis 38.6x20.3 per court, basketball 34x25, skate 34x26.
- Plaza and market: paving with a centrepiece, a tree ring or bosquet, benches between trees along the edge, stalls back to back in a grid with cross aisles.
- Beach and waterfront (on a shore: the brief's water summary gives the water level and side):
  1. Carve a bay, always (one call): shape_ground mode lower, an ellipse centred on the old waterline, radius along the shore most of its length, radius into the land 25-40 m, wobble 3, height 1.5-2 m below the water level, feather 10. It must touch the open water: the lake flows in, the shore becomes a curve, and the feather makes the beach slope up out of the water by itself. No water_level for this. The answer tells you how far the water will reach past the ellipse, and the map shows the new water in blue: that is the new waterline. Everything below is laid out from it, not from the old shore.
  2. The beach face: the feather already slopes it. Where the land behind is high, add a line along the back of the sand (shape_ground line, width 10-20, height 1-2 m above the water, feather 15) so the sand rises gently to a level promenade; never a step or a ledge at the water.
  3. Sand: paint_ground sand from 8-12 m out past the new waterline into the water to 30-50 m inland of it (the back of the beach), feather 6-10, noise 4-6, so it wraps the bay and runs under the water. Never stop the sand short of the water, and never paint sand where the ground is not lowered to the water (a sand strip on grass is wrong).
  4. On the sand: umbrellas with two loungers each in loose groups (beach) or rows (resort) about 6-8 m apart, lifeguard towers every 60-80 m facing the water, a timber jetty (place_path with a pier or boardwalk line and height 1 m above the water) from the sand straight out 20-45 m, boats pulled up at one end.
  5. Behind it: a promenade path parallel to the shore at the back of the sand, joined to the streets, with benches facing the water, lamps every 25 m, showers and a kiosk at the access paths; then dunes with marram grass (low mounds, bushes) or a painted lawn with palms or pines. No trees on the sand.
  Waterfront: the same shore and the same bay carve (feather 10), but the edge is built: a timber boardwalk along the curved waterline (place_path with a pier or boardwalk line, height 1 m above the water), a paved promenade behind it with benches facing the water, lamps and trees in a row, T-headed piers out over the water, and a park or plaza behind; a harbour basin (a rect lowered 2 m below the water level, opening to the lake, feather 4) with a boardwalk round it is a strong focal point. Never quays (the player does not want them).
- Ground shaping in general: every shape has a soft edge (feather, default 5 m; 10-20 for natural landforms). Lines (shape_ground line, width, line_heights) make channels and streams falling to a pond, ditches, berms and embankments along an edge, ramps and terraces with a level walk. Hills are a raised ellipse with a wide feather; valleys a lowered one.
- Parking (the premade car park, which players like; build it this way with the car park kit from the essentials):
  1. Ground: level it (a 1-2 % fall is fine). Surfaces never overlap (the game shows only one), so the ground is pieces side by side: grass paint (paint_ground) under the whole site as the verge, then asphalt as strips: one rect for each band between two medians (and from the outer medians to the ring's outer stall backs), the grass medians as their own rects in the gaps between the strips. Never dirt, sand or white concrete for the parking area.
  2. Lanes: the car lane (invisible, 7 m) as a ring about 10 m inside the outline, joined to the street at one or two entrances (end the lane on the street), and straight aisles across the lot parallel to its long edge, 22 m apart (centre to centre), joined to the ring at both ends. Lanes belong under the asphalt.
  3. Stalls: place_along each aisle with the stall decal, spacing 3, offset 6.5, side both, face away, scale 0.93: a row of stalls square to the aisle on each side. Leave out the stretch where a cross lane or the ring meets the aisle. Put a few EV stalls (with a charger behind each pair) and disabled stalls near the entrance. Also a row of stalls on the inner side of the ring.
  4. Medians (they make it look like a real car park; never skip them): halfway between two aisles the stall backs leave a strip 3 m wide. Leave it out of the asphalt and lay a grass surface there (aisles at z 33 and 55: asphalt strips end at z 42.5 and start again at 45.5, the median rect from x 14 to 130, z 42.5 to 45.5), place_along its middle line a tree every 9 m with bushes between, and a double lamp every 25-30 m on the same line. The outer rows by the ring get the same strip behind their backs.
  5. Edge: the verge planted with a row of trees every 9-10 m and a hedge of bushes every 3 m along the outline.
  A 140 x 110 m lot holds about 300 stalls this way; a smaller request (60 cars) is one or two aisles with the rest as planted verge.
- Industrial yards (works, junkyard, scrapyard, container, logistics, car yard; the premade ones are graded B to B+): build them as the premade types do (their kits are in the rough examples).
  1. Ground: dirt or gravel paint over the whole yard (paint_ground), never one white slab. Then pads side by side (surfaces never overlap): a grid of storage blocks 20-35 m across, each a concrete, asphalt or gravel pad holding one kind of thing in exact rows (skips, containers, wrecks, pallets, barrels, rubble or scrap heaps), with a 1-2 m edge of bare ground between pad and aisle.
  2. Aisles: asphalt strips 8 m wide between the blocks in a grid, joined to the street through one gate (a car lane under the asphalt from the gate, a barrier and a booth there), an office cabin or two and parked vehicles by the gate.
  3. Edge: a high industrial fence line around the whole outline with the gate gap, concrete barriers along the inside, a few trees and floodlights in the corners.
  4. Features: one or two landmarks that say what it is (a silo or tank, a crane, a weighbridge, heaps of ore or trash, a crusher), and life between the blocks (barrels, pallets, skips, trucks). Fill every block; a yard reads empty below about 250 objects per hectare.
  Containers are 2.44 x 6.04 m, stacked 2.62 m per tier; wrecks nose to the aisle, stacked higher toward the back.

## Close-up detail (what the player sees zoomed in, a few metres away)
From high up a layout reads; up close only the small things do. Every place gets a second, finer layer:
- Paths: a lamp every 20-30 m, alternating sides; benches in pairs or facing each other at widenings, each with a bin and a bush or planter beside it; low shrubs and flowers in loose drifts along the edges (scatter in a 2-4 m strip, clumps 0.6); a bollard or a sign at each entrance and junction; worn dirt paint where paths meet and at shortcuts.
- Features (a fountain, a playground, a gazebo, a kiosk, a stage): each has a setting: a paved pad or rubber ground under it, seats facing it, planters or flowerbeds at its corners, lamps, a bin, bicycle racks or props that say what happens there.
- Edges: no hard line between grass and anything: a band of bushes, flowers or tall grass along fences, walls, water and paved edges; a few rocks in groups of 3-5 where ground changes; trees at the corners.
- Ground: never one paint: patches of a second and third paint with noise (worn grass by seats, dirt under trees, dry grass in the meadows); stepping stones or gravel where people walk off the path.
- Planting: layered: trees, then bushes under and around them, then flowers and low plants at the front; clumps of the same kind, mixed kinds between clumps.
- Life: what people leave and use: picnic tables with a bin, bicycle racks, notice boards, signs, small stalls, crates and barrels in work areas, boats and buoys at water, toys at a playground, tools and hay at a farm, cones and pallets on a site.
- Places of work (yards, farms, lots): each pad full of its things in rows, with the small clutter between (pallets, barrels, crates, tyres, cones, bins, signs, lamps).
- Ground texture in detail: paint in layers, never one flat colour: a base over the area, then patches of a second and third paint with noise 3-6 (dry grass in meadows, dirt under trees and at gates, sand by water and play areas), worn strips along paths and desire lines (paint_ground strip, width 1.5-3, strength 0.6, feather 1.5), darker lush grass in hollows and by water.
- Surfaces in detail: side by side, each for one use: tiles or stone for a centre or a terrace, pavement around it, gravel for pads, sand or rubber under play equipment, concrete for yards; edge every paved area with a strip of planting or a low hedge.
- Decals: only the base game's markings, court and field lines and parking stalls are built (the essentials list them): lines on courts and pitches, stalls in car parks, arrows and crossings where cars move; never pack decals.
- Where things belong: crop fields hold only crops, hay and farm machinery (hedgerows, trees and play things go at their edges); beach things (umbrellas, loungers, towers, boats) stand on the sand and plants stay off it; play equipment on sand, rubber or a lawn by a path, never in a field; seats face something (a view, a path, each other). The tools refuse or report a misplaced thing: move it, do not leave it.
- Numbers: the premade types place 200-800 objects on a 1.5 ha site, most of them small; a finished build of yours is in that range and uses 20+ different assets. After the review comes a detail pass that lists the bare spots with coordinates: fill every one.

## What you cannot build yet
Zoning and growable buildings, buildings and service buildings, pedestrian areas with access, animals. (You can build: footpaths and trails of any kind, roads, alleys and the invisible car lanes of parking lots joined to the city's roads, fences and hedges as lines, raised boardwalks and piers, ponds with real water, scaled and tilted props such as parking bay decals, terrain, ground paint and surfaces.)
If the request needs one of these, build everything else well, say plainly in your note what you could not do, and note a wish.";
    }
}
