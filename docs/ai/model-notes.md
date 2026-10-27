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
