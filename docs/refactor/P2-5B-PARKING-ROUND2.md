# Parking round 2: three agents (the user's test of P2-5, 2026-10-31)

The user tested the P2-5 car parks in their own game (master `fa76da93`, installed 14:17). Overall grade **B**, with some
styles near A+. This file is the brief for three new agents, each starting fresh from master. Read
`docs/refactor/PHASE2.md` first: its **shared block** (reading list, what Phase 1 gave you, stay in your lane, push to
the max, prove in the game, report format, rules) is part of this brief. Then read `docs/refactor/P2-5-PARKING-LOOKS.md`
(the first brief), `docs/runbooks/parking-looks.md` (what P2-5 built and learned) and `docs/runbooks/parking-tools.md`.

The user's screenshots (local, git-ignored): `C:\Users\jack\cs2-area-tool\.forensics\user-shots\parking-round2` (1 round lot entrances, 2 Japanese on a hexagon, 3 green canopy circles).

## The user's grades and notes, per style

| Style | Grade | The user's notes |
|---|---|---|
| Green with canopies | **~A+** | "I'm flabbergasted how good this is." The green circle decal in each stall looks stretched into ovals: make the circles round, bigger, and grassier, so they read as grass pavers. Check that the canopies line up with parked cars. Add a setting for how much of the lot is under canopies. |
| Upscale | **A−** | "Nearly perfect." The skid marks follow a visible pattern: make them random in placement (not in direction). |
| Big-box store | **near A+** | "One of my favourites." Lines sometimes don't line up, and the medians are a little odd. Try more irregular shapes (parallelograms). Let the player change the line colour (presets of decals are fine). |
| Terraced | look **loved**, on a hill **C** | "I really, really do love the look." It should become the main look: the terraced look first in the list (or what Classic looks like), and today's Classic kept as its own style further down. On a hill it gets a C. Surface mismatches show grass where stalls should be, fences float or stagger, and the ground is still bumpy. "We need to be the dominant force in the terrain." |
| Gravel | **good** | "I like it." Gravel lots have no painted stalls, so the per-stall dirt patches are too regular. Use speckled, irregular wear instead of consistent splotches: less robotic. |
| Rundown | **good** | Most abandoned cars sit on the medians: only about 1 in 5 should; most go in stalls. It could be more rundown, with more homeless camps (as settings). |
| Under construction | **B−** | Some yellow barriers are turned 90° the wrong way. The site should look better and more like a real construction site. Stretched textures turn green. |
| Japanese coin park | **dislikes** | "I don't even understand what this is." The entrance sign and a coin plate on every stall try to do too much. On irregular shapes (a hexagon) it looks very weird (screenshot 2: a dense grid of overlapping white and yellow boxes). The yellow lines are nice on their own. Hide it (shelf), or rework it into something simple that reads. |
| Round | **broken** | See below. |

## Problems across all styles

1. **Round lot** (screenshot 1):
   - Pedestrian entrances (zebra crossing, sign, kerb drop) appear out on the grass beside the city road, away from the lot. Their connectors are invisible and cut into the real road.
   - When the selection is bigger than the circle, the leftover land is empty grass inside a fence around the whole selection. The leftover should become parking rows, a plaza or planting around the round part: a designed site, not empty land.
2. **Hills:** every style must handle any hill, not just Terraced. The lot splits itself into flat sections, levels each one firmly (terraform is the tool, not decoration), and uses the slopes between as driveways and ramps. That leaves no bumpy ground, no grass showing through stalls, and no floating or stepped fences.
3. **Robotic decals:** drain, crack, drain, crack, perfectly spaced; dirt or concrete as obvious square decals; skid marks in a pattern. Make placement irregular and natural: clustered, varied, sometimes absent, with edges that blend. Tone the amount down a little, and add more leaves.
4. **Decals turn green or purple** when they are stretched or seen at lower resolution (mipmaps). Find the cause in the shared decal pipeline (atlas bleeding, mip generation, UV stretch, the colour of transparent texels), fix it for every decal, and prove it close up and far away.
5. **Ground colour from terrain paint first.** Never build a ground colour out of many decals. Terrain paint sets the base, and decals add detail on top (the upscale lot already does this well).
6. **The planning preview** (the top-view site plan shown before building) looks nothing like what gets built. It must show essentially what will be built, in a simplified, clean drawn style. This holds for every type, not just parking.
7. **More styles:** two more dirt lots (a plain gravelly one, and a wooded mulchy one in the trees with big trees), and an even more abandoned one (mostly a homeless camp with a little parking).
8. "Lean on the ones I like and bring those concepts into the ones I don't like."

---

## Agent A: parking terrain and shapes ("any style on any hill")

**Owns:** lot grading and terraces (`Core/Layers/Parking/LotGrading.cs` and any new grading files), the lot's shape and
layout (round, rows, irregular outlines), entrances and their connectors, fences and kerbs where they meet the ground,
and filling leftover land.

**Build:**
1. **Any style on any hill.** Split the lot into level sections along the contours. Grade each one firmly with terraform, and link them with driveways or ramps at a drivable grade, using retaining walls or planted banks (the terraced look). The rules:
   - no bumps;
   - no grass showing through stalls (surface and terrain must agree; check the ground under every stall);
   - fences and kerbs follow the finished ground (no floating, no stair-steps);
   - banks never over 50 %.

   Test on 5 %, 10 %, 15 % and 20 % slopes and on hills, for at least Classic, terraced, big-box, green and upscale.
2. **Round lot:**
   - Entrances sit on the lot's edge facing the street, joined properly to the street's lanes and sidewalk, with nothing out on the grass and nothing invisible cutting into the road.
   - The leftover land around the circle in a bigger selection becomes parking rows, a plaza or planting.
   - The fence follows what's built, not the raw selection.
3. **Irregular outlines** (L, triangle, hexagon): rows fill them cleanly and stalls never overlap (the Japanese hexagon screenshot shows overlapping stall grids). Fix this for every style.
4. **Big-box medians and lines:** make the lines meet exactly. Offer irregular median shapes (parallelograms, angled ends).

**Done:** every style is A on every slope and hill; the round lot is A; irregular outlines are clean. Pictures: 45°, low and close on each slope, plus the terrain map. Check the pit-check and net-sag logs.

---

## Agent B: parking looks and decals round 2

**Owns:** the parking styles, presets and look settings (`Core/Layers/Parking/ParkingLook.cs`, the presets in
`ParkingLayer.cs`, the parking decal kinds and brushes, the lang files, `Recipes/shelf/parking.json`), plus the **shared
decal fix** for the green and purple tint (problem 4: `Core/Decals`, `Game/Decals`, the decal cache).

**Build:**
1. **Natural, not robotic, decals:** irregular spacing, clusters and gaps, varied size and opacity, soft edges, no visible squares. This applies to cracks, drains, oil, skid marks, dirt and patches. Slightly less of everything, and more leaves. Gravel gets speckled wear, not per-stall splotches.
2. **The green and purple tint:** find the cause and fix it in the shared decal pipeline for every decal. Prove it in close and far pictures.
3. **Terrain paint first:** audit every style. Base colours come from terrain paint or surfaces; decals add detail only.
4. **Per style** (from the table above):
   - **Green:** round, bigger, grassier circles; a canopy-coverage setting; canopies lined up with parked cars.
   - **Upscale:** random skid marks.
   - **Big-box:** a line-colour preset setting (white, yellow, blue and so on); share it with other styles where it fits.
   - **Rundown:** abandoned cars mostly in stalls (about 1 in 5 on medians); more rundown; homeless camps more common, via settings.
   - **Under construction:** barriers turned the right way; a real site look (fenced compound, earthworks, machines, stacked materials, a site office, mud tracks).
   - **Japanese:** hide it on the shelf, or rework it into a simple style that reads (yellow lines, small lot, pay machine, no coin on every stall). Decide, and say why.
5. **Make the terraced look the main look:** reorder so it comes first (or becomes the new Classic), and keep today's Classic as its own style. Agent A handles how terraced behaves on hills; you handle its look and its place in the list.
6. **New styles:** plain gravel, wooded or mulchy (in the trees, big trees, mulch and roots), and "abandoned": mostly a homeless camp with a little parking.
7. Bring what works in green, upscale, big-box and terraced into the weaker styles.

**Done:** every style is A+ in close, 45° and far pictures; no tint anywhere; new styles A+; every setting does something; 12 languages and the panel fit test.

---

## Agent C: the planning preview shows what will be built (every type)

**Owns:** the planning preview, the top-view site plan drawn before building (`Core/Build/SitePlan.cs`,
`Core/Build/PreviewLegend.cs`, the site-plan drawing in `Game/` and `UI/src`), shared across every type.

**Build:**
1. Audit what the preview draws today against what gets built, for parking (every style), parks, plazas, shops, farms and beaches. Make a table of what's missing, wrong or misplaced.
2. Make the preview match the build in a clean, simplified drawn style:
   - the real stall layout and markings;
   - kerbs and islands, surfaces and their colours;
   - trees and planting at their real places and sizes, equipment, entrances;
   - terraces and ramps, water, paths, the biome areas.
3. It must stay fast: no new frame drops while dragging or editing. Measure it.
4. Show before-and-after side by side (preview picture next to the built picture, the same view) for every type in the audit.

**Done:** for every audited type, the preview reads as the same place as the build. The user's words: "it needs to look basically exactly like what's going to happen, but in a more baby-looking style or a dumbed view." The bar is that the previews are "much more exquisite".

---

## Working together

- A and B both work in `Core/Layers/Parking/`.
  - **A** owns grading, shape, layout and entrance files.
  - **B** owns looks, presets, decals and the shelf.

  If you need a change in the other's file, keep it small and additive and say so in your commit message, or ask the
  lead. Commit small and often; the lead merges both into master between rounds.
- C touches the preview only; it reads what A and B produce, so keep it data-driven (draw from the plan, never from a
  per-style hand-drawn copy).
- Every agent:
  - Proves everything in the game runner through the queue (background, `-Who <name>`, never `-AllMods` or `-Direct`) and quits the queue's game when done.
  - Puts every text into 12 languages, and hides rather than deletes.
  - Commits with no attribution lines, and doesn't push or install.
  - Reports with picture paths, a self-grade and the gap to A+, and keeps going until A+.
