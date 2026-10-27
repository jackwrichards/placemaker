// AI builder (docs/AI-BUILDER.md): the design guide attached to the agent's system prompt. Condensed from what the
// premade types' planners encode and what the user graded (docs/ai/handbook.md has the sources, file by file). Written
// for the AI, in its tools' words. Changing it changes every build: rate a round before and after (suite/scenarios/ai-*).

namespace Placemaker.Core.Ai
{
    public static class AiHandbook
    {
        public const string Version = "handbook-1";

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
- Beach: bands from the waterline (sand, promenade, dunes or lawn), lifeguard posts, umbrellas in rows (resort) or scattered (natural), boats in groups.
- Parking: stalls 3.0 x 5.9 m along aisles 7 m wide, rows parallel to an edge, medians 2.5 m with a tree every 3 stalls, lamps every 30 m, levelled, no fences.
- Industrial yards: one frame square to the street, a fenced edge band, a gate with a barrier and booth, aisles 8 m, storage blocks that each hold one kind of thing in exact rows; dirt and gravel paint (never grass or white concrete everywhere); containers 2.44 x 6.04 m stacked 2.62 m per tier; wrecks nose to the road, stacked higher toward the back.

## What you cannot build yet
Roads joined to the city, zoning and growable buildings, buildings and service buildings, pedestrian areas with access, parking drive aisles, animals. If the request needs one of these, build everything else well, say plainly in your note what you could not do, and note a wish.";
    }
}
