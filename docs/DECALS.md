# Decals — census, model and dressing rules (s21)

> **2026-10-29: grime is code-made now (docs/DYNAMIC-DECALS.md).** Placemaker draws its own decals from recipes
> (Core/Decals/Dynamic): never entities, never prefabs, no decal packs needed, nothing in a save but bytes on the group
> record. The pack-decal dressing described below stays off for good; the Decals feature scatters only the base game's
> markings. Functional base-game decals (court and pitch lines, parking stalls) are unchanged.

> **2026-10-19: decals from decal packs are no longer placed (GitHub issue #2).** The see-through copies of pack
> decals were runtime prefabs a save names; the packs arrive 30-75 s after startup and the copies were given up after 600
> frames, so loaded cities showed the game's white missing-object boxes, and no copy can outlive the mod. The user:
> "Let's just drop decal support." Plans keep only the base game's kinds (markings, stalls, court and field lines,
> Core/Decals/PackDecals.cs); the rest of this document describes the dressing that is now switched off (its code stays).
> Game/Build/OldDecalCleanupSystem removes the old copies from loaded cities. Ground-wear paint is unchanged.

User brief: *"Decals are not a thing you sprinkle around. They complement every other feature, and they're their own
sub-feature of every other feature: leaves under trees, cracks in the paths, sandy rocks, a dirt patch… There's NEVER not
room for decals — they don't take up space."*

## Headline findings

1. **Vanilla CS2 1.6.2 has no decorative ground decal at all.** All 199 vanilla decal objects are functional: parking
   stalls and parking icons/text, road arrows and lane markings, letters and numbers, bicycle/pedestrian icons,
   sports courts and pitches, playground pads, container grids and Chirper logos. Every leaf, crack, stain, puddle, oil
   blot, litter, sand, seaweed or tyre-mark decal a player has comes from Paradox-mods asset packs (here: 761 decals from
   22 packs). Dressing therefore *only* works with packs installed; without them the pools are empty and nothing is
   placed (the existing "no assets installed" info covers it — never a "no room").
2. **Decals collide in the game.** A decal-only static object is `Overridable | WalkThrough` but still has a 1 m tall
   collision box (a leaves decal is 6×1×6 m) and meets every prop, tree and path it lies on: the ghost cancels both
   optional temps, a real build hides one (`OverrideSystem`). That — plus our own Declutter treating decals as tier-4
   objects — is where "no room for decals" came from.
3. **Pack decals carry a projection mask** (`colossal_DecalLayerMask` in the pack's `decal.json`), and it IS `DecalLayers`
   (s23: ExtraAssetsImporter sets `DecalProperties.m_LayerMask` from it; Terrain 1, Roads 2, Buildings 4, Other 32). The 84
   RealVision decals with 4 and 22 with 36 project onto buildings only. The catalog marks decals without the Terrain bit
   `WallOrHanging`, so they are never automatic.
4. **Pack prefixes lie.** "G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 1 Decal" is an oil blot; the item part
   (after the last "G87", or after the pack's "… Pack"/"… Decals") decides. "Puddle Mirror" is a puddle, not glass.

## How the census was made (and how sure it is)

- Source: the player's `catalog-dump.json` (v19, 960 decal objects: everything whose every mesh is a decal mesh,
  classified by `AssetHeuristics`), real footprints from `ObjectGeometryData` bounds.
- Pack metadata: `…/.cache/Mods/pdx_mods/<id>/CustomDecals/<Category>/<Item>/decal.json` (mesh size, layer mask, draw
  order) and `icon.png` / `_BaseColorMap.png` next to it. Contact sheets of the icons were made and looked at for
  G87 Stains & Puddles (78), G87 Trash/Leaves (57), Ground Texture Decal Pack (10), Fallen leaves (16), Sand Surfaces and
  Beach (44), Golden Sand (7), Cracks and Damage (15), Stains and Leakage (10), Asphalt Patches (32), G87 Dirty Roads (26),
  Manicured Grass (18), Vanilla Terrain Grass (15), G87 curbs/chevrons and the small RealVision items (63). These packs
  are confidence ●● in the census; the remaining RealVision ground sheets are ● (item word only). Vanilla decal icons are
  in Asset Icon Library's thumbnail pack (only vanilla), names are unambiguous.
- What the eye check changed: "lined" leaves are drifts against ONE edge (directional, used along kerbs and path edges);
  G87 "Band"/"Track" stains and puddles are long 2×6 / 2×12 strips (along paths, wheel lines); G87 "Wide/Narrow" are
  tear-drop drips; G87 Debris 01–05 are gravel/rubble grit, Beach Debris is driftwood; G87 Seaweed are 8×4 wrack LINES
  (along the shore); G87 Dust is coloured dust; Leakage are drip streaks; RealVision "Road Side Dirt / Grass" strips fade
  on one side (edge strips); G87 "Dirt Road Transition" are 12×6 dirt-road surface strips.
- The generated tables at the end list every decal with family, size, shape, ground, natural orientation, mask,
  confidence and dressing pools. Regenerate: `PM_WRITE_DECALS=1 dotnet test Tests/Placemaker.Tests --filter DecalsCensus`.

## Taxonomy

| group | families | functional? | typical shape | ground | natural orientation |
|---|---|---|---|---|---|
| Vegetation litter | LeafLitter, LeafEdge, PalmLitter, Mulch, Moss, Overgrowth | no | 3–6 m blobs; lined leaves directional | any / beds / shade | random; LeafEdge along the edge |
| Ground wear | Dirt, Dust, Mud, Sand, WetSand, Rocks, Rubble | no | 2–6 m blobs, 12×6 mud strips | grass, dirt, sand | random; mud strips along the drive |
| Stains & water | Stain, Oil, Leak, Puddle, Wet | no | 3–5 m blobs, 2×6 bands, drips | paving, asphalt | random; bands/drips along the host |
| Damage | Crack, Patch | no | 1–5 m | paving, asphalt, paths | random |
| Traces | Tyre, Footprint | no | 2×6 / 2×12 strips, 2 m tread tiles, 0.3 m prints | paved, dirt, sand | along the wheel line / walking line |
| Litter | Litter, Glass, BeachWrack | no | 0.5–5 m | paved, grass, sand | random; wrack lines along the shore |
| Art | Graffiti | no | 6–10 m | paving | random |
| Designed ground | Paving, LawnPattern, GroundSheet | laid, not scattered | tiles, 12–120 m sheets | — | lattice / fixed |
| Functional | Marking, SportsMarking, ParkingStall, FarmTrack, Logo, BuildingOnly | **yes** | — | — | fixed by the layer |

## The decal model (what changed)

- **Decals take no space.** `ObjectRole.Decal` never reserves (`Composer.Reserve`), never takes part in
  `Build/Declutter` (tier −1: never nudged, never dropped, never a "didn't fit" issue), never counts toward or gets
  thinned by `ObjectBudget` (decals have their own budget in the dressing pass: 1800/ha × Decals amount, ≥ 300), and
  layers keep their own decal art caps (`GritAssets.CapDecals`). The Decals feature never reports "no suitable ground":
  a theme whose ground is missing falls back to any dry ground (markings and paving patterns excepted).
- **In the game** (`BuildService.EmitObjectDefinition`, setting "Decals never collide", default on): decals are emitted
  with `ObjectDefinition.m_Elevation = −0.01`, so the object gets an `Elevation` < 0 and `ObjectUtils.GetCollisionMask`
  returns `Underground` only — no ground prop, tree or surface net shares that bit, in validation (ghost) or after Apply
  (`OverrideSystem`). Side effect read from the decompiled render code: objects with Elevation < 0 are batched on the
  `Tunnel` mesh layer (`BatchInstanceSystem`), which belongs to the normal culling layers; **must be confirmed in game**
  that sunk decals still render in the normal view (if not: switch the setting off; decals then behave as before).
- **Never on water, building lots or zoning cutouts.** The dressing pass refuses water (site depth or planned lakes)
  itself and runs before `Cutouts.Enforce` and `SiteSafety.Apply`, which drop decals on cutouts and lots (decals left out
  on lots are not counted in "N things left out").
- **Orientation by intent.** Plans give decorative decals `Yaw` = the direction of the decal's long axis (path tangent,
  bay axis, shoreline, wheel line). `Core/Decals/DecalFit` runs once after automatic assets are resolved (game
  `ResolveAutomatic`, gallery) and turns a prefab that is long in X by 90°. Functional markings are never turned.
- **Master 0** removes every decorative decal (layer-made ones too); functional decals (markings, stalls, field lines,
  farm ruts, paving patterns) stay. Below 100 % the master also thins layer-made decorative decals evenly.

## Dressing pools (automatic, vary per object, size-capped)

| pool | holds | cap (min) | falls back to |
|---|---|---|---|
| `decal.leaves` (theme) | leaf litter | 8 m | — |
| `decal.leaves.edge` | lined leaf drifts | 8 m | decal.leaves |
| `decal.mulch` | mulch / needle beds | 6 m | decal.leaves |
| `decal.moss` | moss, weeds, grass through cracks | 7 m | decal.wear |
| `decal.wear` | dirt patches, dust | 7 m (1.5) | decal.dirt |
| `decal.mud` | mud, road dust, dirt-road strips | 14 m (2) | decal.wear |
| `decal.sand` / `decal.wetsand` | sand patches / wet sand bands | 12 m | wear / wet |
| `decal.wet` | wet patches, splashes | 10 m | decal.puddles |
| `decal.rocks` | gravel, stones, rubble grit | 10 m | decal.wear |
| `decal.stain` | grime stains | 7 m | decal.dirt |
| `decal.oil` | oil blots, drips | 6 m (0.8) | decal.stain |
| `decal.patch` | asphalt/tar repairs | 5 m | decal.cracks |
| `decal.tyre` | tyre tracks, wheel-line stains | 12 m (1.5) | decal.stain |
| `decal.glass` | broken glass, bottles | 3 m | decal.litter |
| `decal.beach` | seaweed, shells, foam, sea trash, palm litter | 9 m | decal.sand |
| `decal.footprints` | footprints | 1.5 m | — |
| theme pools `decal.cracks/puddles/litter/graffiti` | as before | 8/8/6/10 m | — |

## Complement rules (`Core/Decals/Dressing*.cs`)

A pass in `Composer.Compose` after paths are joined, before cutouts and building lots. Every fill step with a dressing
profile owns one settings set (its features share it); amounts = Decals × family amount × style weight of the decal's
kind (Clean / Lived-in / Gritty). Every decision is a hash of the plan seed and the host position (deterministic,
re-plan stable).

| family (control) | host | what it adds |
|---|---|---|
| Trees (Leaf litter) | every tree and shrub | ~2 litter decals per grown tree inside 85 % of the crown (more for big crowns), mulch rings at some trunks, needle/mulch beds under conifers |
| Paths (Path wear) | every footpath, junction and entrance | lined leaf drifts on both edges (much more near trees), worn verges, moss on shaded edges, cracks, puddle bands along the path, litter; corner-cutting wear in every junction corner < 150°; trodden verges and carried-in leaves at entrances |
| Paved (Stains & cracks) | paved surfaces and what stands on them | clustered cracks, stains, repairs (asphalt), puddles, litter, glass, graffiti (gritty); scuffs in front of benches, litter/stains by bins, wet splash ring round fountains, stains round kiosks/cafés/stalls |
| Parking (Oil & tyres) | every bay, aisle, turn, entrance | oil blot in ~40 % of bays over the engine third, along the bay; drips; tyre marks at stall mouths; stains/patches/wheel lines along aisles; tyre marks fanning at turns and entrances; litter and glass behind edge rows |
| Play (Worn patches) | play items, picnic tables, camping pitches, dog equipment, benches, bins | bare ground under items (by size), under tables, in front of benches on grass, litter by bins |
| Sports (Pitch wear) | goals, hoops, fitness, skate, stands | worn goalmouth strip along the pitch axis, scuffs under hoops, wear under stations, skate marks/graffiti, litter by stands |
| Ground (Ground wear) | the step's open ground (once per step) | clustered: lawn → leaves (shade), worn patches, moss, stones (natural types); dirt → mud, wear, stones, puddles; sand → sand patches, wrack, footprints |
| Shore (Shore wrack) | the waterline (site water or planned lakes) | beach: wet sand bands a metre above the waterline (along it), wrack lines inland, sea trash, footprints toward the water; grass/dirt banks: damp bands, mud, moss |
| Works (Mud & tracks) | drive nets, long narrow track surfaces/paint, machines, heaps | tyre tracks on both wheel lines, mud and rut puddles along, mud + tracks at gates, oil under machines, mud/grit rings round heaps, yard dust/grime scatter |
| Ruins (Decay) | slabs and paving, rubble, walls, weeds | heavy cracks, patches, moss, grit, stains, glass, litter, graffiti; grit rings round rubble; moss at wall feet |
| Garden (Mulch) | beds, shrubs, hedges, planters, crops | mulch under shrubs and in beds, leaf drifts under hedges, water stains under planters, mud between crop rows |

Profiles (families per type, default style): parks/gardens lived-in (formal garden, botanical, cemetery, memorial,
zen, golf: clean); lots/commerce lived-in; abandoned lot, urban ruins, construction, landfill, logistics, quarry,
scrapyard, skate park: gritty. Preset styles: e.g. park *formal/garden/victorian/modern* clean, *urban* gritty; plaza
*civic/podium/garden* clean; parking *green* clean; abandoned lot *demolished/fenced wasteland* lived-in; landfill
*capped/compost* lived-in; beach *resort* clean; sports park *stadium/tennis club* clean.

## Controls

Every fill type with a profile (44 types) gets, in its **Decals** section: **Decals** (0–300 %, default 100) and
**Decal style** (Clean / Lived-in / Gritty, default per type, set by presets). Each dressing family of the type gets an
amount slider (0–300 %) in the section of what it complements when the type has one (Leaf litter under *Planting*,
Path wear under *Paths*, Worn patches under *Picnic*/*Furniture*, Shore wrack under *Shore*…), else in Decals. Ids
(save format): `dressAmount`, `dressStyle`, `dress<Family>`. Old saves without them load the defaults. The existing
Decals feature (theme scatter) is unchanged in settings.

## Art pass (s23): see-through, soft-edged, deliberate

User after seeing them in game: the leaf scatter was "hard, opaque, crisp"; an opaque dark-brown mud rectangle over a
junction and a grey wet streak "look odd"; a faint sand dusting over paving at an edge was "perfect, you almost don't
notice it". Most should be ~80 % transparent, used artistically rather than randomly, more of them in the good spots.
Decals stay ON by default (Decals 100 %, the Decals feature as before); the art pass is the fix.

- **Opacity** (RESEARCH-NOTES-ADDENDUM 82–83): there is no per-instance decal opacity, so the build swaps each decorative
  decal for a runtime see-through clone of its prefab per tier (`Game/Build/DecalOpacitySystem`, `DecalLook.Tier`, tiers
  20/35/50/100 % then; s28 below). Every decorative pool was 20 % except sand dust and wet sand (35 %); markings, stalls and field lines
  stay 100 %.
- **Edge classes** (`DecalEdge`): Soft (feathered: G87 stains A–H, dust, wet effects, Ground Dirt, mulch, moss, fallen-leaf
  scatters, golden sand), Item (discrete things on a clear ground: litter, glass, shells, driftwood, palm leaves), Shaped
  (specific cracks, damage, repairs, tyre tracks, bands, drips, potholes, lined drifts, seaweed lines), Hard (RealVision
  texture tiles, the G87 dirt-road transitions (the mud rectangle), asphalt/sidewalk/grass tiles, the G87 "Dirty Pack
  Leaf" squares that print leaves on grass). Scatter pools take Soft (+Item); placed pools (tyre, cracks, repairs, edge
  drifts) take Shaped; Hard is never automatic, neither in dressing nor in the Decals feature's theme pools.
- **Placement**: sand dust along every sand↔paving seam on the paving side, in soft runs (new Seams rule; dirt dust where
  dirt meets paving); verge wear only near path junctions and entrances, bench/table wear from their own rule; leaf drifts
  on path edges only in the shade of trees; the litter bed under a crown fades toward paths; path cracks along the edge,
  oriented with it; paving stains and cracks gather at the paving's edges; no random scatter on open lawn (leaves only in
  shade, stones only in natural types); no sand dust on sand. A higher amount raises the chances in these spots instead of
  spreading wider.
- **Gallery** draws decals at their tier opacity; `--decals` renders decals-on (100 %), decals-max (300 %), decals-gritty.
- **Second look** (screenshots 37/38, "it just looks like there's poop all over the floor"): the test is that from a normal
  camera height no single decal is consciously noticed. Clean and Lived-in dress only with what blends (`Dressing.Allowed`):
  faint leaf litter under crowns (light/scattered kinds, never red, never dense "medium" piles, never on paths), faint dirt
  dust where feet stop (path junctions and entrances, bench fronts, play items, goalmouths), sand dust where sand meets
  paving, wet sand at the waterline, faint oil in parking bays and faint stains by benches, bins and kiosks. Cracks,
  repairs, tyre marks, mud, puddles, moss, mulch, stones, litter, glass, wrack and graffiti are Gritty only (still placeable
  by hand with the Decals feature). Golden Sand discs count as hard-edged (never automatic), sand dust is pale (never red),
  footprints are never automatic, open paving stays clean unless Gritty, and the Decals feature's leaves/dirt themes only
  gather under crowns or by benches, lamps and bins — never on open lawn or on a path.

## s28: really see-through, fewer, no clashing colours

- **Why s23's copies never got lighter** (RESEARCH-NOTES-ADDENDUM 85): the pack decals use the DefaultDecal shader graph,
  which reads `_BaseColor` as a vector (w = alpha) plus `_NormalOpacity` / `_MetallicOpacity`; s23 scaled `_DecalBlend`
  and a `_BaseColor` colour, which that material ignores. `DecalOpacitySystem` now scales the vector's w and the normal
  and metallic opacities, prewarms copies while the preview resolves, and logs `decal opacity: made N see-through copies
  (tiers …)` and, per build, `decal opacity: used N see-through decals, M at full strength (why)`.
- **Tiers** 10/15/25/35/50/100 %. Most pools 10 %, wear, stones, glass and wrack 15 %, sand dust and wet sand 25 %; the
  Decals feature's themes 10–15 %. Parking oil rarer and fainter; leaf litter sparser, brown/green only.
- **Colour** (screenshot 58: orange-brown discs at junctions and under lamps on a green lawn, a dark splotch on a light
  path). A soft filter, not a colour-matching engine (lead: hyper-transparent and fewer first): every pool has a host
  ground (`DecalPools.Spec.Host`: lawn, paving, asphalt, sand, dirt) and an automatic pick must not clearly clash with it
  (`DecalColours.NoClash`: the texture's measured mean colour, `DecalColours.g.cs`, 723 decals, against the host colour —
  chroma distance ≤ 30, at most 60 darker (any darker on asphalt), at most 45 brighter; sparse specks under 10 % coverage
  and decals with no measurement pass). The Decals feature's leaves/dirt/cracks/puddles/litter themes use the same check.
  Lawn wear is grass-tone (moss, weeds, wet-grass effects), never the brown Ground Dirt discs; dressing pools no longer
  fall back to broader pools; junction and entrance wear is rarer.

## s28 phase 2: ground wear painted into the terrain (`Core/Decals/GroundWear.cs`)

User idea: use the terrain texture paint (s13 `PaintRaster`; materials 1 Dry grass, 2 Lush grass, 3 Dirt, 4 Sand; soft
feathered edges, per-point strength) as ground dressing. Runs in the dressing pass and needs no decal packs:

- **Verges**: faint dirt (12–18 %) along every path, full at the path edge and fading over ~1.8 m, edge noise.
- **Desire paths**: faint dirt lines (22–30 %, ~1.5 m wide) cutting corners where path arms meet at 35–115°, 7–11 m back
  from the junction, and from the nearest path across the lawn to benches, play items and picnic tables 6–30 m off it.
  A line is drawn only when its whole length is clear: bare terrain, off nets, past tree trunks.
- **Patches**: worn dirt at play items, picnic tables, bench fronts and either side of entrances.
- **Edges**: speckled dry grass (sometimes dirt) discs 3–6 m inside the outline, every ~11 m, to blend into the
  surroundings.
- Paint only where the terrain shows: not under any planned surface (grass included), net, water, paving, sand or
  zoning cutout. Max 360 paints per fill step. Amount = Ground wear × style (Clean 0.6, Lived-in 1, Gritty 1.4).
- Controls in **Decals**: **Ground wear** (`dressPaintWear`, 0–300 %, default 100) and **Desire paths**
  (`dressDesirePaths`, on by default, off by default for Clean-style types; greyed while Ground wear is 0). The old
  `dressGround` slider is now labelled "Open-ground decals". Paint is permanent: the plan carries the Permanent warning
  and the permanent-changes summary lists it under Paint (so a build with ground wear asks for the permanent
  acknowledgement).
- Gallery: `--decals` adds `wear-off` (Ground wear 0 %) and, where the type has a ground-surface toggle, `wear-bare`.

**Which types show it.** Defaults, 120 × 100 m (share of the area under a planned surface → ground-wear paints):

| Lawn under a grass surface (wear invisible, nothing painted) | Bare terrain (wear shows) |
|---|---|
| park, formalGarden, botanicalGarden, cemeteryGrounds, communityGarden, dogPark, greenwayPark, memorialPark, picnicGrounds, adventurePlayground, amphitheatre, animalPark, fairground, sportsPark, skatePark, urbanFarm, shopsPark, outdoorMall, shoppingPlaza, stripMall (all 100 %) | natureReserve 67, wetland 68, zenGarden 94, lookoutHill 63, hikingTrail 54, orchardPark 54, restArea 58, golf 49, urbanRuins 48, campground 45, abandonedLot 40, landfill 39, quarry 39, farmstead 23, beach 15, waterfront 18 |

Paved or yard types (plaza, marketSquare, transitPlaza, parking, logisticsYard, scrapyard, constructionSite, pond) are
fully covered and get none, as intended.

**Recommendation.** A flat grass *surface* hides the terrain, so every lawn-heavy park type gets no ground wear today.
CS2's terrain grass already varies with height and slope, and the paint materials (dry/lush grass, dirt) blend into it
with soft edges, which a uniform tiled grass surface cannot do. For the naturalistic types (park lived-in presets,
picnicGrounds, dogPark, greenwayPark, adventurePlayground, animalPark, campground) leaving the lawn as bare terrain,
with a light lush-grass paint field and this wear on top, is likely to look better. Keep the grass surface for formal,
memorial and cemetery types and for maps whose base terrain is not grass (desert or rock maps: bare terrain would show
sand or rock). Next step (not done here, it changes the types' own layers): an A/B in game with park
`groundSurface` off (`wear-bare` in the gallery) before flipping any defaults.

**s33: done.** The naturalistic types now default to Lawn = Painted terrain (bare terrain + lawn variation, mulch and this
wear, all on the Core/Paint toolkit); manicured types keep the grass surface; every lawn type has the choice in its Ground
paint section. See docs/TERRAIN-PAINT.md.

## Open problems

- In-game confirmation of the sunk-decal trick (renders on the Tunnel mesh layer; projection unaffected?). Fallback: the
  "Decals never collide" setting.
- The projection mask is not used: if packs' masks are DecalLayers, terrain-only decals would not show on paths/lots.
  Filtering needs the mask as a catalog trait (`AssetTraits` has no free bit left).
- Side of one-sided strips (lined leaves, road-side dirt, seaweed lines) is random (180° flips); orientation is right.
- Lawn mowing patterns (Manicured Grass, 6/12 m tiles) are a natural fit for formal gardens and pitches but need a
  lattice layout rule; not used yet.
- Without decal packs nothing decorative is placed (vanilla has none).

<!-- census:begin (generated by DecalsCensusTests, PM_WRITE_DECALS=1) -->

## Census (generated)

Source: `catalog-dump.json v20 2026-10-14T22:24:40.3004482Z` — 960 decal objects (199 vanilla, 761 from asset packs).
Family/shape/ground/orientation come from `Core/Decals/DecalCatalog.Describe` with the real footprint; `pools` = the dressing pools
(`DecalPools`) an item can be picked for automatically (size caps apply when the game builds the pools). `mask` = the pack's
`colossal_DecalLayerMask` (read as DecalLayers: T terrain, R roads/paths, B buildings, O other; blank = vanilla/unknown — the
CustomDecals encoding is unverified, see finding 3). Confidence: ●● item word explicit and pack checked by eye (icons/textures), ● item word only, ○ guessed.

### Counts by family

| family | total | vanilla | functional | depicts |
|---|---|---|---|---|
| LeafLitter | 22 | 0 |  | fallen leaves / foliage, scattered or in drifts |
| LeafEdge | 4 | 0 |  | leaves drifted against one edge ("lined") |
| PalmLitter | 10 | 0 |  | single palm fronds |
| Mulch | 3 | 0 |  | bark mulch / needle bed |
| Moss | 2 | 0 |  | moss, weeds |
| Overgrowth | 20 | 0 |  | grass growing through / patchy grass on dirt |
| Dirt | 28 | 0 |  | bare dirt patch, worn ground |
| Dust | 7 | 0 |  | coloured dust (grey, brown, red) |
| Mud | 28 | 0 |  | mud, road dust, dirt-road surface strips |
| Sand | 12 | 0 |  | sand patch |
| WetSand | 7 | 0 |  | wet sand (bands, patches) |
| Rocks | 6 | 0 |  | stones, gravel, scattered rocks |
| Rubble | 5 | 0 |  | gravel/rubble grit |
| Stain | 39 | 0 |  | grime / dark stain blotch |
| Oil | 5 | 0 |  | oil blot |
| Leak | 4 | 0 |  | drip / leakage streak |
| Puddle | 17 | 0 |  | puddle (mirror water) or water-filled pothole |
| Wet | 19 | 0 |  | wet patch, water splash |
| Crack | 25 | 0 |  | cracks, broken surface |
| Patch | 54 | 0 |  | asphalt / tar / stone repair patch |
| Tyre | 10 | 0 |  | tyre tread / wheel track |
| Footprint | 2 | 0 |  | footprints |
| Litter | 16 | 0 |  | litter, papers, cardboard, rubbish |
| Glass | 11 | 0 |  | broken glass, bottles |
| BeachWrack | 40 | 0 |  | seaweed, shells, foam, sea trash, driftwood |
| Graffiti | 6 | 0 |  | graffiti on the ground |
| Paving | 25 | 0 |  | paving / tiles / cobbles texture |
| LawnPattern | 44 | 0 |  | lawn tile: mowing pattern / grass shape |
| GroundSheet | 95 | 0 |  | large ground texture sheet (aerial, terrain, road side) |
| Marking | 214 | 138 | yes | road / text / arrow / icon marking |
| SportsMarking | 11 | 11 | yes | court, pitch or playground pad |
| ParkingStall | 61 | 44 | yes | parking stall / parking icon |
| FarmTrack | 62 | 0 | yes | farm wheel ruts (laid along rows) |
| Logo | 6 | 6 | yes | Chirper logo |
| BuildingOnly | 40 | 0 | yes | wall / roof / curb / window decal (not ground) |

### Every decal

#### LeafLitter (22) — fallen leaves / foliage, scattered or in drifts

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Fallen leaves decals Leaf Dead Leaves1 Yellow medium Decal | 6×6 | Blob | Any | Random | TRB | ●● |  |
| Fallen leaves decals Leaf Dead Leaves1 Yellow scattered Decal | 6×6 | Blob | Any | Random | TRB | ●● |  |
| Fallen leaves decals Leaf Dead Leaves2 Yellow light Decal | 6×6 | Blob | Any | Random | TRB | ●● |  |
| Fallen leaves decals Leaf Dead Leaves3 Red medium Decal | 6×6 | Blob | Any | Random | TRB | ●● |  |
| Fallen leaves decals Leaf Dead Leaves3 Red scattered Decal | 6×6 | Blob | Any | Random | TRB | ●● |  |
| Fallen leaves decals Leaf Dead Leaves4 Red light Decal | 6×6 | Blob | Any | Random | TRB | ●● |  |
| Fallen leaves decals Leaf Dead Leaves5 Green light Decal | 6×6 | Blob | Any | Random | TRB | ●● |  |
| Fallen leaves decals Leaf Dead Leaves6 Green medium Decal | 6×6 | Blob | Any | Random | TRB | ●● |  |
| Fallen leaves decals Leaf Dead Leaves6 Green scattered Decal | 6×6 | Blob | Any | Random | TRB | ●● |  |
| Fallen leaves decals Leaf Dead Leaves7 Brown medium Decal | 6×6 | Blob | Any | Random | TRB | ●● |  |
| Fallen leaves decals Leaf Dead Leaves7 Brown scattered Decal | 6×6 | Blob | Any | Random | TRB | ●● |  |
| Fallen leaves decals Leaf Dead Leaves8 Brown light Decal | 6×6 | Blob | Any | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Leaf G87 Leaf 01 Decal | 6×6 | Blob | Any | Random | TR | ●● |  |
| G87 Trash Decals Dirty Pack Leaf G87 Leaf 02 Decal | 6×6 | Blob | Any | Random | TR | ●● |  |
| G87 Trash Decals Dirty Pack Leaf G87 Leaf 03 Decal | 5×5 | Blob | Any | Random | TR | ●● |  |
| G87 Trash Decals Dirty Pack Leaf G87 Leaf 04 Decal | 3×3 | Blob | Any | Random | TR | ●● |  |
| G87 Trash Decals Dirty Pack Leaf G87 Leaf 05 Decal | 6×6 | Blob | Any | Random | TR | ●● |  |
| G87 Trash Decals Dirty Pack Leaf G87 Leaf 06 Decal | 6×6 | Blob | Any | Random | TR | ●● |  |
| G87 Trash Decals Dirty Pack Leaf G87 Leaf 07 Decal | 6×6 | Blob | Any | Random | TR | ●● |  |
| G87 Trash Decals Dirty Pack Leaf G87 Leaf 08 Decal | 12×12 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture39 - Foliage Decal | 15×15 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture40 - Foliage Decal | 10×10 | Blob | Any | Random | B | ●● |  |

#### LeafEdge (4) — leaves drifted against one edge ("lined")

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Fallen leaves decals Leaf Dead Leaves1 Yellow lined Decal | 6×6 | Directional | Paved, Grass | AlongHost | TRB | ●● | leaves.edge |
| Fallen leaves decals Leaf Dead Leaves3 Red lined Decal | 6×6 | Directional | Paved, Grass | AlongHost | TRB | ●● | leaves.edge |
| Fallen leaves decals Leaf Dead Leaves6 Green lined Decal | 6×6 | Directional | Paved, Grass | AlongHost | TRB | ●● | leaves.edge |
| Fallen leaves decals Leaf Dead Leaves7 Brown lined Decal | 6×6 | Directional | Paved, Grass | AlongHost | TRB | ●● | leaves.edge |

#### PalmLitter (10) — single palm fronds

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Sand Surfaces and Beach Decals Beach Palm-leaf-01 Decal | 1.3×1.3 | Blob | Paved, Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Palm-leaf-02 Decal | 1.3×1.3 | Blob | Paved, Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Palm-leaf-03 Decal | 1.3×1.3 | Blob | Paved, Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Palm-leaf-04 Decal | 1.3×1.3 | Blob | Paved, Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Palm-leaf-05 Decal | 1.3×1.3 | Blob | Paved, Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Palm-leaf-06 Decal | 1.3×1.3 | Blob | Paved, Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Palm-leaf-07 Decal | 1.3×1.3 | Blob | Paved, Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Palm-leaf-08 Decal | 1.3×1.3 | Blob | Paved, Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Palm-leaf-09 Decal | 1.3×1.3 | Blob | Paved, Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Palm-leaf-10 Decal | 1.3×1.3 | Blob | Paved, Sand | Random | TRB | ●● | beach |

#### Mulch (3) — bark mulch / needle bed

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Ground Texture Decal Pack Ground LargeMulch01 Decal | 4×4 | Blob | Grass, Dirt | Random | TRB | ●● | mulch |
| Ground Texture Decal Pack Ground Mulch01 Decal | 4×4 | Blob | Grass, Dirt | Random | TRBO | ●● | mulch |
| Ground Texture Decal Pack Ground SmMulch01 Decal | 2×2 | Blob | Grass, Dirt | Random | TRB | ●● | mulch |

#### Moss (2) — moss, weeds

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Ground Texture Decal Pack Ground Moss01 Decal | 2.5×2.5 | Blob | Paved, Grass, Dirt | Random | TRBO | ●● | moss wear |
| Ground Texture Decal Pack Ground Weeds01 Decal | 2.5×2.5 | Blob | Paved, Grass, Dirt | Random | TRB | ●● | moss wear |

#### Overgrowth (20) — grass growing through / patchy grass on dirt

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| RealVision Decals Ground Texture06 - Patch Grass Decal | 20×10 | Sheet | Paved, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture109 - Road Side Grass Decal | 15×8 | Strip | Paved, Dirt | AlongHost | TR | ●● | moss |
| RealVision Decals Ground Texture129 - Grass Decal | 10×2 | Strip | Paved, Dirt | AlongHost | TR | ●● | moss |
| RealVision Decals Ground Texture133 - Grass Patch Decal | 10×10 | Blob | Paved, Dirt | Random | B | ●● | moss |
| RealVision Decals Ground Texture141 - Grass Patch Decal | 10×2.5 | Strip | Paved, Dirt | AlongHost | RB | ●● | moss |
| RealVision Decals Ground Texture178 - Grass Patch Decal | 25×15 | Sheet | Paved, Dirt | Random | B | ●● |  |
| RealVision Decals Ground Texture190 - Grass Corner Decal | 10×10 | Blob | Paved, Dirt | Random | TR | ●● | moss |
| RealVision Decals Ground Texture191 - Grass Corner Decal | 10×10 | Blob | Paved, Dirt | Random | TR | ●● | moss |
| RealVision Decals Ground Texture46 - GrassPatch Decal | 10×10 | Blob | Paved, Dirt | Random | B | ●● | moss |
| RealVision Decals Ground Texture47 - GrassPatch Decal | 25×10 | Sheet | Paved, Dirt | Random | B | ●● |  |
| RealVision Decals Ground Texture51 - GrassPatch Decal | 15×10 | Blob | Paved, Dirt | Random | B | ●● | moss |
| RealVision Decals Ground Texture55 - Grass Sand Decal | 40×40 | Sheet | Paved, Dirt | Random | B | ●● |  |
| RealVision Decals Ground Texture56 - Grass Road Side Decal | 15×10 | Blob | Paved, Dirt | Random | TR | ●● | moss |
| RealVision Decals Ground Texture57 - Grass Road Side Decal | 25×15 | Sheet | Paved, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture63 - Grass Patch Decal | 20×20 | Sheet | Paved, Dirt | Random | B | ●● |  |
| RealVision Decals Ground Texture67 - Grass Patch Decal | 10×5 | Strip | Paved, Dirt | AlongHost | TR | ●● | moss |
| RealVision Decals Ground Texture80 - Grass Patch Decal | 20×20 | Sheet | Paved, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture83 - Stones and Grass Decal | 20×20 | Sheet | Paved, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture87 - Grass Path Decal | 35×20 | Sheet | Paved, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture88 - Wild Garden Decal | 25×15 | Sheet | Paved, Dirt | Random | TR | ●● |  |

#### Dirt (28) — bare dirt patch, worn ground

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Ground Texture Decal Pack Ground Dirt01 Decal | 6×6 | Blob | Paved, Grass, Dirt | Random | TRB | ●● | wear mud |
| Ground Texture Decal Pack Ground Dirt02 Decal | 6×6 | Blob | Paved, Grass, Dirt | Random | TRB | ●● | wear mud |
| Ground Texture Decal Pack Ground Dirt03 Decal | 6×6 | Blob | Paved, Grass, Dirt | Random | TRB | ●● | wear mud |
| Ground Texture Decal Pack Ground Dirt04 Decal | 6×6 | Blob | Paved, Grass, Dirt | Random | TRB | ●● | wear mud |
| RealVision Decals Ground Texture117 - Dirt Path Decal | 25×15 | Sheet | Paved, Grass, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture118 - Dirt Patch Decal | 25×15 | Sheet | Paved, Grass, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture127 - Dirt Terrain Decal | 30×30 | Sheet | Paved, Grass, Dirt | Random | B | ●● |  |
| RealVision Decals Ground Texture128 - Dirt Terrain Decal | 30×30 | Sheet | Paved, Grass, Dirt | Random | B | ●● |  |
| RealVision Decals Ground Texture130 - Road Side Dirt Decal | 10×2 | Strip | Paved, Grass, Dirt | AlongHost | TR | ●● | wear mud |
| RealVision Decals Ground Texture132 - Dirt Terrain Decal | 30×15 | Sheet | Paved, Grass, Dirt | Random | B | ●● |  |
| RealVision Decals Ground Texture134 - Dirt Broken Pavement Decal | 20×5 | Sheet | Paved, Grass, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture145 - Road Side Dirt Decal | 20×5 | Sheet | Paved, Grass, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture151 - Dirt Path Decal | 20×15 | Sheet | Paved, Grass, Dirt | Random | B | ●● |  |
| RealVision Decals Ground Texture152 - Dirt Patch Decal | 10×2.5 | Strip | Paved, Grass, Dirt | AlongHost | RB | ●● | wear mud |
| RealVision Decals Ground Texture182 - Dirt Path Decal | 22×12 | Sheet | Paved, Grass, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture20 - Dirt Decal | 40×40 | Sheet | Paved, Grass, Dirt | Random | B | ●● |  |
| RealVision Decals Ground Texture20 - DirtPatch Decal | 20×15 | Sheet | Paved, Grass, Dirt | Random | TRB | ●● |  |
| RealVision Decals Ground Texture216 - Dirt Park Decal | 60×60 | Sheet | Paved, Grass, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture43 - Dirt Decal | 20×20 | Sheet | Paved, Grass, Dirt | Random | B | ●● |  |
| RealVision Decals Ground Texture44 - Dirt Decal | 15×10 | Blob | Paved, Grass, Dirt | Random | T | ●● | wear mud |
| RealVision Decals Ground Texture49 - Dirt Decal | 25×10 | Sheet | Paved, Grass, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture50 - Dirt Decal | 20×10 | Sheet | Paved, Grass, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture54 - Dirt Decal | 20×15 | Sheet | Paved, Grass, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture65 - DirtPath Decal | 60×60 | Sheet | Paved, Grass, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture66 - DirtTerrain Decal | 10×10 | Blob | Paved, Grass, Dirt | Random | B | ●● | wear mud |
| RealVision Decals Ground Texture81 - Dirt Curve Decal | 40×20 | Sheet | Paved, Grass, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture82 - Dirt Patch Decal | 20×20 | Sheet | Paved, Grass, Dirt | Random | TR | ●● |  |
| RealVision Decals Ground Texture86 - Dirt Decal | 30×15 | Sheet | Paved, Grass, Dirt | Random |  | ●● |  |

#### Dust (7) — coloured dust (grey, brown, red)

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Trash Decals Dirty Pack Trash G87 Dust 01 Grey Decal | 10×10 | Blob | Paved, Dirt | Random | TRB | ●● | wear mud sand stain |
| G87 Trash Decals Dirty Pack Trash G87 Dust 02 Brown Decal | 10×10 | Blob | Paved, Dirt | Random | TRB | ●● | wear mud sand stain |
| G87 Trash Decals Dirty Pack Trash G87 Dust 03 Red Decal | 10×10 | Blob | Paved, Dirt | Random | TRB | ●● | wear mud sand stain |
| RealVision Decals Ground Texture90 - Road Dust Decal | 15×10 | Blob | Paved, Dirt | Random | RB | ●● | wear mud sand stain |
| RealVision Decals Ground Texture91 - Road Dust Decal | 25×15 | Sheet | Paved, Dirt | Random | RB | ●● |  |
| RealVision Decals Ground Texture92 - Road Dust Decal | 20×10 | Sheet | Paved, Dirt | Random | RB | ●● |  |
| RealVision Decals Ground Texture93 - Road Dust Decal | 20×10 | Sheet | Paved, Dirt | Random | B | ●● |  |

#### Mud (28) — mud, road dust, dirt-road surface strips

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Dirty Roads Stains G87 Dirt Road Transition 01 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 02 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 03 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 04 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 05 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 06 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 07 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 08 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 09 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 10 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 11 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 12 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 13 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 14 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 15 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 16 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 17 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 18 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 19 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 20 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 21 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Dirt Road Transition 22 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Gravel Road Transition 01 Decal | 10×5 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Gravel Road Transition 02 Decal | 10×5 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Gravel Road Transition 03 Decal | 10×5 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| G87 Dirty Roads Stains G87 Gravel Road Transition 04 Decal | 12×6 | Strip | Paved, Dirt | AlongHost | TR | ●● | mud |
| RealVision Decals Ground Texture146 - Dirty Road Decal | 25×15 | Sheet | Paved, Dirt | AlongHost | TR | ●● |  |
| RealVision Decals Ground Texture147 - Muddy Stain Decal | 20×5 | Sheet | Paved, Dirt | AlongHost | RB | ●● |  |

#### Sand (12) — sand patch

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Golden Sand Beaches Texture Pack Beach Golden Sand Base Decal | 8.2×8.2 | Blob | Paved, Sand | Random | TR | ●● | sand |
| Golden Sand Beaches Texture Pack Beach Golden Sand Medium Decal | 8.2×8.2 | Blob | Paved, Sand | Random | TR | ●● | sand |
| Golden Sand Beaches Texture Pack Beach Golden Sand Path Decal | 8×2 | Strip | Paved, Sand | AlongHost | TRB | ●● | sand |
| Golden Sand Beaches Texture Pack Beach Golden Sand Thick Decal | 8.2×8.2 | Blob | Paved, Sand | Random | TR | ●● | sand |
| Golden Sand Beaches Texture Pack Beach Golden Sand Thin Decal | 8.2×8.2 | Blob | Paved, Sand | Random | TR | ●● | sand |
| RealVision Decals Ground Texture113 - Dirty Sand Decal | 15×15 | Blob | Paved, Sand | Random | TRB | ●● | sand |
| RealVision Decals Ground Texture135 - Beach Sand Decal | 25×15 | Sheet | Paved, Sand | Random | TR | ●● |  |
| RealVision Decals Ground Texture213 - Sand Decal | 60×30 | Sheet | Paved, Sand | Random | TR | ●● |  |
| RealVision Decals Ground Texture219 - Sand Decal | 70×70 | Sheet | Paved, Sand | Random | B | ●● |  |
| RealVision Decals Ground Texture220 - Sand Decal | 60×30 | Sheet | Paved, Sand | Random | B | ●● |  |
| RealVision Decals Ground Texture221 - Sand Brush Decal | 30×20 | Sheet | Paved, Sand | Random | TR | ●● |  |
| RealVision Decals Ground Texture45 - Sand Decal | 25×10 | Sheet | Paved, Sand | Random | T | ●● |  |

#### WetSand (7) — wet sand (bands, patches)

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 17 Band A Wet Sand Decal | 2×6 | Strip | Sand | AlongHost | TRB | ●● | wetsand |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 18 Band B Wet Sand Decal | 2×6 | Strip | Sand | AlongHost | TRB | ●● | wetsand |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 094 B 50 Medium Sand Decal | 10×10 | Blob | Sand | Random | TRB | ●● | wetsand |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 100 C 50 Medium Sand Decal | 10×10 | Blob | Sand | Random | TRB | ●● | wetsand |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 101 C 70 Medium Sand Decal | 10×10 | Blob | Sand | Random | TRB | ●● | wetsand |
| Golden Sand Beaches Texture Pack Beach Golden Sand Wet D Decal | 30×15 | Sheet | Sand | Random | TR | ●● |  |
| Golden Sand Beaches Texture Pack Beach Golden Sand Wet L Decal | 30×15 | Sheet | Sand | Random | TR | ●● |  |

#### Rocks (6) — stones, gravel, scattered rocks

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Ground Texture Decal Pack Ground ScatteredRocks01 Decal | 3×3 | Blob | Paved, Grass, Dirt | Random | TRB | ●● | rocks |
| RealVision Decals Ground Texture185 - Dirty Stones Decal | 20×10 | Sheet | Paved, Grass, Dirt | Random | BO | ●● |  |
| RealVision Decals Ground Texture210 - Gravel And Stone Decal | 10×5 | Strip | Paved, Grass, Dirt | AlongHost | TR | ●● | rocks |
| RealVision Decals Ground Texture214 - Stones Decal | 10×5 | Strip | Paved, Grass, Dirt | AlongHost | B | ●● | rocks |
| RealVision Decals Ground Texture215 - Stones Decal | 90×45 | Sheet | Paved, Grass, Dirt | Random | B | ●● |  |
| RealVision Decals Ground Texture218 - Aerial Stones Decal | 40×20 | Sheet | Paved, Grass, Dirt | Random | B | ●● |  |

#### Rubble (5) — gravel/rubble grit

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Trash Decals Dirty Pack Trash G87 Debris 01 Decal | 2×2 | Blob | Paved, Grass, Dirt | Random | TRB | ●● | rocks |
| G87 Trash Decals Dirty Pack Trash G87 Debris 02 Decal | 2×2 | Blob | Paved, Grass, Dirt | Random | TRB | ●● | rocks |
| G87 Trash Decals Dirty Pack Trash G87 Debris 03 Decal | 2×2 | Blob | Paved, Grass, Dirt | Random | TRB | ●● | rocks |
| G87 Trash Decals Dirty Pack Trash G87 Debris 04 Decal | 2×2 | Blob | Paved, Grass, Dirt | Random | TRB | ●● | rocks |
| G87 Trash Decals Dirty Pack Trash G87 Debris 05 Decal | 5×5 | Blob | Paved, Grass, Dirt | Random | TRB | ●● | rocks |

#### Stain (39) — grime / dark stain blotch

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 01 A S Decal | 5×5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 02 A M Decal | 12.5×12.5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 03 A L Decal | 20×20 | Sheet | Paved | Random | TRB | ●● |  |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 04 B S Decal | 5×5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 05 B M Decal | 12.5×12.5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 06 B L Decal | 20×20 | Sheet | Paved | Random | TRB | ●● |  |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 07 C S Decal | 5×5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 08 C M Decal | 12.5×12.5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 09 C L Decal | 20×20 | Sheet | Paved | Random | TRB | ●● |  |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 10 D S Decal | 5×10 | Strip | Paved | AlongHost | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 11 D L Decal | 8×14 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 12 E S Decal | 5×5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 13 E M Decal | 12.5×12.5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 14 E L Decal | 20×20 | Sheet | Paved | Random | TRB | ●● |  |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 15 F S Decal | 5×5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 16 F M Decal | 12.5×12.5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 17 F L Decal | 20×20 | Sheet | Paved | Random | TRB | ●● |  |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 18 G S Decal | 5×5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 19 G M Decal | 12.5×12.5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 20 G L Decal | 20×20 | Sheet | Paved | Random | TRB | ●● |  |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 21 H S Decal | 5×5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 22 H M Decal | 12.5×12.5 | Blob | Paved | Random | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 23 H L Decal | 20×20 | Sheet | Paved | Random | TRB | ●● |  |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 40 Band A Decal | 2×6 | Strip | Paved | AlongHost | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 41 Band B Decal | 2×6 | Strip | Paved | AlongHost | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 44 Trio Decal | 2×6 | Strip | Paved | AlongHost | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 45 Lone Medium Decal | 4×2 | Strip | Paved | AlongHost | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 46 Wide Decal | 2×6 | Strip | Paved | AlongHost | TRB | ●● | stain oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 47 Narrow Decal | 2×6 | Strip | Paved | AlongHost | TRB | ●● | stain oil |
| Light Stains - Decals RoadAssets Stain Rectangle L Decal | 16×32 | Sheet | Paved | Random |  | ● |  |
| Light Stains - Decals RoadAssets Stain Rectangle M Decal | 8×16 | Sheet | Paved | Random |  | ● |  |
| Light Stains - Decals RoadAssets Stain Square L Decal | 16×16 | Sheet | Paved | Random |  | ● |  |
| Light Stains - Decals RoadAssets Stain Square M Decal | 8×8 | Blob | Paved | Random |  | ● | stain oil |
| Stains and Leakage Decal Pack Stains Stain 1 - Large Decal | 20×20 | Sheet | Paved | Random | TR | ●● |  |
| Stains and Leakage Decal Pack Stains Stain 1 - Small Decal | 12×12 | Blob | Paved | Random | TR | ●● | stain oil |
| Stains and Leakage Decal Pack Stains Stain 2 - Large Decal | 40×10 | Sheet | Paved | Random | TR | ●● |  |
| Stains and Leakage Decal Pack Stains Stain 2 - Small Decal | 20×5 | Sheet | Paved | Random | TR | ●● |  |
| Stains and Leakage Decal Pack Stains Stain 3 - Large Decal | 35×17.5 | Sheet | Paved | Random | TR | ●● |  |
| Stains and Leakage Decal Pack Stains Stain 3 - Small Decal | 20×10 | Sheet | Paved | Random | TR | ●● |  |

#### Oil (5) — oil blot

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 1 Decal | 3×3 | Blob | Paved | Random | TRB | ●● | oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 2 Decal | 3×3 | Blob | Paved | Random | TRB | ●● | oil |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Oil 3 Decal | 3×3 | Blob | Paved | Random | TRB | ●● | oil |
| RealVision Decals Ground Texture125 - Oil Decal | 10×5 | Strip | Paved | AlongHost | TR | ●● | oil |
| RealVision Decals Ground Texture126 - Oil Decal | 10×5 | Strip | Paved | AlongHost | TR | ●● | oil |

#### Leak (4) — drip / leakage streak

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Stains and Leakage Decal Pack Stains Leakage 1 - Large Decal | 16×8 | Directional | Paved | AlongHost | TRB | ●● | oil |
| Stains and Leakage Decal Pack Stains Leakage 1 - Small Decal | 10×5 | Directional | Paved | AlongHost | TRB | ●● | oil |
| Stains and Leakage Decal Pack Stains Leakage 2 - Large Decal | 16×8 | Directional | Paved | AlongHost | TRB | ●● | oil |
| Stains and Leakage Decal Pack Stains Leakage 2 - Small Decal | 10×5 | Directional | Paved | AlongHost | TRB | ●● | oil |

#### Puddle (17) — puddle (mirror water) or water-filled pothole

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Pothole 40 Trio Decal | 2×6 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Pothole 41 Long Medium Decal | 4×2 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Pothole 42 Wide Decal | 2×6 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Pothole 43 Slim Decal | 2×6 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Pothole 44 Trio Small Decal | 4×2 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Pothole 45 Small Decal | 1×1 | Tiny | Paved, Dirt | Random | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Pothole 46 Medium Decal | 2×2 | Blob | Paved, Dirt | Random | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 11 Band A Decal | 2×6 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 12 Band A Wet Decal | 2×6 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 13 Band B Decal | 2×6 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 14 Band B Wet Decal | 2×6 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 15 Band A Wet Grass Decal | 2×6 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 16 Band B Wet Grass Decal | 2×6 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 21 Trio Wet Decal | 2×6 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 22 Lone Medium Wet Decal | 4×2 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 23 Wide Wet Decal | 2×6 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 24 Narrow Wet Decal | 2×6 | Strip | Paved, Dirt | AlongHost | TRB | ●● | puddles |

#### Wet (19) — wet patch, water splash

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Water Splash 80 A S Decal | 1×1 | Tiny | Any | Random | TRBO | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Water Splash 81 A M Decal | 3×3 | Blob | Any | Random | TRBO | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Water Splash 83 B S Decal | 1×1 | Tiny | Any | Random | TRBO | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Water Splash 84 B M Decal | 3×3 | Blob | Any | Random | TRBO | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Water Splash 86 C S Decal | 1×1 | Tiny | Any | Random | TRBO | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Water Splash 87 C M Decal | 3×3 | Blob | Any | Random | TRBO | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 090 A 50 Medium Decal | 10×10 | Blob | Any | Random | TRB | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 090 A 50 Medium Prop 1S Decal | 2×2 | Blob | Any | Random | TRBO | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 090 A 50 Medium Prop 2M Decal | 5×5 | Blob | Any | Random | TRBO | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 090 A 50 Medium Prop 3L Decal | 10×10 | Blob | Any | Random | TRBO | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 091 A 70 Medium Decal | 10×10 | Blob | Any | Random | TRB | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 092 B 50 Medium Decal | 10×10 | Blob | Any | Random | TRB | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 093 B 50 Medium Grass Decal | 10×10 | Blob | Any | Random | TRB | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 095 B 70 Medium Decal | 10×10 | Blob | Any | Random | TRB | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 096 C 50 Medium Decal | 10×10 | Blob | Any | Random | TRB | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 097 C 70 Medium Decal | 10×10 | Blob | Any | Random | TRB | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 098 C 50 Medium Grass Decal | 10×10 | Blob | Any | Random | TRB | ●● | wear wet |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Wet Effect 099 C 70 Medium Grass Decal | 10×10 | Blob | Any | Random | TRB | ●● | wear wet |
| RealVision Decals Ground Texture222 - Wave Crashing Decal | 30×20.5 | Sheet | Any | Random | B | ●● |  |

#### Crack (25) — cracks, broken surface

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Cracks and Damage Decal Pack RoadAssets Crack - Medium 1 Decal | 1.5×1.5 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Crack - Medium 2 Decal | 1.5×1.5 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Crack - Medium 3 Decal | 1.5×1.5 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Crack - Small 1 Decal | 1.8×1.8 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Crack - Small 2 Decal | 1.3×1.3 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Crack - Small 3 Decal | 1.5×1.5 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Crack - Small 4 Decal | 1.3×1.3 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Crack - Small 5 Decal | 1.5×1.5 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Cracks - XL Decal | 8×4 | Strip | Paved | AlongHost | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Damage - Large 1 Decal | 5×5 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Damage - Large 2 Decal | 3×3 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Damage - Large 3 Decal | 2×2 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Damage - Large 4 Decal | 1.5×1.5 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Damage - Large 5 Decal | 2.5×2.5 | Blob | Paved | Random | TR | ●● |  |
| Cracks and Damage Decal Pack RoadAssets Damage - XL Decal | 12×6 | Strip | Paved | AlongHost | TR | ●● |  |
| RealVision Decals Ground Texture137 - Road Damage Decal | 20×10 | Sheet | Paved | Random | RB | ●● |  |
| RealVision Decals Ground Texture138 - Road Damage Decal | 20×10 | Sheet | Paved | Random | RB | ●● |  |
| RealVision Decals Ground Texture139 - Road Damage Decal | 20×10 | Sheet | Paved | Random | RB | ●● |  |
| RealVision Decals Ground Texture181 - Cracked Concrete Decal | 25×20 | Sheet | Paved | Random | TR | ●● |  |
| RealVision Decals Ground Texture192 - Damaged Stone Decal | 10×5 | Strip | Paved | AlongHost | TR | ●● |  |
| RealVision Decals Ground Texture37 - Road Crack Decal | 15×10 | Blob | Paved | Random | RB | ●● |  |
| RealVision Decals Ground Texture38 - Road Crack Decal | 15×10 | Blob | Paved | Random | RB | ●● |  |
| RealVision Decals Ground Texture78 - Damaged Decal | 15×15 | Blob | Paved | Random | RB | ●● |  |
| RealVision Decals Ground Texture96 - Road Damage Decal | 10×5 | Strip | Paved | AlongHost | RB | ●● |  |
| RealVision Decals Ground Texture97 - Road Damage Decal | 10×5 | Strip | Paved | AlongHost | RB | ●● |  |

#### Patch (54) — asphalt / tar / stone repair patch

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 1 Decal | 2×2 | Blob | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 1 Small Decal | 1×1 | Tiny | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 2 Decal | 2.2×2.2 | Blob | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 2 Small Decal | 1.3×1.3 | Blob | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 3 Decal | 3.2×1.6 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 3 Small Decal | 2.2×1.1 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 4 Decal | 2×2 | Blob | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 4 Small Decal | 1.2×1.2 | Tiny | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 5 Decal | 2.5×2.5 | Blob | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 5 Small Decal | 1.5×1.5 | Blob | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 6 Decal | 3×1.5 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 6 Small Decal | 1.5×0.8 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 7 Decal | 3.5×1.8 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 7 Small Decal | 2×1 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 8 Decal | 4×2 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Dark - 8 Small Decal | 2.3×1.2 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 1 Decal | 2×2 | Blob | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 1 Small Decal | 1×1 | Tiny | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 2 Decal | 2.2×2.2 | Blob | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 2 Small Decal | 1.3×1.3 | Blob | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 3 Decal | 3.2×1.6 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 3 Small Decal | 2.2×1.1 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 4 Decal | 2×2 | Blob | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 4 Small Decal | 1.2×1.2 | Tiny | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 5 Decal | 2.5×2.5 | Blob | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 5 Small Decal | 1.5×1.5 | Blob | Paved | Random | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 6 Decal | 3×1.5 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 6 Small Decal | 1.5×0.8 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 7 Decal | 3.5×1.8 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 7 Small Decal | 2×1 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 8 Decal | 4×2 | Strip | Paved | AlongHost | TR | ●● | patch |
| Asphalt Patches Decal Pack RoadAssets Asphalt Patch Light - 8 Small Decal | 2.3×1.2 | Strip | Paved | AlongHost | TR | ●● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 01 Small 625 Decal | 0.6×0.6 | Tiny | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 03 Medium 1250 Decal | 1.3×1.3 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 05 Large 2500 Decal | 2.5×2.5 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 07 XLarge 5000 Decal | 5.1×5.1 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 09 XXLarge 10000 Decal | 10.2×10.2 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 10 Small 625 Under Decal | 0.6×0.6 | Tiny | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 11 Medium 1250 Under Decal | 1.3×1.3 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 12 Large 2500 GO Decal | 2.5×2.5 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 12 Large 2500 Under Decal | 2.5×2.5 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 13 XLarge 5000 GO Decal | 5.1×5.1 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 13 XLarge 5000 Under Decal | 5.1×5.1 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 14 XXLarge 10000 GO Decal | 10.2×10.2 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 14 XXLarge 10000 Under Decal | 10.2×10.2 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 21 Small 625 Worn VFX Decal | 0.6×0.6 | Tiny | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 23 Medium 1250 Worn VFX Decal | 1.3×1.3 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 25 Large 2500 Worn VFX Decal | 2.5×2.5 | Blob | Paved | Random |  | ● | patch |
| G87 Vanilla Asphalt RoadAssets G87 VA 27 XLarge 5000 Worn VFX Decal | 5.1×5.1 | Blob | Paved | Random |  | ● | patch |
| RealVision Decals Ground Texture114 - Tar Patch Decal | 10×5 | Strip | Paved | AlongHost | RB | ●● | patch |
| RealVision Decals Ground Texture115 - Tar Patch Decal | 10×5 | Strip | Paved | AlongHost | RB | ●● | patch |
| RealVision Decals Ground Texture116 - Road Patch Decal | 30×15 | Sheet | Paved | Random | TRB | ●● |  |
| RealVision Decals Ground Texture177 - Cobble Wide Patch Decal | 7×4 | Blob | Paved | Random | TR | ●● | patch |
| RealVision Decals Ground Texture196 - Stone Patch Decal | 10×2.5 | Strip | Paved | AlongHost | TR | ●● | patch |

#### Tyre (10) — tyre tread / wheel track

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 19 Wet Track Short Decal | 2×6 | Directional | Paved, Dirt, Sand | AlongHost | TRB | ●● | tyre |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 20 Wet Track Long Decal | 2×12 | Directional | Paved, Dirt, Sand | AlongHost | TRB | ●● | tyre |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 42 Track Short Decal | 2×6 | Directional | Paved, Dirt, Sand | AlongHost | TRB | ●● | tyre |
| G87 Stains and Puddles Decals Wet Pack Stains G87 Stain 43 Track Long Decal | 2×12 | Directional | Paved, Dirt, Sand | AlongHost | TRB | ●● | tyre |
| Sand Surfaces and Beach Decals Beach Tire-Track-01 Decal | 2×2 | Directional | Paved, Dirt, Sand | AlongHost | TRB | ●● | tyre |
| Sand Surfaces and Beach Decals Beach Tire-Track-01-Small Decal | 0.3×0.3 | Directional | Paved, Dirt, Sand | AlongHost | TRB | ●● | tyre |
| Sand Surfaces and Beach Decals Beach Tire-Track-02-Small Decal | 0.4×0.4 | Directional | Paved, Dirt, Sand | AlongHost | TRB | ●● | tyre |
| Sand Surfaces and Beach Decals Beach Tire-Track-03-Small Decal | 0.3×0.3 | Directional | Paved, Dirt, Sand | AlongHost | TRB | ●● | tyre |
| Sand Surfaces and Beach Decals Beach Tire-track-02 Decal | 2×2 | Directional | Paved, Dirt, Sand | AlongHost | TRB | ●● | tyre |
| Sand Surfaces and Beach Decals Beach Tire-track-03 Decal | 2×2 | Directional | Paved, Dirt, Sand | AlongHost | TRB | ●● | tyre |

#### Footprint (2) — footprints

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Sand Surfaces and Beach Decals Beach Footprint-01-Left Decal | 0.3×0.3 | Directional | Sand | AlongHost | TRB | ●● | footprints |
| Sand Surfaces and Beach Decals Beach Footprint-01-Right Decal | 0.3×0.3 | Directional | Sand | AlongHost | TRB | ●● | footprints |

#### Litter (16) — litter, papers, cardboard, rubbish

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Trash Decals Dirty Pack Trash G87 Litter 01 Decal | 2×2 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Litter 02 Decal | 1×1 | Tiny | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Litter 03 Decal | 1×1 | Tiny | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Litter 04 Papers 01 Small Decal | 5×5 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Litter 05 Papers 01 Large Decal | 10×10 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Litter 06 Papers 02 Large Decal | 10×10 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Litter 07 Papers 03 Decal | 4×4 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Litter 08 Papers 04 Decal | 4×4 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Trash Cardboard 01 Decal | 3×3 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Trash Cardboard 02 Decal | 5×5 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Trash Cardboard 03 Decal | 5×5 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Trash Cardboard 04 Decal | 3.5×3.5 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Trash Cardboard 05 Decal | 3.5×3.5 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Trash Misc 01 Decal | 1.5×1.5 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Trash Misc 02 Decal | 1.5×1.5 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |
| G87 Trash Decals Dirty Pack Trash G87 Trash Misc 03 Decal | 2×2 | Blob | Paved, Grass, Dirt | Random | TRB | ●● |  |

#### Glass (11) — broken glass, bottles

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Trash Decals Dirty Pack Trash G87 Debris 11 Glass Bottle Brown Decal | 0.7×0.7 | Tiny | Paved, Grass, Dirt | Random | TRB | ●● | glass |
| G87 Trash Decals Dirty Pack Trash G87 Debris 12 Glass Bottle Green Decal | 0.7×0.7 | Tiny | Paved, Grass, Dirt | Random | TRB | ●● | glass |
| G87 Trash Decals Dirty Pack Trash G87 Debris 13 Glass Bottle Misc Decal | 0.7×0.7 | Tiny | Paved, Grass, Dirt | Random | TRB | ●● | glass |
| G87 Trash Decals Dirty Pack Trash G87 Debris 14 Broken Mirror Decal | 1×1 | Tiny | Paved, Grass, Dirt | Random | TRB | ●● | glass |
| G87 Trash Decals Dirty Pack Trash G87 Debris 15 Glass Spreaded Decal | 2×2 | Blob | Paved, Grass, Dirt | Random | TRB | ●● | glass |
| G87 Trash Decals Dirty Pack Trash G87 Debris 16 Glass Green Small Decal | 0.7×0.7 | Tiny | Paved, Grass, Dirt | Random | TRB | ●● | glass |
| G87 Trash Decals Dirty Pack Trash G87 Debris 17 Glass Brown Small Decal | 0.7×0.7 | Tiny | Paved, Grass, Dirt | Random | TRB | ●● | glass |
| G87 Trash Decals Dirty Pack Trash G87 Debris 18 Glass Small 01 Decal | 1×1 | Tiny | Paved, Grass, Dirt | Random | TRB | ●● | glass |
| G87 Trash Decals Dirty Pack Trash G87 Debris 19 Glass Small 02 Decal | 1×1 | Tiny | Paved, Grass, Dirt | Random | TRB | ●● | glass |
| G87 Trash Decals Dirty Pack Trash G87 Debris 20 Glass Small 03 Decal | 1×1 | Tiny | Paved, Grass, Dirt | Random | TRB | ●● | glass |
| G87 Trash Decals Dirty Pack Trash G87 Debris 21 Glass Small 04 Decal | 1×1 | Tiny | Paved, Grass, Dirt | Random | TRB | ●● | glass |

#### BeachWrack (40) — seaweed, shells, foam, sea trash, driftwood

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Trash Decals Dirty Pack Trash G87 Sea Trash 01 Decal | 3×3 | Blob | Sand | AlongHost | TRBO | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Sea Trash 02 Decal | 3×3 | Blob | Sand | AlongHost | TRBO | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Sea Trash 03 Decal | 1.5×1.5 | Blob | Sand | AlongHost | TRBO | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Sea Trash 04 Decal | 0.5×0.5 | Tiny | Sand | Random | TRBO | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Sea Trash 05 Decal | 0.5×0.5 | Tiny | Sand | Random | TRBO | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Sea Trash 06 Decal | 0.5×0.5 | Tiny | Sand | Random | TRBO | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Sea Trash 07 Decal | 1.2×1.2 | Blob | Sand | AlongHost | TRBO | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Sea Trash 08 Decal | 3.5×1.8 | Strip | Sand | AlongHost | TRBO | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Seaweed 01 Brown Decal | 8×4 | Strip | Sand | AlongHost | TRB | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Seaweed 02 Brown Decal | 8×4 | Strip | Sand | AlongHost | TRB | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Seaweed 03 Brown Decal | 8×4 | Strip | Sand | AlongHost | TRB | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Seaweed Red 01 Decal | 8×4 | Strip | Sand | AlongHost | TRB | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Seaweed Red 02 Decal | 8×4 | Strip | Sand | AlongHost | TRB | ●● | beach |
| G87 Trash Decals Dirty Pack Trash G87 Seaweed Red 03 Decal | 8×4 | Strip | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Debris-01 Decal | 1.3×1.3 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Debris-02 Decal | 1.3×1.3 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Debris-03 Decal | 1.3×1.3 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Debris-04 Decal | 1.3×1.3 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Debris-05 Decal | 1.3×1.3 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seafoam-01 Decal | 1.3×1.3 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seafoam-02 Decal | 1.3×1.3 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seafoam-03 Decal | 1.3×1.3 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seafoam-04 Decal | 1.3×1.3 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seashells-01 Decal | 2×2 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seashells-02 Decal | 3×3 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seashells-03 Decal | 4×4 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seashells-04 Decal | 2×2 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seashells-05 Decal | 2×2 | Blob | Sand | AlongHost | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seaweed-01 Decal | 1×1 | Tiny | Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seaweed-02 Decal | 1×1 | Tiny | Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seaweed-03 Decal | 1.2×1.2 | Tiny | Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seaweed-04 Decal | 1.2×1.2 | Tiny | Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seaweed-05 Decal | 1×1 | Tiny | Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seaweed-06 Decal | 1×1 | Tiny | Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seaweed-07 Decal | 1.1×1.1 | Tiny | Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seaweed-08 Decal | 1×1 | Tiny | Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seaweed-09 Decal | 1×1 | Tiny | Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seaweed-10-Hibiscus Decal | 1.1×1.1 | Tiny | Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seaweed-11-Hibiscus Decal | 1.1×1.1 | Tiny | Sand | Random | TRB | ●● | beach |
| Sand Surfaces and Beach Decals Beach Seaweed-12 Decal | 1×1 | Tiny | Sand | Random | TRB | ●● | beach |

#### Graffiti (6) — graffiti on the ground

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| RealVision Decals Ground Graff - 01 Decal | 6×6 | Blob | Paved | Random | BO | ●● |  |
| RealVision Decals Ground Graff - 02 Decal | 10×5 | Strip | Paved | AlongHost | TRB | ●● |  |
| RealVision Decals Ground Graff - 03 Decal | 10×5 | Strip | Paved | AlongHost | TRB | ●● |  |
| RealVision Decals Ground Graff - 04 Decal | 8×4 | Strip | Paved | AlongHost | TRB | ●● |  |
| RealVision Decals Ground Graff - 05 Decal | 8×4 | Strip | Paved | AlongHost | TRB | ●● |  |
| RealVision Decals Ground Graff - 06 Decal | 8×4 | Strip | Paved | AlongHost | TRB | ●● |  |

#### Paving (25) — paving / tiles / cobbles texture

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| EDT Cobblestone & Gravel Surface Texture Pack RoadAssets Sidewalk Base Decal | 4.1×4.1 | Tile | Paved | Lattice | TR | ● |  |
| EDT Cobblestone & Gravel Surface Texture Pack RoadAssets Sidewalk Base Square Decal | 4.1×4.1 | Tile | Paved | Lattice | TR | ● |  |
| EDT Cobblestone & Gravel Surface Texture Pack RoadAssets Sidewalk Dark Base Decal | 4.1×4.1 | Tile | Paved | Lattice | TR | ● |  |
| EDT Cobblestone & Gravel Surface Texture Pack RoadAssets Sidewalk Dark Base Square Decal | 4.1×4.1 | Tile | Paved | Lattice | TR | ● |  |
| EDT Cobblestone & Gravel Surface Texture Pack RoadAssets Sidewalk Dark Thick Decal | 4.1×4.1 | Tile | Paved | Lattice | TR | ● |  |
| EDT Cobblestone & Gravel Surface Texture Pack RoadAssets Sidewalk Dark Thin Decal | 4.1×4.1 | Tile | Paved | Lattice | TR | ● |  |
| EDT Cobblestone & Gravel Surface Texture Pack RoadAssets Sidewalk Thick Decal | 4.1×4.1 | Tile | Paved | Lattice | TR | ● |  |
| EDT Cobblestone & Gravel Surface Texture Pack RoadAssets Sidewalk Thin Decal | 4.1×4.1 | Tile | Paved | Lattice | TR | ● |  |
| G87 UP Grass Ground G87 UP Tiles x 1 Decal | 1.1×1.1 | Tile | Paved | Lattice |  | ● |  |
| G87 UP Grass Ground G87 UP Tiles x 2 Decal | 1.1×2.1 | Tile | Paved | Lattice |  | ● |  |
| G87 UP Grass Ground G87 UP Tiles x 4 Decal | 1.1×4.3 | Tile | Paved | Lattice |  | ● |  |
| G87 UP Grass Ground G87 UP Tiles x 8 Decal | 1.1×8.6 | Tile | Paved | Lattice |  | ● |  |
| RealVision Decals Ground Texture102 - Garden Tiles Decal | 8×8 | Tile | Paved | Lattice | TRB | ●● |  |
| RealVision Decals Ground Texture111 - Pavement Decal | 20×10 | Sheet | Paved | Random | TR | ●● |  |
| RealVision Decals Ground Texture140 - Pavement Tiles Decal | 15×5 | Tile | Paved | Lattice | RB | ●● |  |
| RealVision Decals Ground Texture143 - Pavement Decal | 10×5 | Tile | Paved | Lattice | TR | ●● |  |
| RealVision Decals Ground Texture144 - Pavement Decal | 8×8 | Tile | Paved | Lattice | TR | ●● |  |
| RealVision Decals Ground Texture149 - Sandy Cobble SideWalk Decal | 15×5 | Tile | Paved | Lattice | TR | ●● |  |
| RealVision Decals Ground Texture176 - Cobble SideWalk Decal | 15×5 | Tile | Paved | Lattice | TR | ●● |  |
| RealVision Decals Ground Texture179 - Stone Pavement Decal | 10×5 | Tile | Paved | Lattice | B | ●● |  |
| RealVision Decals Ground Texture180 - Concrete Slab Decal | 25×20 | Sheet | Paved | Random | B | ●● |  |
| RealVision Decals Ground Texture189 - Old Cobble Decal | 12×7 | Tile | Paved | Lattice | B | ●● |  |
| RealVision Decals Ground Texture190 - Round Cobble Decal | 10×10 | Tile | Paved | Lattice | B | ●● |  |
| RealVision Decals Ground Texture211 - Pavement Decal | 30×15 | Sheet | Paved | Random | B | ●● |  |
| RealVision Decals Ground Texture59 - Pavement Decal | 20×20 | Sheet | Paved | Random | TR | ●● |  |

#### LawnPattern (44) — lawn tile: mowing pattern / grass shape

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 UP Grass Ground G87 UP Grass Round Large Decal | 17.1×17.1 | Sheet | Grass | Random |  | ● |  |
| G87 UP Grass Ground G87 UP Grass Round Medium Decal | 8.6×8.6 | Tile | Grass | Lattice |  | ● |  |
| G87 UP Grass Ground G87 UP Grass Round Small Decal | 4.3×4.3 | Tile | Grass | Lattice |  | ● |  |
| G87 UP Grass Ground G87 UP Grass Round XSmall Decal | 2.1×2.1 | Tile | Grass | Lattice |  | ● |  |
| G87 UP Grass Ground G87 UP Grass Round XXSmall Decal | 1.1×1.1 | Tile | Grass | Lattice |  | ● |  |
| G87 UP Grass Ground G87 UP Grass Square Large Decal | 17.1×17.1 | Sheet | Grass | Random |  | ● |  |
| G87 UP Grass Ground G87 UP Grass Square Medium Decal | 8.6×8.6 | Tile | Grass | Lattice |  | ● |  |
| G87 UP Grass Ground G87 UP Grass Square Small Decal | 4.3×4.3 | Tile | Grass | Lattice |  | ● |  |
| G87 UP Grass Ground G87 UP Grass Square XSmall Decal | 2.1×2.1 | Tile | Grass | Lattice |  | ● |  |
| G87 UP Grass Ground G87 UP Grass Square XXSmall Decal | 1.1×1.1 | Tile | Grass | Lattice |  | ● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Checkered Large 12m Decal | 12×12 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Checkered Large 6m Decal | 6×6 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Checkered Small 12m Decal | 12×12 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Checkered Small 6m Decal | 6×6 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Circle Large 12m Decal | 12×12 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Circle Large 6m Decal | 6×6 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Circle Small 12m Decal | 12×12 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Circle Small 6m Decal | 6×6 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Plain 12m Decal | 12×12 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Plain 6m Decal | 6×6 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Stripes Large 12m Decal | 12×12 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Stripes Large 6m Decal | 6×6 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Stripes Small 12m Decal | 12×12 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Stripes Small 6m Decal | 6×6 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Zig Zag Large 12m Decal | 12×12 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Zig Zag Large 6m Decal | 6×6 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Zig Zag Small 12m Decal | 12×12 | Tile | Grass | Lattice | TR | ●● |  |
| Manicured Grass Texture Pack Ground Manicured Grass - Zig Zag Small 6m Decal | 6×6 | Tile | Grass | Lattice | TR | ●● |  |
| RealVision Decals Ground Texture148 - Field Terrain Grass Decal | 40×30 | Sheet | Grass | Random | RB | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Circle 16m Decal | 16.6×16.6 | Sheet | Grass | Random | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Circle 1m Decal | 1×1 | Tile | Grass | Lattice | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Circle 2m Decal | 2.1×2.1 | Tile | Grass | Lattice | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Circle 4m Decal | 4.2×4.2 | Tile | Grass | Lattice | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Circle 8m Decal | 8.3×8.3 | Tile | Grass | Lattice | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Square 16m Decal | 16×16 | Sheet | Grass | Random | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Square 1m Decal | 1×1 | Tile | Grass | Lattice | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Square 2m Decal | 2×2 | Tile | Grass | Lattice | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Square 4m Decal | 4×4 | Tile | Grass | Lattice | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Square 8m Decal | 8×8 | Tile | Grass | Lattice | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Triangle 45 16m Decal | 16.6×16.6 | Sheet | Grass | Random | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Triangle 45 1m Decal | 1×1 | Tile | Grass | Lattice | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Triangle 45 2m Decal | 2.1×2.1 | Tile | Grass | Lattice | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Triangle 45 4m Decal | 4.2×4.2 | Tile | Grass | Lattice | TR | ●● |  |
| Vanilla Terrain Texture Pack Ground Vanilla Terrain Grass - Triangle 45 8m Decal | 8.3×8.3 | Tile | Grass | Lattice | TR | ●● |  |

#### GroundSheet (95) — large ground texture sheet (aerial, terrain, road side)

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 UK Road Markings RoadMarkings G87 AR Rail Road Decal | 6.1×6.1 | Blob | Any | Random |  | ● |  |
| RealVision Decals Ground Texture01 - Aerial Decal | 20×20 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture02 - Aerial Decal | 10×10 | Blob | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture03 - small Decal | 10×10 | Blob | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture04 - Aerial Decal | 10×10 | Blob | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture05 - Aerial Decal | 10×10 | Blob | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture06 - Aerial Decal | 10×10 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture07 - Aerial Decal | 10×10 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture08 - Aerial Decal | 20×20 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture10 - Side Decal | 10×5 | Strip | Any | AlongHost | B | ●● |  |
| RealVision Decals Ground Texture101 - Garden Decal | 12×7 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture104 - Field Decal | 30×30 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture105 - Road Side Wide Decal | 30×15 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture108 - Park Decal | 30×30 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture11 - Aerial Decal | 10×10 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture110 - Park Decal | 50×30 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture112 - Road Texture Decal | 15×10 | Blob | Any | Random | TRB | ●● |  |
| RealVision Decals Ground Texture119 - Park Decal | 25×15 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture12 - Aerial Decal | 30×30 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture123 - Garden Decal | 15×15 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture124 - Park Decal | 30×30 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture13 - Road Decal | 15×10 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture136 - Park Decal | 30×30 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture14 - Aerial Decal | 10×5 | Strip | Any | AlongHost | B | ●● |  |
| RealVision Decals Ground Texture142 - Road Side Decal | 22×7 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture15 - Aerial Decal | 15×10 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture15 - small Decal | 10×10 | Blob | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture16 - Aerial Decal | 20×20 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture16 - small Decal | 10×10 | Blob | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture166 - Road Side Parking Decal | 25×10 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture17 - Aerial Decal | 20×15 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture18 - Aerial Decal | 15×15 | Blob | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture184 - Rural Road Decal | 23×8 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture186 - Aerial Decal | 40×40 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture187 - Aerial Decal | 70×40 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture188 - Aerial Decal | 70×40 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture19 - Aerial Decal | 15×15 | Blob | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture193 - Concreted Park Decal | 30×20 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture194 - Aerial Decal | 40×40 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture195 - Aerial Decal | 20×7 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture197 - Concrete Park Decal | 20×15 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture198 - Aerial Decal | 90×45 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture199 - Aerial Field Decal | 90×45 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture200 - Aerial Field Decal | 90×45 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture201 - Aerial Field Decal | 90×45 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture202 - Aerial Field Decal | 90×45 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture203 - Aerial Field Decal | 60×60 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture204 - Aerial Field Decal | 90×45 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture205 - Aerial Field Decal | 40×20 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture206 - Terrain Path Decal | 60×30 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture207 - Aerial Field Decal | 120×75 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture208 - Aerial Field Decal | 70×70 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture209 - Park Decal | 30×15 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture21 - Aerial Decal | 10×5 | Strip | Any | AlongHost | B | ●● |  |
| RealVision Decals Ground Texture212 - Aerial Decal | 30×30 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture217 - Aerial Decal | 60×60 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture22 - Aerial Decal | 15×10 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture23 Decal | 10×5 | Strip | Any | AlongHost | B | ●● |  |
| RealVision Decals Ground Texture24 Decal | 10×5 | Strip | Any | AlongHost | B | ●● |  |
| RealVision Decals Ground Texture25 Decal | 20×10 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture26 Decal | 20×20 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture27 Decal | 10×10 | Blob | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture28 Decal | 20×20 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture29 Decal | 20×20 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture30 Decal | 16×8 | Sheet | Any | Random | TRB | ●● |  |
| RealVision Decals Ground Texture31 Decal | 10×5 | Strip | Any | AlongHost | B | ●● |  |
| RealVision Decals Ground Texture32 Decal | 15×10 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture33 Decal | 20×20 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture34 - Aerial Decal | 40×40 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture34 - road Decal | 35×17.5 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture34 - road small Decal | 16×8 | Sheet | Any | Random | TRB | ●● |  |
| RealVision Decals Ground Texture35 - Aerial Decal | 40×40 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture36 - Terrain Decal | 15×10 | Blob | Any | Random | R | ●● |  |
| RealVision Decals Ground Texture41 - Road Side Decal | 25×15 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture42 - Road Side Decal | 15×10 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture48 - Road Side Decal | 25×10 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture52 - RoadSide Decal | 25×15 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture53 - RoadSide Decal | 15×10 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture58 - Park Decal | 30×30 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture60 - Terrain Decal | 60×40 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture61 - Terrain Decal | 40×40 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture62 - Crop Decal | 60×60 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture64 - Field Decal | 30×20 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture69 - SideGarden Decal | 15×10 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture70 - Garden Decal | 12×6 | Strip | Any | AlongHost | B | ●● |  |
| RealVision Decals Ground Texture71 - Road Texture Decal | 12×6 | Strip | Any | AlongHost | TR | ●● |  |
| RealVision Decals Ground Texture72 - Round Curve Decal | 30×30 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture73 - Road Texture Decal | 21×11 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture79 - Curve Decal | 40×40 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture84 - Terrain Decal | 70×40 | Sheet | Any | Random | B | ●● |  |
| RealVision Decals Ground Texture85 - Fields Patch Decal | 15×5 | Strip | Any | AlongHost | TR | ●● |  |
| RealVision Decals Ground Texture89 - Asphalt Decal | 35×20 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture94 - Road Side Decal | 15×10 | Blob | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture95 - Road Side Decal | 17×7 | Sheet | Any | Random | TR | ●● |  |
| RealVision Decals Ground Texture99 - Road Texture Decal | 20×10 | Sheet | Any | Random | TR | ●● |  |

#### Marking (214) — road / text / arrow / icon marking

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| AlphabetDecalA01 *(vanilla)* | 0.6×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalB01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalC01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalD01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalE01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalF01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalG01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalH01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalI01 *(vanilla)* | 0.1×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalJ01 *(vanilla)* | 0.4×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalK01 *(vanilla)* | 0.6×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalL01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalM01 *(vanilla)* | 0.6×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalN01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalO01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalP01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalQ01 *(vanilla)* | 0.6×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalR01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalS01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalT01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalU01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalV01 *(vanilla)* | 0.6×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalW01 *(vanilla)* | 0.8×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalX01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalY01 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| AlphabetDecalZ01 *(vanilla)* | 0.4×0.7 | Tiny | Paved | Fixed |  | ● |  |
| ContainerGridDecal01 *(vanilla)* | 15.9×7 | Strip | Paved | Fixed |  | ● |  |
| ContainerGridDecal02 *(vanilla)* | 15.9×13.9 | Blob | Paved | Fixed |  | ● |  |
| EU LaneMarkings Bus Taxi *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU LaneMarkings Speed100 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU LaneMarkings Speed120 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU LaneMarkings Speed30 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU LaneMarkings Speed40 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU LaneMarkings Speed50 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU LaneMarkings Speed60 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU LaneMarkings Speed80 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Forward *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Forward Left90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Forward Left90 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Forward Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Left180 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Left45 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Left45 Right45 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Left45 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Left90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Left90 Right45 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Left90 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow LeftMerge *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Right180 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Right45 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow RightMerge *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2L Forward *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2L Forward Left90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2L Forward Left90 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2L Forward Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2L Left90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2L Left90 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2L Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2R Forward *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2R Forward Left90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2R Forward Left90 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2R Forward Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2R Left90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2R Left90 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU RoadArrow2R Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| EU_BicycleBanIconDecal01 *(vanilla)* | 1.6×1.6 | Blob | Paved | Fixed |  | ● |  |
| EU_BicycleIconDecal01 *(vanilla)* | 1.4×0.9 | Blob | Paved | Fixed |  | ● |  |
| EU_LaneMarkingsBicycle01 *(vanilla)* | 1×0.6 | Tiny | Paved | Fixed |  | ● |  |
| EU_LaneMarkingsBicycleBan01 *(vanilla)* | 1.5×1.5 | Blob | Paved | Fixed |  | ● |  |
| EU_LaneMarkingsPedestrian01 *(vanilla)* | 0.7×0.9 | Tiny | Paved | Fixed |  | ● |  |
| EU_LaneMarkingsPedestrianBan01 *(vanilla)* | 1.5×1.5 | Blob | Paved | Fixed |  | ● |  |
| EU_PedestrianBanIconDecal01 *(vanilla)* | 1.6×1.6 | Blob | Paved | Fixed |  | ● |  |
| EU_PedestrianIconDecal01 *(vanilla)* | 0.8×1.1 | Tiny | Paved | Fixed |  | ● |  |
| EU_RoadArrowForwardSmall *(vanilla)* | 0.3×1.2 | Tiny | Paved | Fixed |  | ● |  |
| G87 Road Markings SC RoadMarkings G87 Chevrons 2to1 30cm White Decal | 5.1×5.1 | Blob | Paved | Fixed | R | ●● |  |
| G87 Road Markings SC RoadMarkings G87 Chevrons 2to1 60cm White Decal | 5.1×5.1 | Blob | Paved | Fixed | R | ●● |  |
| G87 Road Markings SC RoadMarkings G87 Chevrons 2to1 60cm White Wide Decal | 10.2×10.2 | Blob | Paved | Fixed | R | ●● |  |
| G87 UK Road Markings RoadMarkings G87 AR Stop Decal | 2.9×2.9 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 FR Livraison Decal | 1.2×4.9 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 FR Livraison Negative Decal | 1.2×4.9 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 FR Payant Decal | 1×2 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 FR Payant Negative Decal | 1×2 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 NA Only 3f Decal | 2.4×2.4 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 NA Only 6f Decal | 4.9×4.9 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 NA Only 9f Decal | 4.9×7.3 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 NA Slow 3f Decal | 2.4×2.4 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 NA Slow 6f Decal | 4.9×4.9 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 NA Stop 3f Decal | 2.4×2.4 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 NA Stop 6f Decal | 4.9×4.9 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 NA Wrong Way 3f Decal | 2.4×2.4 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 NA Wrong Way 6f Decal | 4.9×4.9 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Bike Lane Arrow Decal | 2×2 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Bike Lane Dots Decal | 2×1 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Bike Lane Sign Decal | 1.3×1.3 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Circle Big Decal | 3.4×3.4 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Circle Small Decal | 3.1×3.1 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK Bus Stop Marking Decal | 4.3×4.3 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK Give Way Marking Decal | 2×4 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK KC Children Marking Decal | 10.2×1.3 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK KC Left Segment Decal | 5.1×2.6 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK KC Left Side Decal | 5.1×2.6 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK KC Left Terminal Decal | 5.1×2.6 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK KC Marking Decal | 10.2×1.3 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK KC Right Segment Decal | 5.1×2.6 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK KC Right Side Decal | 5.1×2.6 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK KC Right Terminal Decal | 5.1×2.6 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK KC School Marking Decal | 10.2×1.3 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK Keep Clear Decal | 4×4 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK Roundabout Big Decal | 7.3×7.3 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK Roundabout Small Decal | 5×5 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK Stop Marking Decal | 3×3 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK Taxis Bay Marking Decal | 2.7×1.4 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK Terminal Line Decal | 1×1 | Tiny | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK Terminal Line Red Decal | 1×1 | Tiny | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK Terminal Line White Decal | 1×1 | Tiny | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK Terminal Line Yellow Decal | 1×1 | Tiny | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK ZigZag Line Full Decal | 1×4 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 UK ZigZag Line Segment Decal | 1×2 | Strip | Paved | Fixed |  | ● |  |
| NA LaneMarkings Bus Taxi *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA LaneMarkings Speed25 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA LaneMarkings Speed30 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA LaneMarkings Speed35 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA LaneMarkings Speed45 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA LaneMarkings Speed55 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA LaneMarkings Speed65 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA LaneMarkings Speed70 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Forward *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Forward Left90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Forward Left90 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Forward Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Left180 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Left45 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Left45 Right45 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Left45 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Left90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Left90 Right45 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Left90 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow LeftMerge *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Right180 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Right45 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow RightMerge *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2L Forward *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2L Forward Left90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2L Forward Left90 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2L Forward Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2L Left90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2L Left90 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2L Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2R Forward *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2R Forward Left90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2R Forward Left90 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2R Forward Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2R Left90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2R Left90 Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA RoadArrow2R Right90 *(vanilla)* | 3×6 | Strip | Paved | Fixed |  | ● |  |
| NA_BicycleBanIconDecal01 *(vanilla)* | 1.6×1.6 | Blob | Paved | Fixed |  | ● |  |
| NA_BicycleIconDecal01 *(vanilla)* | 1.4×1.2 | Blob | Paved | Fixed |  | ● |  |
| NA_LaneMarkingsBicycle01 *(vanilla)* | 1×0.9 | Tiny | Paved | Fixed |  | ● |  |
| NA_LaneMarkingsBicycleBan01 *(vanilla)* | 1.5×1.5 | Blob | Paved | Fixed |  | ● |  |
| NA_LaneMarkingsPedestrian01 *(vanilla)* | 0.7×0.9 | Tiny | Paved | Fixed |  | ● |  |
| NA_LaneMarkingsPedestrianBan01 *(vanilla)* | 1.5×1.5 | Blob | Paved | Fixed |  | ● |  |
| NA_PedestrianBanIconDecal01 *(vanilla)* | 1.6×1.6 | Blob | Paved | Fixed |  | ● |  |
| NA_PedestrianIconDecal01 *(vanilla)* | 0.8×1.1 | Tiny | Paved | Fixed |  | ● |  |
| NA_RoadArrowForwardSmall *(vanilla)* | 0.3×1.2 | Tiny | Paved | Fixed |  | ● |  |
| NoParkingStripingDecal01 *(vanilla)* | 3.7×6.5 | Blob | Paved | Fixed |  | ● |  |
| NoParkingStripingDecal02 *(vanilla)* | 6.6×1.2 | Strip | Paved | Fixed |  | ● |  |
| NoParkingStripingDecal03 *(vanilla)* | 12.2×1.2 | Strip | Paved | Fixed |  | ● |  |
| NoParkingStripingDecal04 *(vanilla)* | 21.9×1.2 | Sheet | Paved | Fixed |  | ● |  |
| NumberDecal001 *(vanilla)* | 0.4×0.7 | Tiny | Paved | Fixed |  | ● |  |
| NumberDecal101 *(vanilla)* | 0.2×0.7 | Tiny | Paved | Fixed |  | ● |  |
| NumberDecal201 *(vanilla)* | 0.4×0.7 | Tiny | Paved | Fixed |  | ● |  |
| NumberDecal301 *(vanilla)* | 0.4×0.7 | Tiny | Paved | Fixed |  | ● |  |
| NumberDecal401 *(vanilla)* | 0.5×0.7 | Tiny | Paved | Fixed |  | ● |  |
| NumberDecal501 *(vanilla)* | 0.4×0.7 | Tiny | Paved | Fixed |  | ● |  |
| NumberDecal601 *(vanilla)* | 0.4×0.7 | Tiny | Paved | Fixed |  | ● |  |
| NumberDecal701 *(vanilla)* | 0.4×0.7 | Tiny | Paved | Fixed |  | ● |  |
| NumberDecal801 *(vanilla)* | 0.4×0.7 | Tiny | Paved | Fixed |  | ● |  |
| NumberDecal901 *(vanilla)* | 0.4×0.7 | Tiny | Paved | Fixed |  | ● |  |
| RealVision Decals Ground Texture72 - roundabout Decal | 25×25 | Sheet | Paved | Fixed | TR | ●● |  |
| RealVision Decals Ground Texture72 - roundabout xl Decal | 50×50 | Sheet | Paved | Fixed | TR | ●● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 1 L Decal | 18.5×18.5 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 1 M Decal | 11×11 | Blob | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 1 XL Decal | 27×27 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 10 L Decal | 18.5×18.5 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 10 M Decal | 11×11 | Blob | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 10 XL Decal | 27×27 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 2 L Decal | 18.5×18.5 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 2 M Decal | 11×11 | Blob | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 2 XL Decal | 27×27 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 3 L Decal | 18.5×18.5 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 3 M Decal | 11×11 | Blob | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 3 XL Decal | 27×27 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 4 L Decal | 18.5×18.5 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 4 M Decal | 11×11 | Blob | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 4 XL Decal | 27×27 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 5 L Decal | 18.5×18.5 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 5 M Decal | 11×11 | Blob | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 5 XL Decal | 27×27 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 6 L Decal | 18.5×18.5 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 6 M Decal | 11×11 | Blob | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 6 XL Decal | 27×27 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 7 L Decal | 18.5×18.5 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 7 M Decal | 11×11 | Blob | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 7 XL Decal | 27×27 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 8 L Decal | 18.5×18.5 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 8 M Decal | 11×11 | Blob | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 8 XL Decal | 27×27 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 9 L Decal | 18.5×18.5 | Sheet | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 9 M Decal | 11×11 | Blob | Paved | Fixed |  | ● |  |
| Roundabout Pack 1 Ground RZZirrah Roundabout 9 XL Decal | 27×27 | Sheet | Paved | Fixed |  | ● |  |
| ServiceTextDecal01 *(vanilla)* | 2.1×0.5 | Strip | Paved | Fixed |  | ● |  |
| SlowDownTextDecal01 *(vanilla)* | 3.2×0.5 | Strip | Paved | Fixed |  | ● |  |

#### SportsMarking (11) — court, pitch or playground pad

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| AmericanFootballFieldDecal01 *(vanilla)* | 65×65 | Sheet | Paved | Fixed |  | ● |  |
| BasketballCourtDecal01 *(vanilla)* | 28×15 | Sheet | Paved | Fixed |  | ● |  |
| BasketballCourtDecal03 *(vanilla)* | 28×15 | Sheet | Paved | Fixed |  | ● |  |
| FootballFieldDecal01 *(vanilla)* | 68×52.5 | Sheet | Paved | Fixed |  | ● |  |
| MultipurposeSportFieldDecal01 *(vanilla)* | 87.3×89.5 | Sheet | Paved | Fixed |  | ● |  |
| MultipurposeSportFieldDecal02 *(vanilla)* | 50.4×54.5 | Sheet | Paved | Fixed |  | ● |  |
| PlaygroundDecal01 *(vanilla)* | 24×24 | Sheet | Paved | Fixed |  | ● |  |
| PlaygroundDecal02 *(vanilla)* | 8×8 | Blob | Paved | Fixed |  | ● |  |
| PlaygroundDecal03 *(vanilla)* | 24×24 | Sheet | Paved | Fixed |  | ● |  |
| TennisCourtDecal01 *(vanilla)* | 32×21 | Sheet | Paved | Fixed |  | ● |  |
| TennisCourtDecal02 *(vanilla)* | 27×12.5 | Sheet | Paved | Fixed |  | ● |  |

#### ParkingStall (61) — parking stall / parking icon

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| BicycleParkingLotDecal01 *(vanilla)* | 4×2 | Strip | Paved | Fixed |  | ● |  |
| BicycleParkingLotDecal02 *(vanilla)* | 4×4 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Blue 60 Degree Decal | 4.5×9 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Blue 90 Degree Decal | 1.1×9 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Blue Disabled x1 Decal | 7×7 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Blue Slot Decal | 7.4×7.4 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Blue x1 Decal | 4.5×9 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Blue x1 Diag Decal | 6×12 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Blue x3 Decal | 9×9 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Blue x3 Diag Decal | 12×12 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Yellow 60 Degree Decal | 4.5×9 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Yellow 90 Degree Decal | 1.1×9 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Yellow Disabled x1 Decal | 7×7 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Yellow Slot Decal | 7.4×7.4 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Yellow x1 Decal | 4.5×9 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Yellow x1 Diag Decal | 6×12 | Strip | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Yellow x3 Decal | 9×9 | Blob | Paved | Fixed |  | ● |  |
| G87 UK Road Markings RoadMarkings G87 RM Parking Yellow x3 Diag Decal | 12×12 | Blob | Paved | Fixed |  | ● |  |
| NoParkingTextDecal01 *(vanilla)* | 3.3×0.5 | Strip | Paved | Fixed |  | ● |  |
| ParkingIconDecal01 *(vanilla)* | 0.5×0.8 | Tiny | Paved | Fixed |  | ● |  |
| ParkingIconDisabledDecal01 *(vanilla)* | 1.4×1.6 | Blob | Paved | Fixed |  | ● |  |
| ParkingIconElectricDecal01 *(vanilla)* | 1.1×1.6 | Blob | Paved | Fixed |  | ● |  |
| ParkingIconMotorbikeDecal01 *(vanilla)* | 1.1×1.4 | Blob | Paved | Fixed |  | ● |  |
| ParkingIconTruckDecal01 *(vanilla)* | 1.2×1.4 | Blob | Paved | Fixed |  | ● |  |
| ParkingLotDecal01 *(vanilla)* | 3.2×6.2 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotDecal02 *(vanilla)* | 9.1×6.2 | Blob | Paved | Fixed |  | ● |  |
| ParkingLotDecal03 *(vanilla)* | 14.9×6.2 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotDecal04 *(vanilla)* | 29.6×6.2 | Sheet | Paved | Fixed |  | ● |  |
| ParkingLotDecal05 *(vanilla)* | 3.2×8.1 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotDiagonalDecal01 *(vanilla)* | 5.6×6.3 | Blob | Paved | Fixed |  | ● |  |
| ParkingLotDiagonalDecal02 *(vanilla)* | 12×6.3 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotDiagonalDecal03 *(vanilla)* | 18.4×6.3 | Sheet | Paved | Fixed |  | ● |  |
| ParkingLotDiagonalServiceDecal01 *(vanilla)* | 5.6×6.3 | Blob | Paved | Fixed |  | ● |  |
| ParkingLotDiagonalServiceDecal02 *(vanilla)* | 12×6.3 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotDiagonalServiceDecal03 *(vanilla)* | 18.4×6.3 | Sheet | Paved | Fixed |  | ● |  |
| ParkingLotDisabledDecal01 *(vanilla)* | 5×6.2 | Blob | Paved | Fixed |  | ● |  |
| ParkingLotDisabledDecal02 *(vanilla)* | 14.6×6.2 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotDoubleDecal01 *(vanilla)* | 3×12 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotDoubleDecal02 *(vanilla)* | 3×15.6 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotDoubleServiceDecal01 *(vanilla)* | 3×12 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotDoubleServiceDecal02 *(vanilla)* | 3×15.6 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotElectricDecal01 *(vanilla)* | 3.2×6.2 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotElectricDecal02 *(vanilla)* | 9.1×6.2 | Blob | Paved | Fixed |  | ● |  |
| ParkingLotLongDecal01 *(vanilla)* | 4.4×14 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotLongDecal02 *(vanilla)* | 4.4×10.6 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotLongServiceDecal01 *(vanilla)* | 4.4×14 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotLongServiceDecal02 *(vanilla)* | 4.4×10.6 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotMotorbikeDecal01 *(vanilla)* | 1.4×3.8 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotServiceDecal01 *(vanilla)* | 3.2×6.2 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotServiceDecal02 *(vanilla)* | 9.1×6.2 | Blob | Paved | Fixed |  | ● |  |
| ParkingLotServiceDecal03 *(vanilla)* | 14.9×6.2 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotServiceDecal04 *(vanilla)* | 29.6×6.2 | Sheet | Paved | Fixed |  | ● |  |
| ParkingLotServiceDecal05 *(vanilla)* | 3.2×8.1 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotSidewaysDecal01 *(vanilla)* | 3.6×6.4 | Blob | Paved | Fixed |  | ● |  |
| ParkingLotSidewaysDecal02 *(vanilla)* | 3.6×8.3 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotSidewaysDecal03 *(vanilla)* | 4.4×10.7 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotSidewaysServiceDecal01 *(vanilla)* | 3.6×6.4 | Blob | Paved | Fixed |  | ● |  |
| ParkingLotSidewaysServiceDecal02 *(vanilla)* | 3.6×8.3 | Strip | Paved | Fixed |  | ● |  |
| ParkingLotSidewaysServiceDecal03 *(vanilla)* | 4.4×10.7 | Strip | Paved | Fixed |  | ● |  |
| ParkingTextDecal01 *(vanilla)* | 2.4×0.5 | Strip | Paved | Fixed |  | ● |  |
| RealVision Decals Ground Texture150 - Parking Spot Decal | 5×5 | Blob | Paved | Fixed | TR | ●● |  |

#### FarmTrack (62) — farm wheel ruts (laid along rows)

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| Farming Surfaces & Decals RoadMarkings Haristess - 2-1 - TractorTracks Dkr Straight1 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-1 - TractorTracks Dkr Straight2 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-2 - TractorTracks Dkr Straight1 Half Decal | 6×6 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-2 - TractorTracks Dkr Straight2 Half Decal | 6×6 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-3 - TractorTracks Dkr End Open Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-3 - TractorTracks Dkr End Straight Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr LCorner 11-25 Long Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr LCorner 11-25 Short Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr LCorner 22-5 Long Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr LCorner 22-5 Short Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr LCorner 45 Short Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr LCorner 45 Wide Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr RCorner 11-25 Long Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr RCorner 11-25 Short Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr RCorner 22-5 Long Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr RCorner 22-5 Short Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr RCorner 45 Short Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr RCorner 45 Wide Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr SBend Shallow L Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr SBend Shallow R Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 3-1 - GrassTracks Straight1 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 3-1 - GrassTracks Straight2 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 3-2 - GrassTracks2 Straight1 Half Decal | 6×6 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 3-3 - GrassTracks Straight End Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 3-4 - GrassTracks Corner 11-25 Long Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 3-4 - GrassTracks Corner 11-25 Short Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 3-4 - GrassTracks Corner 22-5 Long Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 3-4 - GrassTracks Corner 22-5 Short Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 3-4 - GrassTracks Corner 45 Long Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 3-4 - GrassTracks Corner 45 Short Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-1 - FieldTracks Straight1 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-1 - FieldTracks Straight2 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-1 - FieldTracks Straight3 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-1 - FieldTracks Straight4 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-1 - FieldTracks Straight5 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-2 - FieldTracks Straight Half Decal | 6×6 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-2 - FieldTracks Straight3 Half Decal | 6×6 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-2 - FieldTracks Straight4 Half Decal | 6×6 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-3 - FieldTracks End Open1 Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-4 - FieldTracks Corner 11-25 Long Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-4 - FieldTracks Corner 11-25 Short Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-4 - FieldTracks Corner 22-5 Long Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-4 - FieldTracks Corner 22-5 Short Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-4 - FieldTracks Corner 45 Long Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-4 - FieldTracks Corner 45 Short Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 4-4 - FieldTracks Corner TBend Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-1 - FieldTracks Darker Straight1 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-1 - FieldTracks Drkr Straight2 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-1 - FieldTracks Drkr Straight3 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-1 - FieldTracks Drkr Straight4 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-1 - FieldTracks Drkr Straight5 Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-2 - FieldTracks Drkr Straight Half Decal | 6×6 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-2 - FieldTracks Drkr Straight3 Half Decal | 6×6 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-2 - FieldTracks Drkr Straight4 Half Decal | 6×6 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-3 - FieldTracks Drkr End Open1 Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-4 - FieldTracks Drkr Corner 11-25 Long Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-4 - FieldTracks Drkr Corner 11-25 Short Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-4 - FieldTracks Drkr Corner 22-5 Long Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-4 - FieldTracks Drkr Corner 22-5 Short Decal | 6×12 | Strip | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-4 - FieldTracks Drkr Corner 45 Long Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-4 - FieldTracks Drkr Corner 45 Short Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |
| Farming Surfaces & Decals RoadMarkings Haristess - 5-4 - FieldTracks Drkr Corner TBend Decal | 12×12 | Blob | Paved | Fixed | TR | ● |  |

#### Logo (6) — Chirper logo

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| ChirperDecalLarge01 *(vanilla)* | 24×24 | Sheet | Paved | Fixed |  | ● |  |
| ChirperDecalLarge02 *(vanilla)* | 23.7×22.4 | Sheet | Paved | Fixed |  | ● |  |
| ChirperDecalMedium01 *(vanilla)* | 16×16 | Sheet | Paved | Fixed |  | ● |  |
| ChirperDecalMedium02 *(vanilla)* | 15.9×15 | Blob | Paved | Fixed |  | ● |  |
| ChirperDecalSmall01 *(vanilla)* | 8×8 | Blob | Paved | Fixed |  | ● |  |
| ChirperDecalSmall02 *(vanilla)* | 7.9×7.4 | Blob | Paved | Fixed |  | ● |  |

#### BuildingOnly (40) — wall / roof / curb / window decal (not ground)

| decal | size m | shape | ground | orientation | mask | conf | pools |
|---|---|---|---|---|---|---|---|
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 01 Curb Decal | 2×0.5 | Strip | None | Fixed | TRB | ●● |  |
| G87 Stains and Puddles Decals Wet Pack Puddles G87 Puddle Mirror 02 Curb Decal | 4×2 | Strip | None | Fixed | TRB | ●● |  |
| G87 Vanilla Curb Misc G87 Vanilla Curb Cornerstone Round Decal | 0.6×0.6 | Tiny | None | Fixed | TRB | ●● |  |
| G87 Vanilla Curb Misc G87 Vanilla Curb Cornerstone Square Decal | 0.6×0.6 | Tiny | None | Fixed | TRB | ●● |  |
| G87 Vanilla Curb Misc G87 Vanilla Curb Decal | 0.6×3.2 | Strip | None | Fixed | TRB | ●● |  |
| G87 Vanilla Curb Misc G87 Vanilla Curb Short Decal | 0.6×1.6 | Strip | None | Fixed | TRB | ●● |  |
| RealVision Decals Ground Texture100 - Road Side Separator Decal | 25×15 | Sheet | None | Fixed | B | ●● |  |
| RealVision Decals Ground Texture103 - Broken Curb Decal | 15×10 | Blob | None | Fixed | TRB | ●● |  |
| RealVision Decals Ground Texture106 - Side Building Decal | 15×7 | Strip | None | Fixed | TRB | ●● |  |
| RealVision Decals Ground Texture107 - Side Building Decal | 15×7 | Strip | None | Fixed | TRB | ●● |  |
| RealVision Decals Ground Texture114 - Roof Rust Decal | 15×15 | Blob | None | Fixed | TRB | ●● |  |
| RealVision Decals Ground Texture120 - Red Brick Roof Decal | 15×10 | Blob | None | Fixed | TRB | ●● |  |
| RealVision Decals Ground Texture121 - Red Brick Roof Decal | 20×10 | Sheet | None | Fixed | TRB | ●● |  |
| RealVision Decals Ground Texture122 - Red Brick Roof Decal | 20×10 | Sheet | None | Fixed | TRB | ●● |  |
| RealVision Decals Ground Texture131 - Road Side Curb Decal | 15×3 | Strip | None | Fixed | TR | ●● |  |
| RealVision Decals Ground Texture153 - Roof Dirt Decal | 13×13 | Blob | None | Fixed | RB | ●● |  |
| RealVision Decals Ground Texture154 - Castle Wall Decal | 15×3 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture155 - Castle Wall Decal | 10×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture156 - Castle Side Floor Decal | 10×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture157 - Castle Wall Decal | 7×7 | Blob | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture158 - Castle Wall Decal | 15×5 | Strip | None | Fixed | B | ●● |  |
| RealVision Decals Ground Texture159 - Castle Wall Decal | 15×10 | Blob | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture160 - Castle Wall Decal | 10×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture161 - Castle Window Decal | 3×3 | Blob | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture162 - Castle Wall Decal | 10×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture163 - Castle walkPath Decal | 15×5 | Strip | None | Fixed | B | ●● |  |
| RealVision Decals Ground Texture164 - Castle Wall Decal | 15×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture165 - Castle Inside Rumble Decal | 20×20 | Sheet | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture167 - Wall Vegetation Decal | 10×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture168 - Castle Wall Decal | 10×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture169 - Wall Plant Decal | 2×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture170 - Wall Plant Decal | 2×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture171 - Wall Plant Decal | 2×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture172 - Wall Plant Decal | 2×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture173 - Wall Plant Decal | 1.5×3 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture174 - Wall Plant Decal | 2×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture175 - Wall Plant Holder Decal | 2×7 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture183 - Damaged Brick Wall Decal | 15×5 | Strip | None | Fixed | BO | ●● |  |
| RealVision Decals Ground Texture68 - Curb and Pavement Decal | 10×5 | Strip | None | Fixed | TR | ●● |  |
| RealVision Decals Ground Texture98 - Road Water Drain Decal | 7×1.8 | Strip | None | Fixed | TR | ●● |  |
