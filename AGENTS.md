# Placemaker: start here

Placemaker is a Cities: Skylines II (1.6.2f1) code mod. The player selects an area, picks a type and a preset (style),
previews it and builds it: roads, parking lots, parks, shops. It merges ParkingLotTool (KruemelGames), ParkManager
(tomkpunkt) and Grid Road Generator (Ti4goc), so it is GPL-3.0 and credits all three.

- `Core/`: the pure planner (.NET + Unity.Mathematics only), tested by `Tests/Placemaker.Tests` (xUnit, net9).
- `Game/`: the ECS game layer (tool, preview, build pipeline in `Game/Build/`).
- `UI/`: the React panel; `UI/src/contract.ts` is authoritative.
- `Lang/`: one strings file per area and language (English and the game's 11 others).
- `Recipes/`: the shipped types.

## Every text in every language

Placemaker ships in English and the game's 11 other languages (de-DE, es-ES, fr-FR, it-IT, ja-JP, ko-KR, pl-PL, pt-BR,
ru-RU, zh-HANS, zh-HANT). **Anything you add or change that the player reads (a type, a style, a control, a tooltip, a
button, a message) is translated into all 11 in the same change.** It is not done until `node tools/lang/check.js`
reports every language at 100 % with 0 changed and 0 broken; then `node tools/lang/check.js --stamp <locale>` for each.

- `node tools/lang/check.js --todo <locale> <area>` lists what to translate; use the game's own words
  (`node tools/lang/glossary.js`, then `.forensics/lang/glossary/<locale>.json`) and keep labels as short as the English.
- Panel text: also run `PM_ALL=1 node mock/run.js --test tests/i18n.spec.ts` (from `UI/`): nothing may be cut that
  is whole in English.
- How it works: `docs/LOCALIZATION.md`.

## Direction: tall and perfect, not wide

The focus, in order, is **Roads, Parking, Parks, Shops & squares**. Make those excellent before anything else.

**The shelf** (`Recipes/shelf/<category>.json`, read by `Core/Tool/Shelf.cs`) decides the Make row: one file per
category (Roads, Parking, Parks, Beach, Shops & squares, Nature, Farms, Sports, Zoning, Industrial (docs/refactor/industrial.md); Transport hidden), its order,
the type groups it shows (Gardens fold into Parks, the old Water group is Beach, Squares and Events fold into Shops), types
moved in from another group (`types`), and what it hides: `hiddenTypes` and `hiddenStyles` per type. To cut a type or
a style, list it there: never delete code. The panel, its search, the game runner's catalog and `Show.ps1` all read it;
hidden things are flagged and can still be built by id (`Show.ps1 -Type quarry`). A category agent edits only its own
file.

## Read first

- `docs/ARCHITECTURE.md`, `docs/DESIGN.md`: how it is built and how it should feel.
- `docs/RESEARCH-NOTES.md`, `docs/RESEARCH-NOTES-ADDENDUM.md`: game engine rules (decompiled). A mandatory checklist.
- `docs/refactor/GRADES.md`: the user's grades per category and the current refactor brief.
- `docs/AREA-TOOLS.md`: the area tools (Points, Paint bucket, Paint area) after the area overhaul: one set of stops,
  the fill engine (gaps, reach, exact road surfaces), how to test them (`suite/scenarios/area-tools.json`).
- `suite/README.md`: the game runner (below).
- `docs/teardown/`: what went wrong and why. Start with `00-user-feedback-1215.md`, then `07` (shop defects), `08`
  (street zoning), `09` (switching freezes).
- `.forensics/suite/scrutiny/STATUS.md` (git-ignored, local): the state of the game runner and its known gaps.

## The game runner: required for every change

Nothing is done until you have seen it in the game. After any change that affects what gets built (the planner, the
build pipeline, a type, a style, the panel), run `tools/suite/Show.ps1` on the types it touches, and on the other
types that share the same mechanism. Read that build's lines in `Placemaker.log` (Show prints them) and look at its
three pictures: the plan, the built result and the terrain map. Only then tell the user anything. For a bug, run it
before the fix too, so you can compare.

Use the land options to reproduce the case: any shape and size, a slope, hills or mountains, your own heights, open
land for roads (`suite/README.md`, "Land"). The runner is deliberately simple: it launches the real game, does the
steps, takes the pictures in order and collects the logs. It judges nothing (no PASS/FAIL); **you read the logs**.
Ask for more views or modes only when the three pictures did not show what you needed.

```
powershell -File tools/suite/Show.ps1 -Category parks -Type park -Style formal   # 3 pictures (plan, built, terrain) + log lines
powershell -File tools/suite/Show.ps1 -Type roads -Size 900x700 -Ground mountains -Roads none   # land made to order
powershell -File tools/suite/Show.ps1 -Category parking -Type parking -Style all -Views top -Modes clean,zoning
powershell -File tools/suite/Play.ps1                     # the user's playground: flat grid, camera on it, they play
powershell -File tools/suite/Run-Suite.ps1 -Set look-one  # one type and preset, one 45° picture, quit
powershell -File tools/suite/Run-Suite.ps1 -Set look      # every preset of a type, one 45° picture each
powershell -File tools/suite/Send-Suite.ps1 -File x.json  # a scenario file in the open game, with your current build
powershell -File tools/suite/Queue.ps1                    # who is in line, what runs now, ETAs
```

### The game queue: always go through it

There is one game for every agent. Each of these commands waits its turn in one machine-wide line, with a frozen copy
of your build; requests with the same build share one open game, and the game is relaunched only to switch builds.

- **Run the command as a background command.** It exits when your results are back (same output and exit code as
  before), which wakes you up. Do the next thing meanwhile; don't poll, retry or start a second copy. Start it from
  your own shell and keep that shell: a command whose parent process is gone leaves the line (exit 2).
- **Name yourself with `-Who <name>`** (your task, e.g. `-Who fix-parking-fence`). Without it you show up as your
  branch, and every agent in the same worktree looks the same in the line.
- Read its progress lines: `queue: #2 in line | ahead: lead (3 build(s), 9 pictures, relaunch) | now: ... | ETA about
  2 min 10 s`. `#N` is your place, `ahead` who goes first, the ETA is measured per build and per picture, per type
  (ask for fewer pictures and you are done sooner). `waiting: the user's ... game is open`, `the user is starting the
  game`, or `the queue waits 2 min before launching`: the user comes first; that can take long, and that is fine.
  `STUCK` (exit 1): the queue itself is broken (no dispatcher could run for 3 min); report it with the lines it
  printed, don't retry in a loop.
- **Your branch must contain the queue** (master from `932753b` on). A branch made before it has the old commands,
  which launch the game themselves past the line: merge master first. The queue also runs only what your worktree has
  **committed** of its own scripts (`Game-Dispatcher`, `GameQueue`, `ModsInstall`, `Run-Suite`, `Contact-Suite`);
  your build (`binRelease`, `UIuild`) is taken as it is, committed or not.
- Ctrl+C leaves the line. `-Direct` bypasses the queue: for debugging the runner only. `tools/suite/Queue.ps1` shows
  the line and what `Mods\Placemaker` holds.
- The queue does not install your build for the user's own game: `Mods\Placemaker` gets the user's build back the
  moment the queue's game exits. Say so when you report a fix (below, "Build and test").
- Details: `suite/README.md`, "The game queue".

- Results go to `C:\Users\jack\cs2-area-tool\.forensics\suite\runs\<id>\` (the main checkout's, also from a worktree;
  the commands print full paths): `logs/Placemaker.log` and `logs/Player.log` (your request's part), `suite.log`,
  `summary.md`, `contact.html` and `shots/`.
- One game at a time. Never close a game the user started.
- A code change needs a rebuild and a relaunch (about 60 s to a ready test world).
- After a cut-off run, `Run-Suite.ps1 -RestoreOnly` puts the user's game settings back (the queue does it by itself).
- A new worktree: `git -C C:Usersjackcs2-area-tool worktree add C:Usersjackpm-wt<name> -b <name> master`,
  then link the shared UI deps: `cmd /c mklink /J C:Usersjackpm-wt<name>UI
ode_modules
  C:Usersjackcs2-area-toolUI
ode_modules`.
- Removing a worktree: its `UI\node_modules` is a link to the shared folder. Delete the link first
  (`cmd /c rmdir <worktree>\UI\node_modules`), then `git worktree remove`; a recursive delete empties the shared
  folder and breaks every worktree's UI build.

## Build and test

- `dotnet build Placemaker.csproj -c Release -p:Deploy=false`: **the build** (UI included). It installs nothing; the
  game runner takes your build from `bin\Release` (the queue's commands build it for you anyway).
- `-p:Deploy=false -p:SkipUI=true`: a compile check only.
- Installing into the user's game (`dotnet build Placemaker.csproj -c Release`, without `-p:Deploy=false`, which
  installs into `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\Placemaker` and makes it the
  user's build) is the lead's job, from master, when the user asks or has closed the game. It fails, and touches
  nothing, while any game is open or while the game queue's game owns the folder (`tools/suite/Deploy-Mod.ps1`). The
  user's previous build is kept in `%USERPROFILE%\.placemaker\game-queue\user-build.prev`.
- `dotnet test Tests/Placemaker.Tests --filter ...`: the full run takes 20+ minutes, so run what you touch.
- When the user plays, the logs are in `...\Cities Skylines II\Logs\Placemaker.log` and the build audits in
  `...\ModsData\Placemaker\audit\`.
- Decompiled game source: `C:\Users\jack\AppData\Local\Temp\pm-decomp\game`. It is a temp folder; `ilspycmd` is
  installed to regenerate it from `Cities2_Data\Managed\Game.dll`.
- There is no `python` on this machine; use node for scripting.

## Fixed in the core refactor (rf-core; seen in the game runner, see its merge commit)

- Trees, bushes and lamps not drawn: the parking planner's lot area became a vanilla "Agriculture Lot - Field"
  (CanOverrideObjects hid everything inside it). `area.lot` is never automatic, an unchosen lot area is never built,
  and no vanilla extractor/storage lot is built outside farm and industry types. The un-hide of our own overlapping
  pieces also restores their render search-tree mask.
- Farm fields and their fences across entrances: same cause, gone.
- The dip at lot entrances (4–7 m, strip mall, mall district): the game re-fits marker nets (invisible paths) to the
  ground and the lane junction clips the ground to them. Our marker edges and nodes get `Game.Net.Elevation` 0 on the
  Apply frame (`BuildService.Pits.cs`). With the option "Debug: build checks" (off: it froze frames; a scenario turns it on with a `setting` step) every build logs a live pit check over its whole outline ("pit check"). Every build logs "Placemaker build timing" (its main-thread time per step and the worst frames).
- Shops facing the city street: our zoning roads switch zoning off on sides no plot faces (`Core/Build/LaneSides.cs`,
  vanilla `ZonesDisabled`), kerb strips block the city streets' first zone row along the build before the zoning
  refresh (`ZoneGuard.KerbStrips`), the refresh never moves a lane plot onto a city street, and street-facing plots are
  given back to the site unless "Shops facing the streets" is on (on by default only for shops park and retail park).
  Plazas and market squares have no lanes: their shops still face the streets by design.
- Catalog rescans from our own prefabs (switching freezes): `AssetCatalogSystem.AddOwnPrefab` counts them; they never
  trigger a rescan and are not catalogue items.

## Known mod bugs (found by the game runner, not fixed yet)

- Build freezes of 1.4–2.7 s (the build itself; report 09's other items: ghost re-emission, plan landing work).
- The decal-copy rescan fix is proven with park companions in the clean profile (no rescan after them); decal copies
  themselves only appear with the user's asset packs (`-AllMods`), not run.
- Plazas and market squares: their shops face the city streets (these types have no lanes); for the Shops & squares
  agent to decide.
- The roads presets strict-grid and superblocks build nothing.
- The game crashes (native, `TerrainSystem.CullForCascades`) on the 4th build of `Show.ps1 -Category parks -Type park
  -Style all -Views 45,top -Modes clean,zoning,terrain` (park-garden, right after its terraform and terrain paint);
  park-garden alone builds fine (runs `queue-1016-003552`, `queue-1016-004950`).

## How the user works

- They are not a programmer. Use plain language and short answers.
- Never tell the user something is fixed in their game unless you saw it work in the game (`Show.ps1`) **and** it is
  installed. You don't install (the lead does, above), so say it plainly: "fixed on branch X and seen in the game
  runner; not in your game yet". Compare the time on `Mods\Placemaker\Placemaker.dll` with the commit (the game queue
  does not leave your build installed).
- Fix things everywhere they occur: every type and code path that shares the mechanism, not just the one in the
  screenshot.
- Helper agents use the strongest model. Commit when a piece of work is done; the user tests in game.
