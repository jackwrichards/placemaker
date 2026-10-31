# P2-8 Parks and biomes: every kind of city park, and a landscape panel that feels great

Phase 2 wave B. Read `docs/refactor/PHASE2.md` first: its **shared block** (reading list, what Phase 1 gave you, stay in
your lane, push to the max, prove in the game, report format, rules) is part of this brief, word for word.

## The user's words (2026-10-31)

> "I think parks are amazing. They get nearly an A+. But not all city parks are a big forest with mulch and whatnot. And
> **even when I turn it to full meadow, I still get some trees.** I want even more customization in biomes. **Sometimes I
> just want a park to be a field**, you know? I don't want all that fancy stuff. But **even a field should have good
> looking decals.** Maybe that's just a new biome we need to add."

> "That slider where you can set the ratio of the different things **doesn't behave exactly like I would expect it to.
> The zone tool is a much better one**: it sticks out, but it doesn't fit, and it's more easily draggable. I like that a
> lot better. And maybe **each biome can be customized itself.** But we also have other parameters below. Is that where
> those are global and then the biomes are biome-specific? **It needs to be really good UX.**"

## Where things stand (start here)

- **The biome mix** (`docs/runbooks/biomes.md`, read all of it): `Core/Layers/Common/Biomes/` (`Biome.cs` the library,
  `BiomeField.cs` the split, `BiomeBuilder.cs` the build), the panel `UI/src/controls/BiomeMix.tsx` (the Landscape card:
  a stacked bar, a slider per biome, Patch size). 15 biomes now: Lawn, Meadow, Flower field, Mulch, Glade, Forest, Rocky,
  Sand heath, Dunes, Shingle, Wetland, Pond (+ Fields, Orchard, Rough grazing from farms; Dunes and Shingle from beaches).
- **The zone tool's bar**, the one the user likes: `UI/src/controls/ZoneMix.tsx` (the zoning panel: each zone's share on
  one bar, labels inside, dragged directly). Study exactly how it behaves (what sticks out, how a drag moves the
  neighbours, how small shares stay grabbable) and bring that feel to the biome mix.
- **Parks**: `Core/Layers/Park/`, `docs/runbooks/parks.md`, `docs/refactor/parks.md`, the shelf `Recipes/shelf/parks.json`.
  P2-4 just made walks follow the contours on hills with painted steps (`docs/runbooks/p2-polish.md`); don't undo it.
- **Other park-like types** that offer the mix: botanical garden, memorial park, adventure playground, nature types,
  farms, beaches. Whatever you change in the shared mix must keep working (and look right) on them.

## What to build

### 1. Full means full: the mix does what it says

- 100 % Meadow means **no trees and no forest bits** anywhere a biome rules. Find every source of the stray trees: the
  park's own planting layer, path-side avenues, the boundary, moods, companions, rim scrub, grove noise. A biome at 100 %
  owns the whole natural ground. Where trees come from the park's design (avenue, boundary row), make that a setting the
  player can see and switch, not a surprise.
- Every share is honoured within a few percent on every site shape and on hills (measure it and log it per build:
  "biome shares asked vs built").
- Write a test: 100 % of each biome, nothing of the others planted in natural ground.

### 2. Every kind of city park

Not every park is a forest with mulch. Add the biomes and park styles that real cities have:
- **Field / open grass:** a big mown field, nothing fancy, but it still looks great. Mowing pattern only where it
  belongs (the shared rule: stripes only on bounded pitches). Otherwise the field has:
  - mown and long-grass edges, worn desire lines;
  - goal-post wear and bald patches, clover and daisies, molehills;
  - the odd frisbee-worn spot, puddles in the low ground, leaves under the edge trees.

  This is the user's first ask: "even a field should have good looking decals."
- **Kickabout / rec ground**, **sun lawn** (sunbathing lawn with picnic wear), **dog park** (fenced, worn, agility bits).
- **Formal city green or square garden:**
  - clipped lawn panels with crisp edges, gravel walks;
  - the London square, a Paris jardin, a German Stadtpark.
- **Japanese garden:** raked gravel, moss, stones, pruned pines and maples, a pond with stepping stones.
- **Prairie / wildflower meadow**, **pocket park**, **linear park** (along a strip), **urban plaza-park** (hard and green
  mixed).

Look at real references (name them in your runbook). Each is a complete ground design (paint, surfaces, decals, planting).
Some are biomes (they can mix), some are park styles (presets of the mix plus park settings): decide which is which and
say why in your runbook. Every park style also gets a preset that sets the mix to its look.

### 3. The landscape panel: really good UX

- **The bar:** replace the stacked bar and sliders with a bar that behaves like the zone tool's (direct drag between
  neighbours, labels and shares on the bar, small shares still grabbable). Add or remove a biome with one click (a
  picker), and set one to 100 % in one click.
- **Global versus per-biome:** make the structure obvious at a glance.
  - Settings for the whole park: patch size, terrain, paths, facilities.
  - Settings for one biome: tree density and species, rock amount, flower colour, wetness, mowing, and so on.
- **Per-biome settings:** click a biome on the bar to open its own few settings (3–5 each, the ones that matter). Find
  out what the existing controls below the card do. Fold any that are really per-biome into that biome, keep the true
  globals together, and drop duplicates. No control may do nothing (`ControlEffectTests`).
- Short labels, and no explanations needed: the user wants it obvious, not explained. Panel checks: the i18n test in all
  12 languages, nothing cut that is whole in English.
- Show the user mock-ups of the new panel early (the UI mock: `UI/mock/`, screenshots), then build it.

### 4. Push the look to A+

- Parks already grade near A+. Find what still keeps each style from A+ in the pictures (close-ups especially) and fix
  it.
- Every new biome needs decals that look designed: shared library kinds plus new ones as needed. Show them in the decal
  browser, and keep within the shared memory budget (measure it with every category at once).

## Stay in your lane

- You own parks and the **shared biome mix** (its core and its panel) for this round. The parking looks agent (P2-5) is
  running at the same time in `Core/Layers/Parking/` and must not be touched.
- Changes to shared biome code must keep every type that offers the mix working: run farms, beaches, nature and the
  botanical garden in the game runner too.

## Where to test

- Every park style and biome at 100 %.
- A mixed park on a city block, 220×200 m, and a big site.
- A 5–15 % slope and hills.
- Pictures at 45°, top and close.
- Before-and-after of today's park styles, so nothing the user liked is lost.
- Build, bulldoze, count what's left.

## Done means

- 100 % of a biome is 100 % of it (no stray trees), and the shares are honoured and logged.
- A plain field park looks great.
- The new biomes and city-park styles are A+ in the pictures, judged against named real places.
- The landscape panel drags like the zone tool, with per-biome settings and obvious globals. The user has seen the mock-ups.
- Every control works, and every text is in 12 languages.
- The other types that use the mix still look right.
- `docs/runbooks/biomes.md` and `docs/runbooks/parks.md` are updated, and the general lessons go in `docs/GROUND-RUNDOWN.md`.

Don't stop at a milestone. Report at milestones (pictures with full paths, a self-grade per style, the exact gap to A+),
but keep going until everything is A+. The lead reviews the pictures and sends work back.
