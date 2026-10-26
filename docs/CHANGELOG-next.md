# Placemaker: what's new since 0.2.0

Everything since the 0.2.0 upload to Paradox Mods (21 Oct 2026).

## New

- **Style photos.** Every style shows a photo of how it builds, at the top of the Design page: Close, Angle or Above.
  The view you pick is kept, and the photo can be folded away. Roads open on Above. Your own saved styles have no photo.
- **Every style has a real name.** "Standard" is now named for each type: Playing fields, Town grid, Neighbourhood
  park, Nine-hole course, Tree-lined lot, and so on.
- **Site plan sketch.** A new look for the Sketch preview, on by default; the old one is still in the options as "Classic".
  - Draws what gets built: roads with dashed markings, paths, lawn, paving, water, tree crowns, painted parking spaces,
    and every object as a rough shape (cars, containers, stalls, benches...).
  - Draws the same way at any camera angle.
- **Top view.** A small map of the build in its own window next to the panel, with a legend.
  - Hover a legend row to make it blink on the map; click it to show or hide it.
  - Click the map to see it large.
- **Planned water.** Ponds, lakes and flooded pits show as water in the sketch and in the top view before you build;
  islands stay dry.
- **Terrain depth on request.** The ground's height changes are hidden at first; turn on Terrain in the top view to see
  them, on the map and in the top view together.
- **Review, reorganised.**
  - Cost & park stats in one section.
  - Warnings and Checks are now separate.
  - Checks lists what the preview can't show until it's built: ground shaping, ground paint, water filling in,
    buildings growing.
- **Ungroup.** Build as separate pieces, or ungroup a finished build from its info panel, so the bulldozer takes its
  parts one by one.
- **Entrances with or without a crosswalk.** Footpaths stop at the kerb.
- **Right-click** takes back the last thing you placed; while choosing the area it starts over.
- **Paint bucket** shows its reach as a dashed circle at the cursor.
- **12 languages.** Placemaker is in every language the game has: English, German, Spanish, French, Italian, Japanese,
  Korean, Polish, Brazilian Portuguese, Russian and Chinese (Simplified and Traditional).

## Changed

- **Vanilla by default.** Everything Placemaker picks on its own is vanilla: surfaces, decals, props, trees, park
  styles, parked cars, brushes. If vanilla has nothing that fits, that item is left out instead of borrowed from a mod.
  Anything you pick yourself can still be any asset you have installed.
- **Park upkeep shown honestly.** The upkeep in Review is what the game will charge. That includes the park's running
  cost, which is like a vanilla park of the same quality and was missing before. A park showed about ¢50 a month and
  then cost thousands.
- **A park works as a park only when grouped.** Built "Not grouped as one asset", it has no upkeep, but no tourism,
  leisure or wellbeing either, and Review warns you. Ungrouping a finished park takes its park building away; grouping
  it again brings it back.
- **Roads no longer zone.** Zoning is the Zoning category's job; the zoning setting is gone from Roads and no road style
  zones.
- **Design page.**
  - The Placemaker library (category, type, style) and the style you're editing are one clear flow, with Revert, Save
    and Save as on the style card.
  - Shape and Extras are one Parameters section.
- **Preview modes.** Design always shows the Sketch; Review opens on Real, with a Sketch/Real switch.
- **Real preview** looks closer to the finished build: no haze or blue outlines, just a thin line round the area.
- **Confirmations** appear in the panel's own style, in one short line.

## Outgrow (beta)

- New window: one switch shows or hides Outgrow, plus a paint brush (Paint, Erase, the type) and a map (Off, Status,
  Types) with counts.
- New settings per type: Grow sideways and Outgrow depth, with a live picture of where they reach.
- Each save keeps its own zone defaults.
- A new city starts with all of Outgrow's city switches off.
- Much faster: a 241-house town now outgrows in about 2 seconds, down from 55.
- No more stutter in big cities.
- Gardens and fences:
  - No gardens over streets, other lots, or each other.
  - No gardens under overpasses.
  - Fences run without gaps, zigzags or spikes, and two neighbours share one fence.
  - Front yards stay open.
- Gardens behind a walkway, along a curve, or at an angle to their neighbours now meet properly.
- Bulldozing a house replans only the gardens touching it.
- Land zoned under a car park grows buildings again.
- Options: show every outgrown garden's outline (for checking), and export your Outgrow settings to a file.

## Faster

- Parks plan about 3-6 times faster, with exactly the same results.
- Large painted parking lots plan 7-10 times faster.
- The sketch on a big lot went from about 16 to about 165 frames per second.
- Changing a design no longer freezes the game for a second.

## Fixed

- Builds could leave duplicate records after loading the same city twice in one game (GitHub issue #6).
- Long translations no longer get cut off in the panel.
