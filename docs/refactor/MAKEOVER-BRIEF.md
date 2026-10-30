# The makeover: shared brief for the five category agents (2026-10-29)

You are one of five agents, each making one category of Placemaker (a Cities: Skylines II mod) look like a photograph
of real life, without lag. The others run at the same time in their own worktrees: parks, plazas, nature, industrial,
leftover & rundown places. The sports park was the first type done this way ("nailed it"); its lessons are written down
for you. Master is at 8bb8063a or later and holds all of it.

## The user's bar (their words, 2026-10-29)

"Only when they're completely, completely done and they're totally satisfied with every close angle, side angle, wide
angle ... every single corner, close up, middle ... and all the colors complement and the leaves don't mismatch the
trees and the rocks look good. And we're using lots of rock assets themselves along with decals and dirt decals and
water runoff ... cracks and dirt on the edge of paths. It should look like basically a photograph of real life. While
also not being laggy."

Earlier, on the sports park: "it doesn't mean more or less decals, it means correct"; "within reason, do more with
less". They disliked big blurry smudges ("small smudginess is fine, macro smudginess looks odd") but said tone down,
not cut. Leaves must be summer leaves that match the trees above them (brown and speckled, not autumn orange under
green trees). Dirt is speckled grit and rubble, not mud.

**Neglect is not a slider.** "Neglect is more than stripping away features or adding features. It changes the entire
vibe of everything." A neglected or grungy version, if your category has one, is its own version (its own style or
type, with its own set of things in it), not a grime amount on the tidy one.

**Vibe first, technique second.** The user describes pictures, not methods ("I meant it as a vibe, not technically").
Find the technique that gives the picture.

## Freedom

The guides are how we did the sports park, not a cage. If a rule or part of a guide blocks a better idea, break it and
write down why in your runbook. Two limits only: **performance comes first** (frame time in a running city, planning
time, memory), and **build on the core systems, don't rewrite them** ("invent your decals, or invent a new way to draw
them, but don't rewrite everything"). Make as many new decal kinds as you need: they are data and cost almost nothing.

## Read first

1. `AGENTS.md` (the rules of this repo: game queue, languages, shelf, build, how the user works).
2. `docs/GROUND-RUNDOWN.md` (how to give any type a designed ground: brushes, new kinds, budgets, look rules, engine
   traps, the report). Then `docs/DECALS-PLAYBOOK.md` and `docs/runbooks/sportsPark.md`.
3. `docs/refactor/GRADES.md` (the user's grades and what they said per category).
4. The code of your types (`Core/Layers/...`, `Recipes/<type>.json`) and how they look in the game today: run them
   first and look before you change anything.

## Look at everything the game ships

The user: "they need to look into different tree types, reeds, rocks ... there's a lot of vanilla assets we skimped
on". Go through the game's own prefabs for your category (trees and their species, bushes, reeds and water plants,
rocks, boulders, cliffs, props, fences, surfaces) and use the right ones richly, with decals and terrain paint around
them as one look. Never the user's asset packs: vanilla only.

## Working setup

- Your worktree needs the shared UI packages: `cmd /c mklink /J <your worktree>\UI\node_modules
  C:\Users\jack\cs2-area-tool\UI\node_modules`. When removing a worktree, delete that link first (`cmd /c rmdir`).
- The build: `dotnet build Placemaker.csproj -c Release -p:Deploy=false`. Never install into the user's game, never
  push, never merge into master (the lead merges). Commit on your own branch per finished piece, with no attribution
  lines.
- Tests: `dotnet test Tests/Placemaker.Tests --filter ...` for what you touch (the full run takes 20+ minutes).
- Languages: anything the player reads, in all 12 (`node tools/lang/check.js` at 100 %, 0 changed, 0 broken; then
  `--stamp` each locale). Your own area's Lang files.
- No python; use node. Text with backslashes: write it with Edit/Write or node, not bash heredocs.

## The game (required for everything)

- Through the queue only, as a background command, named: `powershell -File tools/suite/Show.ps1 -Who <your-name>
  -Category ... -Type ... -Style ...`. Never `-AllMods`, never `-Direct`. Five agents share one game: ask for the
  pictures you need, batch styles into one request, and expect to wait in line; that is fine.
- Views: `-Views 45,top,low,close`, land with `-Size`, `-Ground` (slope, hills, mountains), see `suite/README.md`.
  For corners and close-ups beyond those, write a scenario with camera steps (`suite/scenarios/ground-sports.json` is
  a worked example, also for frame time) and run it with `Send-Suite.ps1`.
- Look at every picture yourself, close, mid and wide, and read the build's lines in `Placemaker.log`.
- Close the test game when your results are in, but only when `tools/suite/Queue.ps1` shows nobody else in line and
  nothing running: write `{"command":"quit"}` to a file in the newest `.forensics/suite/runs/queue-*/inbox`. Never
  close a game the user started.
- If a boot fails with "Out of memory", say so in your report.
- Frame time: measure your heaviest style with and without your ground (as the sports runbook did). A real cost is a
  bug to fix before you are done.

## Sharing files with four other agents

- Your category's code in its own files. Shared files (`Core/Decals/Dynamic/Brushes.cs`, `Weathering.cs`,
  `DecalLibrary.cs`, `DecalVariants.cs`, `Game/Decals/SharedDecals.cs`, `Core/Decals/GroundWear.cs`): small, additive
  changes only, never reshaping what others use. If you need more, add a new file beside them.
- Your new decal kinds in your own library file: `Recipes/decals/library/<your-category>.json` (every
  `library/*.json` is loaded). Reuse the shared kinds freely; don't retune them for everyone (copy and tweak instead).
- Only your own shelf file (`Recipes/shelf/<category>.json`), except where your brief says to move a type.
- The sports park's site reader (`Weathering.Scene`) is a good start; if you lift it into a shared piece, keep the
  sports park building the same (its tests: `GroundDesignTests`).

## Write it down

Keep `docs/runbooks/<your-category>.md` as you go (the sports runbook is the model): what you made, every look rule
the pictures taught you, numbers, runs, mistakes. Add your best general lessons as short dated lines in a section of
your own at the end of `docs/GROUND-RUNDOWN.md` ("Lessons from <category>").

## When you are done

Only when you are completely satisfied with every style from every angle. Your final message (it goes to the lead, not
the user): your branch and last commit; what is on the shelf now (types, styles, hidden); the runs and the pictures that
show it best (full paths); frame time with and without; language check; tests; anything left or broken.
