# Simplified Chinese localization maintenance

This optional tool rebuilds translations against the **current English source**. It needs Windows PowerShell 5.1 or PowerShell 7 and uses no network services. Keep `Update-Localization.ps1` encoded as UTF-8 **with BOM** for Windows PowerShell 5.1.

## Maintainer workflow

From the repository root, containing `Lang/en-US*.json`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\localization\zh-HANS\Update-Localization.ps1 -Action Build -SourcePath .
```

`-SourcePath` accepts either the repository root or its `Lang` directory. Only `Build` and `Check` are supported in this mode. They do not modify the input English files or existing files in the source `Lang` directory; they do write generated output, reports and imported user translations inside the tool directory. No DLL is required. Both commands produce reviewable files:

- `Payload/Native/Lang/zh-HANS.*.json`: one file per English source, plus `zh-HANS.ui-extras.json` when source-checked UI additions are present.
- `Reports/Latest.json`: coverage, added/changed/removed keys, ambiguity counts, duplicate-key conflicts, and input/output file hashes. Source reports contain filenames rather than machine-specific absolute paths.
- `Reports/PendingTranslations.json`: untranslated key/English pairs with empty `zh`, a reason and the previous translation when available.
- `Reports/SupplementalReview.json`: UI additions whose source reference, key marker or English fallback no longer matches.

Review the generated files, copy the intended native language files into the repository's `Lang` directory, and commit them through the project's normal review process. Unknown translations are **omitted**, so an incomplete export must not be presented as complete. Deleted source keys and deleted source files are removed from the generated native output; old translations remain in the data files for future reference. Cross-file duplicate keys are resolved independently using each file's English value.

Fill `zh` in `Reports/PendingTranslations.json` and run the same command again. Completed rows are imported automatically into `Data/UserTranslations.json` before the pending report is regenerated. Alternatively use `-ImportFile .\reviewed-translations.json`. Imports whose English text is no longer current are retained but are not applied to that changed/removed key. Keep the whole maintenance directory when moving to a new mod version, especially your `Data/UserTranslations.json`.

## Matching and validation

The priority order is:

1. User override matching both `key` and `en` exactly.
2. `Data/KeyTranslations.json` matching both `key` and `en` exactly.
3. `Data/Translations.json` matching the English value exactly, **only if no different known translations exist for that English value**.

For example, `Stands` can mean sports seating or food stalls. Existing context-specific keys keep their respective translations; a new ambiguous key is sent for review rather than inheriting an arbitrary translation. Comparisons are ordinal and case sensitive. `KeyTranslations.json` and user overrides may retain multiple English revisions for one key. Contradictory bundled translations for the same key/English pair are rejected.

Brace placeholders and markup tags are checked as exact multisets, including repeated tokens. A changed English source must match a reviewed entry or is exported for translation. Source checks for supplementary UI entries are conservative string-presence checks, not a parser or a guarantee that a future UI still uses the same fallback. They compare `reference` in source mode and `runtimeReference` (if supplied) in installed-mod mode. Review supplementary entries after upstream UI changes.

`Data/Baseline.json` records the reference English version used for added/changed/removed-key reporting. `Data/KeyTranslations.json` contains reviewed contextual translations; `Data/Translations.json` is the optional unambiguous English memory. Update the baseline and reviewed data intentionally after reviewing a new version. No machine translation is performed automatically.

## Player workflow

Exit the game, then double-click `1-Install-or-Update.cmd`. The tool selects the largest numeric `163638_*` cached mod directory. This is a cache heuristic, not a live query of the game's enabled version. From the tool directory, specify the actual directory when needed:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Update-Localization.ps1 -Action Install -ModPath "D:\path\to\163638_3"
```

Players receive one additional `Lang/zh-HANS.zz-placemaker-zhcn.json`; the DLL and other mod files are untouched. Existing bytes at that exact filename are backed up in `.placemaker-zhcn-install.json`. Repeated installation preserves the original backup. SHA-256 checks refuse to overwrite or uninstall a subsequently edited patch; pending transaction records permit recovery after an interrupted replacement or restoration.

Use `2-Check-Translations.cmd` to build and inspect without changing the mod; it still writes local payload/reports and imports completed pending translations. Use `3-Uninstall.cmd` to restore the original file or remove the added file. Keep the installation record until uninstall completes. Game saves are never accessed. Keep the package outside the mod cache so a mod update does not remove your translation memory.

## Regression tests

From the tool directory, run `python tests/test_updater.py`. The suite uses disposable temporary directories and fake DLL bytes that are never loaded. It does not read or change the real game/mod installation. The default test shell is Windows PowerShell 5.1; set `PLACEMAKER_TEST_POWERSHELL` to a PowerShell 7 executable to test that runtime.

The suite covers install/update/uninstall and backup recovery; modified-file protection; source exports; duplicate source keys; exact contextual reuse; ambiguous wording; stale imports; removed keys/files; placeholders; UI source guards; and PowerShell dictionary-key edge cases. In-game rendering remains a separate manual check.
