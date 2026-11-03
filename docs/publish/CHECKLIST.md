# Putting Placemaker on Paradox Mods

**What it means.** Paradox Mods is the game's official mod store: the same list players see in the game's Paradox
Mods menu. Uploading sends a copy of the mod, its page text (`MOD-PAGE.md`) and its pictures to Paradox under your
Paradox account. Anyone can then subscribe, and the game downloads it for them. Later fixes go up as new versions,
which players get automatically.

**How.** Code mods have no upload button on the website. The game's modding kit includes an uploader
(`ModPublisher.exe`) that runs on this computer and uses the Paradox login you last used in the game. 
`tools/publish/Publish.ps1` builds a clean copy of the mod and runs it with `Properties/PublishConfiguration.xml`:

```
powershell -File tools/publish/Publish.ps1 -StageOnly              # build and show what would go up
powershell -File tools/publish/Publish.ps1                         # the first upload (writes the mod's id back)
powershell -File tools/publish/Publish.ps1 -Command NewVersion     # new files: raise ModVersion, write ChangeLog
powershell -File tools/publish/Publish.ps1 -Command Update         # the page only: text, pictures, AccessLevel
```

## Ready

- Page text, thumbnail (four shots from the showcase recordings, the logo and name) and 8 screenshots (stills from showcase v6).
- `AccessLevel` = Private: only you can see it.

## The private test

1. Uploaded 2026-10-17 as Private: mod id **163638** (build 0.1.0+51b39d1, master's mod code).
   0.1.1 uploaded 2026-10-17 09:25 (build 0.1.1+bc58941 = master with the area tools work).
2. Remove the local copy (`Mods\Placemaker`) so the game doesn't load two, subscribe to the private mod in your game,
   and check it works.

## Before going Public

1. **Credits wording**: decided 2026-10-17: "Based on similar concepts to ...". The code files keep their own "ported from" notes (GPL-3.0).
2. **Your co-author's OK.** Grid Road Generator code is in Placemaker, and that mod has no licence yet. The code
   comments disagree too (`Core/Tool/RoadGraph.cs` says MIT, `BuildService.Terrain.cs` says GPL-3.0); settle it.
3. **Public source code** (the GPL-3.0 requires it): the code is on GitHub at https://github.com/jackwrichards/placemaker, **public** since 2026-10-17, linked on the page (`<ExternalLink Type="github">` and in the credits line).
   `Publish.ps1` refuses Public or Unlisted without it.
4. **The video**: Paradox Mods takes pictures only. Put the showcase on YouTube and add it as
   `<ExternalLink Type="youtube">` (and a line on the page).
5. **Where to report bugs**: a GitHub issues page or a forum thread (`ForumLink`), named on the page.
6. **Check the page's claims in the game**: undo after a build, and what happens to built places when the mod is
   removed (put it on the page if they vanish).
7. Set `AccessLevel` to Public and run `Publish.ps1 -Command Update`.

## Rules worth knowing

- Mods must be free (donation links are allowed) and contain only things you may share (Paradox user agreement, §5).
- No review before it goes live, but reported mods are checked, and Paradox can remove them.
- A name stays reserved even after the mod is deleted.
- Every new version needs a higher `ModVersion` and a `ChangeLog`; the uploader refuses the same version twice.
- At most 10 screenshots (Paradox refuses more; `Publish.ps1` checks).
