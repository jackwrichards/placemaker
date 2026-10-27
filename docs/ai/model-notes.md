# AI builder: models and thinking, what we saw

The same evaluation requests (`suite/scenarios/ai-eval.json`, `ai-opus.json`) on the same lands, built in the game
runner. Ratings are Claude Code's own, against the premade types and the user's grades. Transcripts:
`ModsData/Placemaker/ai/<time>/`.

| Run | Model, thinking, prompt | Per build | Steps | Cost | Rating | Notes |
|---|---|---|---|---|---|---|
| eval 1 (26 Oct) | Sonnet 5.5, default thinking, agent-6 | 60-285 s | 6-15 | $0.17-0.73 | about C | farm B, park and memorial C+, wetland got no water (anchor on an island), plaza D, two builds empty (the whole reply spent thinking) |
| eval 2 | Sonnet 5.5, low, agent-7 | 10-21 s | 3-5 | $0.06-0.12 | about D | fast but rushed: farm 8 pieces, no looking, no self-grade |
| eval 3 | Sonnet 5.5, medium, agent-7 + forced review | 27-58 s | 6-13 | $0.15-0.30 | about C- | the review was taken but light; the car park's lanes joined the streets for the first time |
| eval 4 (27 Oct) | Sonnet 5.5, medium, agent-8 + head start (essentials, examples) | 24-55 s | 4-9 | $0.12-0.29 | about C- | almost no searching any more (the essentials work); stops early; the park and plaza lost their paths under a whole-site surface (now a reported problem) |
| opus 1 | Opus 5.5, high, agent-8 + head start | about 40 s a step, the park unfinished after 11 min | 16+ | $2.40+ for the park | B on its map at step 12 | 7,000 thinking tokens of planning up front; a loop with entrances from all four streets, a central fountain plaza, a mound with a path up, a playground, dirt and meadow patches, dense planting; still a planted boundary ring. The queue's watchdog (no progress for 5x the estimate, 12.5 min here) stopped the game |
| opus 2 (27 Oct) | Opus 5.5, medium, agent-8 + head start | 3.5-9 min | 12-30 | $1.08-4.10 | about B- | farm B+ (farmhouse, barns, a battery of silos, a tractor yard, gravel tracks joined to the streets with crosswalks, irrigated fields, an orchard, fenced paddocks with a shelter, hedgerows: what the user asked for), park B (main walk to a central plaza, pond basin with a loop, gazebo, playground, fitness and picnic corners, four entrances with crosswalks), wetland B (marsh basins, boardwalks crossing, a gazebo on an island; no water visible), plaza C+ (central fountain with a ring of benches, two small fountains, cafe tables, kiosks; flat grey paving, a ring of trees), car park D+ (a pale slab, invisible lanes, no stalls visible) |

What makes the difference:
- **Effort, not knowledge.** Sonnet knows the same rules (it quotes them in its notes) but stops after a first layout;
  Opus plans the whole design before its first call and keeps building. Thinking low makes Sonnet faster and worse;
  medium is the best Sonnet trade so far.
- **The head start removed the searching** (eval 4: 1-7 searches a build instead of 5-17) but did not raise the
  ambition by itself.
- **Mechanical failures cost more than taste:** water anchored on islands, surfaces hiding paths, empty replies. Each
  is fixed in the tools' answers, not the prompt.

- **Opus at medium is the sweet spot so far:** about a grade and a half above Sonnet on the same prompt, at 5-15 times the cost ($1-4 a build) and 3-9 minutes.

Open: water does not show in the runner pictures (the basins are dug and the build reports water sources; the test game may be paused, so the water never fills: to check); car parks (invisible lanes need a visible ground and decal stalls the AI does not yet place); a keep-going mechanism for Sonnet that is not a quota.

## Cost and steps (27 Oct, agent-9)

Where the money went: input, not writing. Every step re-sent the whole conversation (every map picture too) and only
the brief was cached: Opus's $4.10 farm read 1.7 M tokens, half at full price, and wrote 25 k. Now the conversation's
end is marked for the cache on every step (two rolling marks beside the guide and the brief): 85-92 % of the input is
cached. Fewer, bigger steps: a whole layer per reply, `place_along` for rows (avenues, benches facing paths, stalls along
aisles, rings around a fountain) and rect/circle/ellipse/wobble for every area instead of long point lists.

| Run | Model | Per build | Steps | Cost | Rating | Notes |
|---|---|---|---|---|---|---|
| sonnet 9a | Sonnet 5.5, medium, agent-9 | about 50 s | 5-7 | $0.11-0.15 | about C- | car park stalls vanished (filed as plain decals, dropped by the plan: fixed), a whole-site grass surface hid the park's paths (now refused) |
| sonnet 9b | same + fixes | 30-70 s | 4-10 | $0.09-0.18 | about C | park C+ (curved paths, round plaza, playground on sand, a loop; sparse), car park C+ (60 stalls visible in four rows on dirt, trees between; first readable car park), wetland C+ (boardwalks between ponds, dense groves; no water visible), farm C (barn, silos, tractors, crop fields, a fenced paddock; trees planted over the fields), plaza C- (one grey slab, a ring of trees, a small fountain with benches: empty) |

Still open: Sonnet stops after one review round; the plaza's emptiness was not seen by the review numbers (paving
counted as filled: fixed); trees over crop fields are not a reported problem.

## AI against the premade types (27 Oct, Sonnet 5.5 medium, one plain request each; tools/suite/ai-vs-types.js)

| Type | Premade | AI round 1 | AI round 2 (recipe or kit added) |
|---|---|---|---|
| parking | A- | B (after the car park recipe) | |
| park | B+ | C+ | |
| beach (lake to the north in round 2) | C / C+ | C- | C (sand strip and path; the sand stops short of the water, no shaping) |
| waterfront (lake in round 2) | D / C | C- | C+ (promenade on the shore, a jetty, a park behind with a round plaza) |
| junkYard | B | C | B- (pads of wrecks, trash, skips and scrap, aisles, a fence; scattered rather than in rows) |
| industrialWorks | B+ | D (one white slab) | C+ (four pads, crossed aisles, a fence; scattered, white pads, no landmark) |
| scrapyard | B- | C+ | |

What moved the grades: a recipe pulled from the premade planner and its exact kit (car park D to B, industrial D to C+,
junkyard C to B-). Still open: things on pads are scattered, not in rows; the shore types (premade and AI alike) do not
carve the shore; the AI keeps its sand inside the selection, short of the water.

## Shores (27 Oct): carving for our types and the AI

Our types: `ShoreCarve.Bay` cuts a crescent bay below the water level (new control Bay: family beach 24 m, resort 16 m,
wild dunes 32 m, promenade waterfront 18 m, harbour 20 m; pier park none) and the type plans against the curved waterline.
In the game runner (lake to the north, the game run 40 s so the water flows in): family beach C+ to B-, wild dunes C to B
(a real cove), resort B-, promenade waterfront B (a curved boardwalk), harbour B (a crescent quay and apron).
Runs: before `show-2-builds-1027-085129-42ef`, after `show-3-builds-1027-085656-d3b3` (beach), `show-3-builds-1027-090127-98be` (waterfront).

AI (Sonnet, handbook-6 shore recipe, the brief's water summary, shore work allowed past the selection by the water, the
review's shore numbers): beach C to B- (a sloped beach face with sand running under the water, umbrellas, a jetty, a
promenade), waterfront B- then C+ (boardwalk and T-piers the first time; a basin the second time that did not fill).
Sonnet does not carve a bay by itself yet. Runs: `20261027-085835-ai-shore`, `20261027-090449-ai-shore`.

## Terrain shaping without limits (27 Oct, agent-10, handbook-8)

shape_ground got soft edges (feather, default 5 m) and lines (channels, streams, ditches, berms, ramps, shelves with a
height per point); the inside of a shape is laid as whole shapes and only the soft edge as cells (cells alone were
smoothed by the 3.5 m terrain grid and a pond got no water). The AI's map shows in blue the water its own shaping makes,
the tool answer gives the new waterline, and objects left under water are a problem. Never quays (the user): our
waterfronts build timber boardwalks, the AI's quay lines are refused.

Sonnet, runs `20261027-100022-ai-terrain`, `20261027-100749-ai-terrain`, `20261027-101400-ai-terrain`: it now carves
on its own every time. Beach B- to B (a carved bay the lake flows into, sand along the new curved waterline, umbrellas
and loungers on it), park "with a hill, a valley and a stream into a pond" B (a hill with a gazebo on top and worn
slopes, a pond with water, a channel), waterfront C+ (a promenade and a jetty; it skipped the bay that time).
Our harbour without quays: `show-waterfront-harbor-1027-100750-19f4` (timber boardwalk on the curved shore, two T-piers).

## Close-up detail (27 Oct, agent-11, handbook-9; run `20261027-102735-ai-detail`, close-up pictures)

The user: "B- from high up, D- zoomed in". A close-up guide for every kind of place in the handbook, and a detail pass
after the review that lists the bare spots with coordinates. Close-up grades (ours: park B): junkyard B (514 objects in 54
kinds: wreck rows, trash heaps, clutter, machines, floodlights; the pass worked), plaza C+ (fountain ringed by benches,
cafe table, flowerbeds; paving still bare), beach C+ (umbrellas, loungers, towels, lamps; thin), park C (a fountain,
flowerbeds, lamps; sparse planting), farm D+ (the pass scattered shrubs over the crop fields: bad taste). The pass now
only fixes the listed gaps with things that belong there, no quota ("at least double" removed).

## Chat makeover and detail round (27 Oct; run `20261027-105156-ai-ui`, UI test `UI/mock/tests/aichat.spec.ts`)

The user graded the chat D- (no spinner, overlapping, tool use unclear). New card: header with model, welcome with
examples and the mods switch (base game assets only unless on; fixed after the first message), the AI's actions per
step with icons and problem badges (older steps folded), review/detail/fix dividers, a progress row (phases, spinner,
time, Stop), the message box at the bottom, footer with the asset rule and Start over (asked first), a warning before
anything leaves the chat. The overlap was the conversation box without scrolling. Seen in the game (chat while working
and done). Close-ups (Sonnet, handbook-10): park C+/B- (playground, kiosk, lamps, flowerbeds, tree clumps), beach C-
(umbrellas and loungers on the lawn behind the promenade, shrubs on the sand), farm C- (shrubs and a climbing frame on
the crop fields). Open: placement taste (what belongs on sand, crop fields, lawns) is not checked yet.

## agent-12: freedom, vision first, many rounds (27 Oct; runs `20261027-112447-ai12-sonnet`, `20261027-113105-ai12-opus`)

The user: "slightly passing (C-) but not inspired at all ... is the prompt too strict?" Yes: 10,800 words with 40
never/always/must rules, step-by-step recipes and three correction passes. agent-12 gives full artistic liberty, a
vision tool first (name, story, signature moment, ideas, zones; a card in the chat), many building rounds and one
second look; handbook-12 leads with what makes a place memorable, keeps the hard facts as the only rules and turns
recipes into references; wobble for any polygon.

| Model | Rounds | Cost | Result |
|---|---|---|---|
| Sonnet 5.5 medium | 4-8 | $0.12-0.18 | about C: real visions ("Lantern Green", "Driftwood Cove", "Linden Square") but it still builds them thinly; the junkyard dug a 490 m chasm ("height 6" for a 6 m mound: now refused) |
| Opus 5.5 medium | 16-25 | $0.74-1.11 | about B+: Sundial Cove (a carved crescent bay, a T-pier like a sundial needle, umbrella rows, dune headlands, a gazebo, a beach car park, a playground) A-; Fountain Court (a stone plaza with a sculpted fountain, a ring of benches, a market row, a flower garden) B+; Duck Pond Farm (yard, barns, silos, striped fields with crops and machinery, paddocks, a pond knoll) B+; Linden Commons (loop, lawn, timber walk, playground, groves) B; Rust Hollow Salvage (sorted pads, a scrap mound, a container wall, floodlights, gravel tracks) B |

Caching made Opus affordable: about $1 a build instead of $1-4. Opus is the model that uses the freedom.
