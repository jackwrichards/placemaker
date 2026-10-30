# Placemaker game runner

## The game queue: every game command goes through it

Only one game can run, and every build installs into the same `Mods\Placemaker`. So `Show.ps1`, `Run-Suite.ps1`,
`Send-Suite.ps1` and `Play.ps1` do not launch the game themselves: they put a request in one machine-wide line
(`%USERPROFILE%\.placemaker\game-queue`; not under AppData, which the agents' command sandbox keeps private per
session) and wait their turn. Ten agents share one game, launched once per build.

* **Run the command in the background.** It blocks until its results are back, then prints what it always printed
  (the builds, the pictures, the log lines) and exits with the same exit code, so you are woken up when it exits.
  It never waits for ever: when no dispatcher has answered for 3 minutes (or none could be started 4 times) it prints
  `STUCK` with the dispatcher's last log lines, leaves the line and exits 1. Keep the shell that started it: a command
  whose parent process is gone leaves the line (exit 2), so don't `Start-Process` it from a shell that then exits.
* **`-Who <name>`** names you in the line (`ahead:` and `now:` lines, `Queue.ps1`). The default is your git branch,
  the same for every agent in one worktree: pass your task's name.
* While it waits it prints a line every ~15 s, and at once when its place, who is ahead or what runs changes:

  ```
  queue: #2 in line | ahead: lead (3 build(s), 9 pictures, relaunch) | now: fix-roads 1 of 2 build(s) done, 40 s in | game open with build 3f2a1c9e7b10 | your build needs a launch | ETA about 2 min 10 s (around 21:14)
  queue: running yours in the game: 5 of 22 build(s) done, 4 min 2 s in (build 3f2a1c9e7b10); ETA about 13 min
  ```

  `#N in line` is your place; `ahead` is who goes first, with their builds and pictures (`relaunch`: their build needs
  the game restarted); `now` is what the game does, with how many of its builds are done. Your build `is the open
  game's`, `shares the launch for <who>` (the game opened for someone ahead with the same build) or `needs a launch`.
  The ETA is measured (`history.json`): boot and quit times, and per build its time without pictures plus a time per
  picture (taken from the game's own timings; per type once a type has 3 measured builds: parks settle their pictures
  slower than plazas), so 22 builds with 14 pictures each are estimated as such; the request in the game is followed
  build by build (`over its estimate` when one takes longer; `over its usual boot` for a slow start). `waiting: the
  user's own game is open`, `the user is starting the game` (the Paradox launcher of Cities: Skylines II; another
  Paradox game's launcher does not count, and one left open for over an hour without a game is no longer waited for)
  or `the user's playground is open`: the line waits until the user closes the game, and the ETA is unknown.
* **Your build is frozen when you ask.** The command builds your worktree without installing it (unless `-NoBuild`)
  and copies what Deploy would install (`bin\Release`, `UI\build`) plus the game's inputs (`suite\schema.json`,
  `suite\scenarios\smoke.json`) into the queue. Their hash is the build's identity. Keep working while you wait: later
  edits do not change your request. The game host that installs it, switches and restores the user's settings and puts
  the user's build back is always the dispatcher's own checked copy of `Run-Suite.ps1` and `ModsInstall.ps1`, never
  yours: an edited `Run-Suite.ps1` does not change what the queue runs (debug the runner with `-Direct`).
* **Batching.** The game stays open with a build. Requests with that build run back to back in it without a relaunch
  (land made to order is laid out in the open game when its request comes). Another build waits for the request in
  the game, then the game quits cleanly and relaunches with it (about 60 s). The oldest request with the open game's
  build goes first, but only while the head of the line would still start within 10 minutes of asking (its relaunch
  counted); once the dispatcher has kept a head first it stays first, so places in line do not flip (the plan behind
  the progress lines only predicts: it never keeps a head first by itself). A request that does not fit the open
  game's test grid gets a fresh launch, and so does one that would still run when the game has been open 7.5 hours.
  A request whose build makes no progress for 5 times its estimate (at least 6 min) is stopped like one over its time.
* **The user comes first.** `Play.ps1` goes next after the request in the game; the line then waits until the user
  closes the playground. A game the user started themselves (or the Paradox launcher) is never touched: the line waits
  for it. If the user starts one while the queue's game is open, the request in it finishes and the queue's game quits
  (one still booting is stopped). After a game the queue did not start closes (the user's, a playground), and after the
  queue's own game is stopped from outside (Steam's Stop, Task Manager, its window closed), the queue waits 2 minutes
  before it launches a game, so the user can start theirs (a request in the game when it was stopped goes again once).
  A crash is not a stop: that request fails, naming the build that was running and how many never ran.
* **`Mods\Placemaker` is the user's** whenever no queue game runs (`ModsInstall.ps1`). The queue's game host swaps a
  request's frozen build in right before it launches the game (after it holds the suite lock and has checked that no
  other game runs) and swaps the user's build back the moment that game has exited. Nothing is written while any
  Cities2.exe runs: if the user starts a game in that moment, the user's build goes back once it is closed (the
  dispatcher stays up until then); the user's game settings likewise (a settings file the user's game changed or made
  after the run is kept, except the ones the suite profile itself switches). `install.json` records the version of the
  install code that wrote it: an older copy (a stale worktree's `Deploy-Mod.ps1`) then refuses to touch the folder, and
  a state file that cannot be read stops every change until it can. A build installed on purpose (`dotnet build -c
  Release`, a `-Direct` run) becomes the user's build, once nothing has been written into the folder for 10 s; so does
  the build the user played in `Play.ps1`, once the playground has opened (it stays installed, as it always did). The
  user's build before that is kept in `user-build.prev`.
  **The queue does not install your build for the user's own game.** `dotnet build -c Release` (without
  `-p:Deploy=false`) installs one through `tools/suite/Deploy-Mod.ps1`: under the queue's install lock, as a staged
  copy and two renames, and refused (the build fails, nothing is touched) while any game runs or a queue game owns the
  folder. Installing for the user is the lead's job.
* **Leaving the line:** Ctrl+C (or killing the command), or deleting its folder in `requests\`. A command whose queue
  is stuck (no live dispatcher has updated `status.json` for 3 min, none could be started, or the request sits in
  `active\` unrun) prints `STUCK` and exits 1.
* The dispatcher (`Game-Dispatcher.ps1`) starts by itself with the first request, and stops when nothing has been in
  line for 10 minutes (the game quits first) and the user's build is back. It runs from a copy of its scripts in the
  queue folder (`dispatcher\<hash>\`), not from the worktree that started it, so a worktree removed or edited later
  cannot break it; a copy that does not parse is never started, and `dispatcher\good.json` names the last copy that ran
  well (a client falls back to it). The copy is what that worktree has committed: uncommitted edits to the queue's
  scripts (`Game-Dispatcher`, `GameQueue`, `ModsInstall`, `Run-Suite`, `Contact-Suite`) are never run for everyone.
* Results go to `-ResultsRoot`, by default `C:\Users\jack\cs2-area-tool\.forensics\suite\runs\<runId>\` whatever
  worktree asks (the commands print full paths). Each request's folder is its own: its builds numbered from 01 with
  the ids it asked for, its pictures, and its own part of `logs/Placemaker.log` and `logs/Player.log` (from where the
  logs stood when it was fed); `suite.log` is the whole game's. The pictures are hard links to the game's own files
  (no second copy on disk). The game's own run folder is `runs/queue-<time>/` (removed after 3 days, as are
  `runs/*.old-*` and Show.ps1's set files in `show/`).
* `powershell -File tools/suite/Queue.ps1` shows the line (`-Watch`, `-Log 40`, `-Cancel <id>`) and what
  `Mods\Placemaker` holds. Use it rather than reading `status.json` yourself (it is replaced while you read). In the
  queue folder: `requests\`, `active\`, `done\<id>\result.json`, `dispatcher.log`, `launches.log` (one line per game
  launch), `install.json` and `user-build\` (the user's build), `bundles\` (frozen builds, 2 h after last use).
* **A branch older than the queue** (master before `932753b`) has the old commands, which launch the game themselves
  outside the line and ignore the queue's game and its install lock: merge master into it before running any of them.
* `-Direct` on any of the commands launches the game here and now, outside the line: for debugging the runner only.
  It is refused while any game is open. `Run-Suite -Queue`, `-Set queue` and `-Set bootstrap` (a game of your own
  kept open) need `-Direct`.
* Through the queue, `Send-Suite.ps1` runs your file in the queue's game with your current build (`bin\Release` as it
  is; `-Build` rebuilds first). `on` works only within one file: the next file may land in another game.

## See builds: `Show.ps1`

```
powershell -File tools/suite/Show.ps1                                          # the categories, types and styles
powershell -File tools/suite/Show.ps1 -Category parks -Type park -Style formal # the usual: 3 pictures
powershell -File tools/suite/Show.ps1 -Category parking -Type parking -Style all -Views top -Modes clean,zoning
powershell -File tools/suite/Show.ps1 -Category shops -Type all -NoPlan
powershell -File tools/suite/Show.ps1 -Type park -Shape circle -Size 140 -Ground hills
powershell -File tools/suite/Show.ps1 -Type parking -Shape pinch -Size 180x110 -Ground slope -Grade 8 -Toward east
powershell -File tools/suite/Show.ps1 -Type roads -Size 900x700 -Ground mountains -Roads none -Views top
powershell -File tools/suite/Show.ps1 -Type plaza -Land my-land.json     # any outline, any heights
```

One request for everything asked (a new game takes about 60 s, then about 10 s per build; an open game with the same
build is reused). Each build gets its
own test land and is built the way a person does (select the area, pick type and style, entrances, Review, Build). It
prints, per build, its pictures and its lines in `Placemaker.log`; `Player.log` has the game's own errors.

**Start with the default three pictures**: `plan` (Review before building: the outline and the ghost), `45-clean`
(the finished build at 45°) and `terrain` (the ground's height map). Ask for more only when those did not show what
you needed; don't dump pictures.

**Land.** Without land options a build gets a flat 150 x 140 m city block of the test grid (`-Site` for another kind).
With any of them it gets its own land, made to order at launch (east of the grid) before the roads go in:

* `-Shape`: `rect` (default), `circle`, `pinch` (a waist in the middle, `neck` 0.3 of the depth), `l`, or `points`
  (any outline, in `-Land`).
* `-Size`: `W x D` metres (`180x110`), or one number for both; 4 to 4000 m.
* `-Ground`: `flat` (default: perfectly flat), `slope` (`-Grade` in % or as 0.06, `-Toward north|south|east|west`),
  `hills`, `bumpy`, `mountains` (`-Amplitude` m, `-Wavelength` m), or your own heights: `-Heights` a JSON file of
  metres (`[[m, m, ...], ...]`, rows north to south, stretched over the land) or `-Heightmap` a greyscale PNG (`-Range`
  metres for white).
* `-Roads`: `around` (default: a ring road, so entrances have a street) or `none` (open land, e.g. to test roads).
* **The land touches its street (rf-shops3).** Show.ps1 gives made-to-order land `gap` 10 (the outline about 2 m off
  the kerb of the 16 m ring road, as a user draws up to a street) and, for `l`, `circle`, `pinch` and `points`, a ring road
  that follows the shape (`roads` `follow`). The old default (16 m) left the outline 8 m off the kerb, out of every type's
  frontage reach: shops found no street and fell back to a park. Pass `gap` (or `roads` `around` round a circle) on purpose
  for a site that does not touch its street. Scenario files (Send-Suite, Run-Suite -Set) keep the game's own default (16).
* `-Land`: the same as JSON (a file, or the text; keys in `suite/schema.json` "land"), merged over the options, e.g.
  `{"points": [[0,0],[90,0],[90,40],[30,70],[0,60]], "ground": "bumpy"}`.

The area is drawn in the land's shape with the Points tool. Land is laid out at launch, or, in a game that is already
open (the queue's), when its request arrives.

* `-Category`: a category of the Make row, as the central shelf defines it (`Recipes/shelf/<category>.json`: roads,
  parking, parks, beach, shops, nature, farms, sports, industry; transport is hidden). `Show.ps1` with no arguments
  lists them with their types and styles; hidden types and styles are marked.
* `-Type`, `-Style`: one or more (`a,b`), or `all` (with `-Category` for types). Every type x every style is built.
  `all` leaves out what the shelf hides; a hidden type or style named by id is still built (picked by id: the panel
  shows no chip for it), and the run says so.
* `-Grow <seconds>`: grow the city after the build, before the pictures (shops on the zoned plots; with `-Modes
  zoning` you see which road each grown shop faces).
* `-Views`: `45` (default), `top`, `low`, `close`. Framed on the build (`close` looks at its middle).
* `-Modes`: `clean` (default, no overlays), `zoning` (the game's zone grid: zoned cells in their zone colour, the way
  the road and zoning tools show it), `overlay` (Placemaker's and the game's overlays), `ui` (the real screen),
  `terrain` (a map of the ground's height from above, once per build: blue below the outline's typical ground, red
  above, shaded by slope, contour lines every 0.5 m on gentle ground (1, 2, 5 or 10 m on hills), every 4th darker,
  the outline in white; only the ground, no buildings, trees or roads. Flat ground is a pale area without lines; pits
  and mounds are rings. Show.ps1 prints the lowest and highest ground in metres). Default: `clean,terrain`.
* Every view x every mode is taken after the build (`<view>-<mode>.png`); `-NoPlan` skips the plan picture.
* `-Site slope|rough|zoned|survive` for a block other than flat.

Anything else (edits, deletes, entrances in given places, growth, several steps) is a scenario file you write
yourself, below.

## What it is

A simple tool: it launches the real game, does the steps you write, the way a person would, takes the pictures you ask
for in the order you ask, and collects the logs. It judges nothing. There are no PASS / FAIL checks; **you read the
logs** and look at the pictures.

* **The logs are the point.** After a run, the run folder has:
  * `logs/Placemaker.log`: everything Placemaker did, including plans, the build audit, left-out pieces, zoning and
    errors.
  * `logs/Player.log`: the game's own errors and exceptions.
  * `suite.log`: what the runner did, one line per step, with timings (step time, worst frame, plan time) and the
    picture each step took.
  * `audit/`: Placemaker's build audits written during the run.
* **Pictures come second**, in order: `contact.html` (every shot, captioned) and `shots/`. Use them for an artistic
  look, or to catch what the logs don't say: things built but never drawn, a fence across an entrance, a pit, shops
  facing the wrong street.
* `summary.md` says what ran (one row per scenario: status, build path, time, shots) and lists any step the runner
  could not do (a click that changed nothing, a button that was greyed out). `scenarios/NN-<id>.json` has every step's
  record.

## Default use: one 45° picture per build, many builds per launch

The most useful picture is the finished build seen from 45° (camera south of the build, noon sun behind it). Queue as
many builds as you like into one launch. Each gets its own block and one final 45° shot.

* **Every preset of one type:** `suite/scenarios/look.json`. Change `"layer"` to the type you want, or replace it with
  `"group"` for a whole group. Groups are the types' own (`parks`, `gardens`, `water`, `commercial`, `squares`… as in
  `suite/catalog.json`), not the folded categories of the Make row; hidden types (industry, transport) can still be
  named here.
* **One specific type and preset:** `suite/scenarios/look-one.json`. Change `layer` and `preset`.
* Type, preset (style) and option ids for this build are listed in `suite/catalog.json`, written by every run.

```
powershell -File tools/suite/Run-Suite.ps1 -Set look        # launch, build them all, one 45° shot each, quit
powershell -File tools/suite/Run-Suite.ps1 -Set look-one
powershell -File tools/suite/Play.ps1                       # the user's playground: the flat grid, controls handed over
```

Or feed builds one file at a time. Through the queue each file runs in the queue's game with your current build (no
relaunch while that build's game is open; a code change always needs one):

```
powershell -File tools/suite/Send-Suite.ps1 -File my-builds.json
powershell -File tools/suite/Send-Suite.ps1 -Json '{"command":"grow","seconds":40}'
```

Debugging the runner outside the queue, with a session of your own:

```
powershell -File tools/suite/Run-Suite.ps1 -Direct -Set queue -Queue
powershell -File tools/suite/Send-Suite.ps1 -Direct -File my-builds.json
powershell -File tools/suite/Send-Suite.ps1 -Direct -Quit
```

Send-Suite prints each scenario's status, any step the runner could not do, its pictures and where the logs are. Exit
code 0 = done, 1 = the runner reported an error or timed out, 2 = schema error, 4 = the game went away.

Timings and limits:
* About 60 s from launch to a ready test world, then about 5–12 s per build.
* The default grid holds about 75 flat blocks; a block is used once. For more, split by `group`, run several launches,
  or pass a bigger `-Grid`.
* Water and slope types look odd on flat blocks; use `"site": "slope"` where that matters.
* Commercial types show empty shop plots unless you add `{"do":"grow","seconds":40}` before the camera step. That
  grows the whole city.

If a run is cut off, its logs are not copied into the run folder. Read them in
`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\` before the next launch overwrites them. Then
run `Run-Suite.ps1 -RestoreOnly` so the user's game settings are put back.

Only one game at a time: the queue (above) runs one game for everyone. Underneath, Run-Suite takes
`.forensics/suite/lock` (one for every worktree), and stops when a Cities2.exe it did not start is running (a runner
game it left behind is asked to quit through its inbox; the user's own game is never touched).

Other Run-Suite switches: `-AllMods` (the user's whole playset; slow boot), `-UserModSettings` (keep the user's
Placemaker options), `-Look game` (the map's own light and weather), `-Pace settled|user`, `-Grid 20x9`,
`-MaxScenarios N`, `-NoBuild`, `-RestoreOnly` (put the user's settings back after a cut-off run).

Sets in the repo: `look`, `look-one` (above), `smoke` (4 quick builds), `core` (every parking and commercial style,
plazas, parks, roads, both build paths, user pace), `full` (every type), `steps` (step scripts of every tool),
`examples` (the scenarios in `suite/scenarios/examples/`). Any file works with `-Set path\to\set.json`.

# Reference

One game launch: a new game on the vanilla map Plains, the fixture area reshaped in place (water removed, trees and
nets bulldozed, flat / slope / rough ground easing into the map), a grid of vanilla roads, low-rise housing grown on
the zoned row, two plopped parks, then the scenarios, each on its own block. Every action is done the way a person does
it, and every picture is taken only once the scene is still.

What a run does to the game, so you know what the pictures are not:
* **Clean profile**: only Placemaker loads (`-disableModding`); no asset packs, no other code mods. Decals, surfaces,
  fountains/statues and park dressing differ from the user's game. `Run-Suite -AllMods` loads the user's playset.
* **Readability mode**: the light, weather and icons are the runner's, not the user's (see Look).
* Placemaker's options are **pinned to its defaults** for the run (summary.json `placemakerSettings`, with every option
  that differs from the user's); `-UserModSettings` keeps the user's. The user's settings and ModsData/Placemaker
  files are saved before the run and put back after it.
* Money is unlimited and everything is unlocked.

## Scenario files

The vocabulary is in `suite/schema.json`. Unknown keys, steps, targets and values are refused before anything runs
(Send-Suite checks a file before sending it; the game checks every set and step).

A set (`suite/scenarios/<name>.json`, or any file with `-Set path\to\set.json`) is
`{"scenarios": [...], "settings": {"Option": value}}`.

| key | meaning |
|---|---|
| `id` | the scenario's name (results and PNGs are named after it) |
| `layer`, `preset` | a one-line scenario: bucket the block (or `draw` a rectangle), pick the type and style, entrances where the button asks, preview (an overlay shot and a clean ghost shot), build, built oblique and top shots |
| `draw` | `[w, h]` metres: the Points tool draws this rectangle at the block centre instead of the bucket |
| `values` | `[{"path":"0","id":"aisleWidth","json":"7"}]`: options set before the preview |
| `steps` | a step script (below) instead of the one-line steps |
| `expand` | `"presets"` (each style of `layer` or `group`; `limit` = the first N styles of each type, `skip` = style ids) or `"types"` (every type in the Make row); with `steps` the steps are a template (`type` without `layer` takes the expanded type and style). Ids become `<layer>-<preset>` (`-small` with `draw`), a duplicate gets `+` |
| `site` | `flat`, `zoned` (row 1: low-rise housing on its south street, grown before the first zoned scenario), `slope`, `rough`, `survive` (two plopped parks), `any`. **Never replaced by another kind**: when none is left the scenario is not run (`no-block`) unless `siteFallback: true` |
| `block`, `streets` | a given block (`"B3-0"`), or a street signature (`{"south":"Medium Road","north":"Small Road"}`) |
| `land` | its own land instead of a grid block (see Land above: `shape`, `size`, `points`, `neck`, `ground`, `grade`, `toward`, `amplitude`, `wavelength`, `heights`, `heightmap`, `range`, `roads`, `road`, `gap`); the one-line steps then draw it with the area tool `land` |
| `on` | runs on an earlier scenario's standing build: camera, shot, select, delete, bulldoze, infoEdit, infoRemove, wait, grow; any design step needs `{"do":"edit","build":true}` first. Refused when that build was deleted or never built — so put it before the delete |
| `seed` | design seed (default 1). A type pick starts a fresh design with this seed (as after opening the game) unless the step says `"fresh": false` (the UI's next build of the same type keeps the design) |
| `pace` | `human` (default: the cursor glides to every click, clicks at least 0.4 s apart, sliders dragged, brush strokes at 30 m/s as at 60 Hz), `settled` (no glide, fast strokes), `user` (no waiting for "Planning…": presses on) |
| `buildWhen` | one-liners: `settled` (Build once the ghost is shown: the instant path), `planReady` (Next then Build at once: the build service plans it), `ghostPending` |
| `previewMode` | one-liners: `sketch` sets Review to Sketch before building |
| `note` | free text |

Keys starting with `_` are comments.

## Steps

Every step can add `"shot": "<name>"` (a settled PNG after the step), `"camera": {...}` (the framing of that shot;
without one a build/preview/delete/edit/bulldoze/area step frames the build, oblique), `"shotMode"` and `"zones": true`
(the game's zone grid in the picture, as the road and zoning tools show it).

Panel actions click the panel's real element when it is on the screen (recorded per action in `ui`: `click`), else
fire the panel's own trigger through the UI view (`trigger`), and only when the UI view does not answer call the tool
(`direct`). A greyed-out button is pressed like a person would; when that does nothing the step says so (no trigger
behind its back). Design controls (type, style, options, entrances) live in Design: a script in Review goes back to
Design first, recorded under `implicit`. When an action changed nothing, the step's record and `suite.log` say why;
after a failed `area`, `type`, `preview` or `build` the scenario's remaining steps are skipped.

| step | keys (defaults) | what a person does |
|---|---|---|
| `area` | `tool`: `bucket` (default), `points`/`draw`, `land` (the scenario's land, in its shape), `paint` (smart edges), `paintAnything`, `nodes`; `at` `[dx,dz]`; `points` `[[dx,dz],…]` or `rect` `[w,h]`; `stroke` `[[dx,dz],…]` with `size` (m, default 45 % of the block), `square`, `angle`; `nodes` `[[dx,dz],…]` (default the four junctions); `abs`; `cameraDistance` (320); `open` (points: false; true leaves the outline open) | looks at the block from 320 m, picks the area mode, then: one click (bucket); a click per corner and the first again (points; not the first again with `open`); a brush drag (paint); a click per junction and the first again (nodes). Paint ends with Next |
| `clear` | – | Clear (in Area) |
| `type` | `layer`, `preset` (style), `fresh` (true) | the type tile of the Make row, then the style chip; refuses a type the Make row does not offer; lists the valid ids on a typo |
| `preset` | `layer` (the current type), `preset` | the style chip (Design) |
| `value` | `path` ("0"), `id`, `value` or `json` | a slider is dragged (previewValue each frame, then setValue and endPreview, clamped and snapped to the control's range); a picker hovered then clicked; a toggle or choice set; refused when the panel greys the control out; the value is read back |
| `entrance` | `side` (`south`/`east`/`north`/`west`, `t` 0.5 along it west→east or south→north) or `at`; `path`, `kind`; `crosswalk` (true/false: presses the panel's Crosswalk switch first when it is the other way) | presses "Add an entrance" (or picks the marker kind), clicks the outline at the point, Esc. The tool's own (snapped) marker is recorded (`placedKind`: `pedestrian+crosswalk` with a crosswalk) |
| `entrances` | – | while the button asks for entrances: one on the south street, then east, north, west |
| `moveEntrance` | `index` (0: the script's n-th entrance, matched by position), `side`/`t`/`at` | the entrance's remove button in the panel, then in placement mode a left-click on the new spot, Esc |
| `removeEntrance` | `index` | the entrance's remove button in the panel |
| `rightClick` | `at` `[dx,dz]` (the block's centre), `abs`; `placing` (false; true: an entrance type is pressed first, `path`/`kind` as for `entrance`) | a right-click on the world in whatever the tool is doing (it also presses the game's Cancel, as the real mouse does); records `before` and `after`: stage, area, placing, corners, fills, road nodes, entrances |
| `preview` | – | Next to Review; waits for the plan and the ghost; records what the Review panel lists |
| `previewMode` | `mode`: `sketch`/`ghost` | the preview switch |
| `edit` | `build` (false) | back to Design; with `build: true`: clicks the build (Manage) and Edit |
| `build` | `when` (`settled`; `planReady`; `ghostPending`), `via` (`panel`; `enter`), `after` (`fresh`: the area cleared, the Area step; `keep`: the player's Build & keep area, the area kept and back to Design; `close`: Build & close) | (Next if needed,) OK for permanent changes, Build; waits until it is done and the game has no temporaries left; records the build path (`instant` from the ghost or `start`). No type stays chosen after a build |
| `select` | – | the Manage stage, the game's raycast click on the build |
| `delete` | – | select, Remove, OK; waits until the build is gone |
| `bulldoze` | `target`: `owner` (the owner lot), `part` | the bulldozer's delete definition on that member (applied through the runner's fixture tool, not the bulldozer UI), then the group's own cascade |
| `infoEdit` / `infoRemove` | – | the Placemaker tool closed, the build selected the vanilla way, its info panel's Edit / Remove |
| `infoUngroup` | | the same, its info panel's Ungroup and OK: the build is dissolved for good |
| `camera` | `target`, `view`, `index` (or `"all"`), `yaw`, `pitch`, `distance`, `fov`, `at`, `abs` | frames the camera (below); a target that does not exist is an error and no picture is taken |
| `shot` | `name`, `shotMode`, `labels` (false: street names stay off) | a settled PNG of the current view |
| `terrain` | `name` (`terrain`), `margin` (20 m) | not a camera picture: a map of the ground's height over the outline and the margin, from the game's height data (see `-Modes terrain` above); the step's record has the typical ground level and the lowest and highest ground |
| `wait` | `seconds` (1) or `frames` | a plain wait |
| `grow` | `seconds` (30), `fast` (true) | the simulation with debug fast spawn at speed 4 (`fast: false` = speed 1 with real demand) until the building count is still for 8 s; grows the whole city |
| `undo` / `redo` | `via`: `key` (Ctrl+Z / Ctrl+Y, default), `panel` | says so when there is nothing to undo or redo |
| `key` | `key`: `enter`, `esc`, `undo`, `redo` | the key, through the tool's own key handling |
| `anarchy` | `on` | the panel's anarchy switch (not there in a clean run: the Anarchy mod is not loaded) |
| `group` | `on` | Review's "One group" switch beside Build (off: the build is born ungrouped) |
| `setting` | `name`, `value` | a Placemaker option (e.g. `IgnoreOwnCollisions`), read back |
| `locale` | `locale` (`de-DE`, `ja-JP`, … the game's languages); none: back to the game's own | the game's language, as the Options menu switches it but not saved to the player's settings; end a scenario that changes it with `{"do": "locale"}` (the queue's game is shared) |

World clicks: the point is projected to the screen; off the screen or under the panel the camera pans to it like a
person (recorded as `pan`), else the step says "not clickable". The game's own raycast with the tool's masks decides
what is hit (recorded `hit`, `hitEntity`, `hitOffM`), the cursor glides there, then clicks. Between steps the cursor
is over the panel (no hover on the map).

## The grid

Blocks are 150 m (x) by 140 m (z) between road centre lines, named `B<i>-<j>` (i west→east, j south→north). Vertical
streets (along z, line i): Medium Road (4 lanes) at i = 0, 4, 8…, Pedestrian Street at i = 3, 7, 11…, Small Road
otherwise. Horizontal streets (along x, line j): Medium Road at j = 0, 3, 6…, Small Road otherwise. A block's
streets are its West (line i), East (i+1), South (j) and North (j+1) — recorded per scenario.

```
 j=NZ-1  S S S S S S S S S …    S = slope (6 % up to the north, roads included)
 j=NZ-2  S S S S S S S S S …
 j=NZ-3  R R R f f f f f f …    R = rough (±4 m hills, roads level), columns 0-2
 …       f f f f f f f f f …    f = flat
 j=2     f f f f V f f f f …    V = survive (two plopped parks), column 4
 j=1     Z Z Z Z Z Z Z Z Z …    Z = zoned (low-rise housing on the south street)
 j=0     f f f f f f f f f …
```

Flat blocks are used on one colour of a checkerboard (`spacing: checker`) so no scenario's neighbours hide or shade
it; the grid is made wide enough for the set (summary.json `capacity`, `blocksNeeded`).

## Camera

`target`: `block` (default), `area`/`build` (the outline's bounding box), `entrance` (`index`: the n-th entrance
the script placed), `issue` (the n-th issue the plan marks on the map), `point` (`at`, `abs`), `overview`.
`index: "all"` frames every one, shots named `<shot>-<n>`.

`view`: `oblique` (yaw 25, pitch 60; on `build`/`area` the camera comes as close as it can with the whole outline, and
12 m above it, in the picture; elsewhere 3.4 x the target's radius; `distance` overrides), `top` (yaw 0, pitch 89.5, field of view 18°:
nearly orthographic), `closeup` (yaw 30, pitch 38, 45-60 m), `low` (pitch 20). The camera sits south of its target
and looks north; the noon sun is behind it. The game lifts the eye over terrain and clamps tilt: the rendered pitch
and distance are read back and the request corrected (up to 3 passes).

To stand inside a glade or look along a bank (nature makeover): `"eye": [x, z]` (block-relative, or absolute with
`abs`) and `"height"` (m above the ground there, default 1.7) put the eye there looking at the target; yaw, pitch and
distance follow. `"clearView": true` turns the camera round its target (in 15° steps) to the direction whose line of
sight passes the fewest trees and bushes of the last built plan; with `"height"` and `"distance"` it is a low eye that
far away. Without these keys every camera step behaves as before.

## Shots

* `shotMode`: `render` (the clean scene: no outline, no icons, no street names), `overlay` (with Placemaker's outline,
  ghost symbols and markers; the default while the tool is open outside Manage), `ui` (the real screen with the
  panel). All modes have the window's size (1600x900), so pairs line up pixel for pixel.
* A picture waits until the scene is still: a short floor after the last event (camera move, ghost, build, delete,
  grow), the engine quiet (camera still, no meshes or textures loading, full level of detail, nothing fading), then
  two pairs of captures that barely differ. A scene that is still moving after 10 s is taken anyway and its caption
  says so.
* Names: `shots/<NN>-<id>.<step>-<name>.png` (NN = the scenario's number in this run); never overwritten.

## Look (readability mode; `-Look game` turns it off)

Noon (12:00 local solar time) on day 172 at latitude 45° N (the Plains map is at 44° S, where June is midwinter):
the sun about 68° up, due south. A fixed exposure calibrated at the start of the run, indirect light x1.3, wind 0. No
rain, clouds, fog, aurora or snow. Notification icons off (tool-error icons stay), street names off, full level of
detail without LOD cross-fade, no edge scrolling. The fixture's own growth is low-rise housing; tall buildings in a
shot come from the types being built. Everything is recorded in summary.json `look`.

## Results

`runs/<runId>/`: `logs/`, `suite.log`, `summary.md`, `summary.json`, `contact.html` (and `contact.jpg`), `shots/`,
`scenarios/NN-<id>.json` (each step with its waits, frame times, presses, UI path), `audit/`, `host.log`,
`catalog.json` (also copied to `suite/catalog.json`), `dirty.patch` when the tree was dirty. The run is labelled with
the git hash and the deployed DLL's hash; the game checks it loaded that DLL ("wrong build" stops the batch).

If the game is closed early the quit hook writes what is known, marked incomplete; the host says how it ended
(`quit`, `closed-externally`, `crashed`, `killed`, `hung`, `boot-timeout`) at the top of summary.md.

## Save and reload

What a player sees after quitting and loading their city again needs a second game: a game keeps every runtime prefab
it made in memory, so a load in the same game finds them even when a fresh game would not (GitHub issue #2: white boxes
where a save named a prefab the new game did not have yet). Two files, two games:

```
powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/decal-reload-save.json -Who <you>
powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/decal-reload-load.json -Fresh -Who <you>
```

* `{"do":"lab","op":"saveCity","name":"..."}` saves the city in the player's saves; `{"do":"lab","op":"loadCity",
  "name":"...","delete":true}` loads it (and deletes it after). The load's record says how many objects stand on a
  missing prefab (`objectsOnMissingPrefabs`; the game draws those as its white missing-object box) and what the
  old-decal clean-up removed. After a load only camera and shot steps make sense (frame with `"target":"point"`,
  `"at":[x,z]`, `"abs":true`: the first file's `atAbs`).
* `-Fresh` (Send-Suite) asks for a game launched for that file: never one that has already run other requests.
* `{"do":"lab","op":"oldDecal","decal":"...","tier":25}` places an object the way builds up to 0.1.1 left decorative
  decals (a runtime copy named `Placemaker Decal <tier> - <decal>`); `{"do":"lab","op":"oldDecalCleanup","on":false}`
  turns the clean-up after loads off for the "before" picture.

## Live queue

`Run-Suite -Direct -Queue` keeps the game open (after the set, or right after the fixture with `-Set queue`); the game queue's
dispatcher runs its games this way. Send-Suite (or the dispatcher) drops a file into `<run>/inbox`; the result comes
back as `<file>.result.json`. `{"command":"grow","seconds":40}` grows the whole city. A file's own `settings` hold for
that file only. A scenario with `land` gets its land laid out when it arrives (east of the last plot; the ground is
written only beyond what is already laid out, so standing builds keep theirs); `no-block` when the map has no room
left. A queue-only session grows its housing when the first `zoned` scenario arrives. The session ends with `-Quit`,
after 15 idle minutes, or at the host timeout (8 h).
