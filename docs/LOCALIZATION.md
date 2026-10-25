# Languages

Placemaker ships in the game's own 11 languages besides English: de-DE, es-ES, fr-FR, it-IT, ja-JP, ko-KR, pl-PL, pt-BR,
ru-RU, zh-HANS, zh-HANT (first translated 25 Oct 2026, one agent per language).

- `Lang/<locale>.<area>.json` mirrors `Lang/en-US.<area>.json` key for key. The game loads English first and the
  language's files over it (`Game/Localization/LocalizationLoader.cs`), so an untranslated text shows in English.
- The game's own words per language (Save, Upkeep, Residential…): `node tools/lang/glossary.js` reads them from the
  game's `Locale.cok` into `.forensics/lang/glossary/<locale>.json` (git-ignored: the game's text). Translations use them.

## When the English changes

Every English edit leaves the other languages behind. `node tools/lang/check.js` shows, per language, what is missing,
what changed in English since it was translated (`tools/lang/state/<locale>.json` keeps a hash of the English each text
was made from) and what is broken (a `{0}` or a line break lost). Then:

- `node tools/lang/check.js --todo <locale> <area>`: the texts to translate, `{key: English}`.
- After translating: `node tools/lang/check.js --stamp <locale>`.

## Does it fit

The panel is narrow. `PM_ALL=1 node UI/mock/run.js --test tests/i18n.spec.ts` (from `UI/`) opens the mock panel in every
language (`?lang=de-DE`) on every type with every group open and lists the texts that end in "…" or spill out, against
English (`UI/mock/screenshots/i18n-<locale>-cut.json`, with pictures). In the game: `suite/scenarios/languages.json` (the
runner's `locale` step switches the game's language for the run, not saved to the player's settings).
