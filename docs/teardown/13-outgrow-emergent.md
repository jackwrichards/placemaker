# Teardown 13: Outgrow gardens shared by nearness (the emergent split)

The user, 24 Oct (three screenshots): where two outgrown rows meet at the back at an angle, the gardens "take jagged edges and
steal land from each other and shoot out in weird ways"; a designer would cover the land "with the little amount of bends as
possible"; and a hedge piece sticks out past a small bend. Then: "I don't love the idea of layering hundreds of rules ... I'd
much rather have emergent behavior", "performance is the biggest pillar", and of the variants: "the street one still looked the
best ... it makes those backs of the backyards nice and smooth".

## 1. Why the gardens were jagged

`OutgrowBench --explain` (below) replays one recorded pass and draws every garden after each step. Two rows at an angle: each
house paired with the one house nearest behind it and the middle line of those two lots cut its back, so every lot of an angled
row had its own back line; every other lot it overlapped split it square to the shortest way between corners (lines at -141°,
-35°, 55°, 110° in one spot); the land left over was handed out pair by pair (tabs and notches); and each pass fitted its
gardens to the neighbours' standing ones. One garden: 4 corners grown, 19 after sharing, 14 at the end.

## 2. What changed (knob `emergent`, 1 by default; 0 is the old split, kept)

`YardStitch.Emergent.cs`. Two ideas instead of the pair lines and the steps that patched them (wraps, walkways meeting, sharing
the rest, slivers, caps, straight backs, the spike cut):

* Fair: on a 1 m grid aligned to the world, each cell within reach goes to the lot nearest it, measured from the lot's street
  front (`emergentFront` 1; 0 the whole lot, between a blend), a 3 m strip round each lot its own (`emergentApron`). Land is in
  play when a garden reaches it on its own or it lies between houses (a lot on the far side, over 120° round); open land beside
  or behind a row stays open. Side neighbours keep their middle line exactly.
* Straight: each garden's outline traced from the cells; every run between two points where three owners meet (and every lot
  corner, snapped exactly) straightened (Douglas–Peucker, `emergentTol` 1.5 m, ties by position so both sides draw the same
  points), no edge under `emergentMinEdge` (2 m, a short fence piece) between them.

Fences on the new shapes: each house's own lines go onto the matching edge of its garden (`SnapLines`), the rest is closed with
pieces as before; then loose ends within 2.5 m are joined along a garden's edge (`JoinLooseEnds`), a run's last piece that
overshoots the fence it meets slides back to end on it, scraps under 1.5 m touching nothing go, a side yard along a street stays
open like the front.

Tried and dropped: a curve fitted through where two rows meet (gaps, broken edges); a garden another pass planned taken as final
(`emergentFixed`, copies older shapes: short edges 45 -> 143); yard strips under 6 m shaved (`emergentMinWidth`, 0).

## 3. Measurements

Game runner, `suite/scenarios/outgrow-askew.json` (five saved cases: two streets at 15°, straight and curve, two curves, a
cul-de-sac, two streets at 35°; every garden planned twice each way, the second round measured):

| | old split | emergent |
|---|---|---|
| fence map: fences / gaps / crossings / doubles / inside a garden / hidden by the game | 1886 / 7 / 0 / 131 / 0 / 20 | 1718 / 10 / 0 / 111 / 0 / 19 |
| Outgrow busy, every house again | 1.1-1.2 s | 1.3-1.5 s |
| main thread per redo: total / worst frame (ms) | 98-108 / 11-22 | 76-95 / 7-10 |
| planner on the workers (ms, all plans) | 3900-6200 | 5900-6200 |

`OutgrowBench --fences` on the same second round's passes (37-41 passes): pieces 1334 -> 1152, gaps 6 -> 5, crossings 1 -> 2,
doubles 154 -> 161, fences inside a garden 82 -> 72, unfenced garden edge 618 -> 319 m, lone stubs 4 -> 8, edges under 2 m
where the outline turns 63 -> 42.

## 4. Tools

* `suite/scenarios/outgrow-askew-make.json` grows and saves the five cases ('PM Askew Corner' holds them all);
  `outgrow-askew.json` loads it and plans every garden again each way (38 s in an open game).
* `OutgrowBench --explain <pass or dir> [--at x,z] [--size m] [--edit-all] [--final] [--px n] [--knob k=v]`: the gardens after
  each step (`YardStitch.Trace`, null in the game), the planner's reasons (`TraceWhy`), fence problems marked, an HTML page and
  a PNG.
* `OutgrowBench --fences <dir> [--knob k=v] [--every]`: gaps, crossings, doubles, inside, open, stubs, short over recorded passes
  (`PM_FENCE_WHY=1` names the fences of each gap and crossing).
* The game runner's test games are muted and at the lowest effects; a loaded city gets the runner's light.

Left: a few small corner gaps within one garden's own pieces (in the game 10 gaps against 7), and junctions where a garden meets
fences another pass laid; the fixed piece lengths (4 m, 2 m) still overshoot at some bends: one stretchable lane per run
(teardown 12, section 6) would end that.

## 5. 25 Oct: the older rules are the default again (branch og-classic)

Knob `classic` (1 by default) selects the older rules whatever `emergent` says (lots polished earlier carry `emergent` 1 in
their settings); `classic=0` with `emergent=1` gives this split back. The older rules gained one step, `Push`: after the cut
and tidy, every garden pushes its free sides straight out, parallel, into land nobody has (no new corner, angles kept), all
gardens a step at a time in turn, to exact contact; never a front, a side on a line shared with a neighbour, a walkway's side,
past a road's margin, more than `push` (60) m past its lot, or wholly further than `pushDepth` (50) m from a road. Offline
scores (Outgrow-Score.ps1, no knobs): boot camp 182.2, thinned town 131.2, black box 162.9.
