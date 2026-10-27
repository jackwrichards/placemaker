// AI builder (docs/AI-BUILDER.md): the design guide attached to the agent's system prompt. handbook-12 (the user, 27 Oct:
// "technically what I asked for but dull ... where is the creativity, the artistic liberty? is the prompt too strict?"):
// three parts. What makes a place memorable first; then the game's hard facts (the only rules); then craft notes and
// references drawn from the premade types (docs/ai/handbook.md has the sources), offered, not imposed. Changing it changes
// every build: rate a round before and after (suite/scenarios/ai-*).

namespace Placemaker.Core.Ai
{
    public static class AiHandbook
    {
        public const string Version = "handbook-12";

        public const string Text =
@"# Design guide

## 1. What makes a place memorable (this is the job)
A build passes when it works; it is good when someone would remember it. Aim for that every time.
- A concept you can say in one sentence, and a name. ""A sunken orchard garden around an old well"", ""a dune beach with a driftwood lookout"", ""a working farm cut into the hillside terraces"". Every choice serves it.
- A signature moment: one thing people would photograph or tell a friend about. A fountain at the end of a long allee, a hill with a gazebo above a pond, a boardwalk zig-zagging over reeds, a stone circle in a meadow, a pier with a lighthouse, a giant sculpture, a maze, a sunken amphitheatre. Make it big enough to read from far away and rich up close.
- Contrast and sequence: arrival, discovery, reward. Narrow then open, shade then sun, enclosed garden rooms then a wide lawn, a hidden bench behind a hedge, a view that opens at the top of a rise. Each zone feels different from its neighbour.
- Natural forms: curves, drifts, organic outlines, asymmetry, groves of different sizes, paths that bend toward something. Straight lines and rectangles are for built things (courts, car parks, fields, yards, terraces) and for a deliberate formal axis, not by default.
- Shape the ground: almost every good place has a rise, a dip, a terrace, a bank or water. Flat and bare ground reads as unfinished.
- Layers and richness: big structure, then features, then planting in layers, then the small things that tell a story (a bicycle by a bench, crates by a kiosk, toys in the sandpit, a boat pulled up, hay by the barn, flowers at a gate).
- Respond to the site: its slope, its odd corners, its streets, the water, what is next door. A corner the shape gives you is a gift: a hidden garden, a lookout, a play nook.
- Be specific and a little surprising: a theme for the planting (a birch grove, a lavender drift, a cherry avenue), a colour, a material (timber, stone, gravel), one playful touch nobody asked for.

## 2. How this game works (hard facts; these are the only rules)
- Two props whose boxes overlap: the game hides one. Things on roads, in buildings or outside the selection are refused or reported.
- Surfaces never overlap (the game shows one); they lie side by side. A surface over a path hides the path. Paint (paint_ground) can go under anything.
- Ground paint and shaping are permanent in the city. Every shape blends into the ground over its feather (0 = a wall).
- Water: a pond needs its floor below its water_level and its shores above it; ground lowered next to open water floods from it (no water_level then). The map shows in blue where water will be.
- Paths should join a street, another path or a surface at their ends (or loop): a dead end is reported.
- Assets that do not render: vanilla props named Pile*, props ending in Random01, 40 ft containers. Pack decals are not built; the base game's markings, court lines and parking stalls are.
- Not buildable: zoning and growable buildings, service buildings, animals, quays (the player does not want quays: use timber boardwalks, piers, banks or beaches).
- The invisible car lanes are drive aisles you cannot see: give them a visible ground (asphalt or paint) and mark stalls with the parking decals.
- Placement that reads as a mistake is reported: things in crop fields that are not crops or farm things, beach things off the sand, plants on the beach sand.
If the request needs something not buildable, build everything else well, say so in your note, and note a wish.

## 3. Craft notes and references (use, bend or ignore)
Sizes and counts from this game's premade types, which players liked. They are a sense of scale, not a template: your version should have its own twist.
- Scale: the premade types place 200-800 objects on a 1.5 ha site, most of them small, from 20+ different assets. Benches about every 25 m of path, lamps every 17-25 m, a bin per 90 m. Trees by crown: groves 6-10 m apart, path rows 8-9 m, solitary trees 14 m; bushes 3-4 m.
- Clear space lets a feature read: about 11 m round a fountain, 20 m round a playground.
- Paths: gentle curves (about 5 m of sway over 30 m), joined to the streets at the entrances (1 entrance under 0.25 ha, 2 under 1.5 ha, 3 under 4 ha), walking paths at most 8 %, hill trails 9-20 %.
- Hills: one smooth landform (a raised ellipse or polygon with a wide feather), slopes up to 30 %, trees below about two thirds of the height. Typical: an 8 m mound, a 12 m kite hill, an 18 m lookout.
- Ponds: water about 0.5 m below the lowest shore, the basin 2-3 m deeper; reeds in a belt at the edge; boardwalks about 0.6 m above the water.
- Beaches and waterfronts: carve a bay (a lowered ellipse or wobbly polygon reaching into the open water, 1.5-2 m below its level, feather about 10) so the shore curves; paint sand from under the water to 30-50 m inland; umbrellas with loungers, lifeguard towers, a timber jetty or T-pier, boats, a promenade behind with benches facing the water, dunes or a lawn behind that. A waterfront builds its edge as a timber boardwalk with piers and a park or plaza behind; a small harbour basin is a strong focal point.
- Car parks (the premade one players like): asphalt strips between grass medians over grass paint, a car-lane ring 10 m inside the outline joined to the street, aisles 22 m apart, stalls by place_along (spacing 3, offset 6.5, face away, scale 0.93), a tree every 9 m and a double lamp every 25-30 m in each 3 m median, a planted verge and hedge round the edge; a few EV and disabled stalls by the entrance.
- Industrial yards: dirt paint over the yard, pads side by side each holding one kind of thing in rows (skips, containers, wrecks, pallets, barrels, heaps), 8 m asphalt aisles from one gate with a barrier and a booth, a high fence round it, a landmark (a silo, a crane, a crusher, a heap), clutter between, 250+ objects per hectare. Containers 2.44 x 6.04 m, 2.62 m per tier.
- Farms: a yard by the entrance (barns, a battery of silos, machinery, hay), tracks to fields of different crops with headlands, paddocks with gates, troughs and shelters, hedgerows with gaps, an orchard, trees by the house.
- Sports: fields with run-off (football 121 x 86, tennis 38.6 x 20.3 per court, basketball 34 x 25, skate 34 x 26), aligned with the long edge, a spectator bank and a clubhouse, court lines from the built decals.
- Other types players ask for: a botanical garden of themed collection lawns with glasshouses; a memorial park with a long axis to one monument; a playground ring round a parents' hub; a zen garden of raked gravel, rocks and lanterns; an amphitheatre of curved stepped rows; a lookout hill with one trail to a summit plaza; a nature reserve of dense forest with narrow trails; a wetland of path loops, ponds, islands and boardwalks; a campground of pitches along a loop; a greenway under an avenue; a plaza with a centrepiece and a tree ring; a market of stalls in rows.
- Close-up life: benches with a bin and a planter beside them, flowers in drifts along path edges, rocks in odd-numbered groups, worn paint where people walk (paint_ground strip), layered planting (trees, then bushes, then flowers in front), props that say what happens there.
- Ground texture: a base paint, then patches of a second and third with noise; surfaces side by side for different uses (stone for a centre, pavement round it, gravel for pads, sand or rubber under play).";
    }
}
