# The strip mall, started over (shops4, 2026-10-17)

## The user

> A strip mall should be a PARKING LOT with shop buildings in and around it. [The old one] looks like a park with some
> parking on it ... why is it so much of a loop, how do the cars even get in there, what are those weird green rectangles,
> why is there a path going nowhere? ... I wouldn't even mind if you deleted all of them and we just started over with a
> better strip mall. Let's just perfect one thing and then make permutations of it.

Later in the same session: "maybe 50 % shops, 50 % parking lot", "try to only use painting" (no reshaping but the parking
lot itself), "a pond could be cool".

## Why the old one read as a park

The rf-shops3 centre (`StripMallCentre.cs`, still in the code, no longer called) laid out a ring road round the site, a
12 m planted frontage, a walkway spine, a plaza, islands, pads and corner courts first, and planned the lot last on what
was left. The lot planner failed on the fragments: on the 150 x 140 m block 13 000 m² of 16 000 m² was planted and 64
stalls were built (game run `shops4-base`). The leftover pieces were drawn as lawn surfaces and groves.

## The new layout (`Core/Layers/Commercial/LotCentre.cs`)

- **The lot first, one piece**, entered straight from the player's entrances (the parking planner's own connectors, as the
  parking type). The lot's own drive runs round it (no aisle ends in a turning circle).
- **One shop lane at the back** (the side away from the car entrances), the shops behind it facing the lot. The lot's
  aisles join the lane at both ends: no loop, no dead end.
- **Where the row goes** is scored: shop ground and lot in balance, less the land left behind the row (a round site's long
  chord, not its short rim). Every car entrance's own side is tried as the front, plus the outline's long edges; the
  layout that connects every entrance wins.
- **Big sites**: double rows (shops back to back, ringed by their own lane, three cells deep) between bands of lot one
  aisle pair deep: about half shops on 300 x 280 m. **Mid-size sites (over 2.2 ha)** too shallow for a double row: the U
  (side rows of smaller shops), unless an entrance is on a side.
- **The split (2026-10-18).** The user, of the build above on a 190 x 180 m block with the plaza: "strip malls look like
  they're 90 % parking lot, 10 % buildings. Shouldn't it be kind of more of a split?" Measured offline with the game's
  16 m shop lanes: shops 11 % of the site, asphalt 58 % (the U and the double rows never came in: the U was off with a
  square, the double row needed 9 m more depth). Now every layout behind the best lane lines is estimated from its
  geometry and the most even one is planned first (`Variants`, `Estimate`, `Even` in `LotCentre.cs`): double rows (0 to
  8, shops three or two cells deep), the U on or off (its side rows four or three cells deep: two-cell side rows gave
  less of both), with or without the square, combined (a U round double rows on big sites). The lot counts only its
  whole aisle bays; the bands between rows are whole bays and the front band takes the rest, and the line may sit a
  little short of the back so the lot ends at the shops' lane instead of leaving a strip of grass before them. A layout
  that plans out well short of its estimate (an entrance behind the row took its shops) is taken back and the next
  tried; the plain row is always among the tries. A car entrance that would not come in through the lot (its drive to
  a lane has no ramp from a street above or below the ground) costs a layout 4000 m². Offline (shops / asphalt): the
  user's block 20-24 / 27-29 % (was 11 / 58), 220 x 220 m 23-26 / 27-30 %, 300 x 280 m 24-26 / 31-35 %; a 150 x 140 m
  block 16-17 / 27-39 % (the back street's zone margin and the lot leave no room for more). Plan facts `estShops`,
  `estLot`, `evenPlanned`, `sideCells`, `midCells`. Offline renders: `Tests/Placemaker.Tests/StripMallSplitTests.cs` (Probe)
  (`PM_PROBE_OUT`); game scenarios: `.forensics/split/gen.js`.
- **Double rows out in the lot lose plots in the game (open).** Game runs `send-split-1018-103736` and `-111039`: a
  double row inside the U zoned every planned cell (220 x 220 m 241 / 241, 300 x 280 m 370 / 370), one out in the lot
  without the U lost 21-47 % (the user's block 158 -> 114, 400 x 160 m with three of them 324 -> 172), the same loss
  the double rows had before (`shops4-it19`: 468 -> 372). Both rows of the island lose cells at the same spots; the
  log blames the game's depth rule and lot zone blockers. Where a U fits, no double row stands outside one (run
  `send-split-1018-114959`: the user's block with the plaza became the U, 128 / 128 cells; the classic preset there still
  took a double row in the lot, 178 -> 130, before this rule); where none fits, those rows count at three quarters
  (`DoubleRowZoned`). The cause is not found yet.
- **Ground**: nothing is reshaped on land that rises and falls less than 1 m under the lot; on a slope only the lot is
  levelled (parking grading); lanes are never laid steeper than 8 % (else "too steep"). Lawn is terrain paint kept 2 m off
  every road (it was a lawn surface that spilled onto roads).
- **Refusals the player sees**: too small (under 1600 m²), "no room" (too shallow behind the entrances or too narrow: a
  plain sentence with the metres needed), "too steep", "footpath behind the shops". A refused plan shows no ghost (the
  panel said "Updating preview..." for ever before).
- **Extras off by default** (walkways, plaza, pads, water, plot trees, building sites). New extra: **Square** in the middle
  of the back row: plaza / park / garden / pond. Presets `plazaRow` and `pondRow` (shown: seen in the game, runs `shops4-it8`, `shops4-it9`) and `parkRow` (hidden: its small loop walk still reads oddly).

## The shelf

Only the strip mall (classic) shows in Shops & squares while it is perfected (`Recipes/shelf/shops.json`). Mall district,
mall park, market square and big box are hidden, code kept: they come back as variations of this layout (mall park = the
square as a park, district = denser rows, market = the square as a market).

## Planning time

The preview planner's limit is 60 s now (was 12 s; the user: slow is fine while the panel shows it is planning). Big sites
plan in 1-3 s in the game after the row-line scans were cached.

## Game runs (`C:\Users\jack\cs2-area-tool\.forensics\suite\runs\`)

`shops4-base` (before), `shops4-it3` .. `shops4-it9` (it7: 27 builds, every shape and the odd-entrance sets). Scenario generator: `.forensics/shops4/gen.js` (styles x lands x
entrance sets: block, tiny 40x30, small 72x55, thin 170x32, triangle, odd concave, L, circle, big, mid, long, 6 % slope;
`oneway`, `corners`, `back` entrance sets). Offline renders: `.forensics/shops4/gal.sh` (land-tri-*, land-odd-* added to
the gallery).
