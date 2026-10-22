# TCG Card Translations

A BepInEx mod for **TCG Card Shop Simulator**. Interface in English, or in French when the game is in French.

**Cards, editions and products of Enhanced Prefab Loader packs in your language.** Shows translated card names (binder, menus), and optionally translated card images, edition names, product names, decorations and phone shops — **without modifying the packs**. Works with any EPL pack (Pokémon, Yu-Gi-Oh!…).

Full description, settings and FAQ: [mod page](publish/TCG_Card_Translations/README.md) · changes: [CHANGELOG](publish/TCG_Card_Translations/CHANGELOG.md).

## Install

**Download:** [Nexus Mods](https://www.nexusmods.com/tcgcardshopsimulator/mods/1930) (Thunderstore / r2modman: coming soon). Requires BepInEx 5 and Enhanced Prefab Loader.

## Building from source (Windows, .NET SDK 8)

1. Copy `local.props.example` to `local.props` and set `GameDir` to your game folder (BepInEx 5 installed).
2. `dotnet build src/TCG.CardTranslations -c Release`.
3. Offline tests: `dotnet run --project src/TCG.CardTranslations.Tests`.

Install: `TCG.CardTranslations.dll` in `BepInEx/plugins/TCGCardTranslations`, with the language folders of `card-translations/` (`FR/…`) copied next to it. Card images are not part of this repository.

## Other mods by dr4z0or

- [TCG Save Manager](https://github.com/dr4z0or/TCG-Save-Manager)
- [TCG Custom Game Modes](https://github.com/dr4z0or/TCG-Custom-Game-Modes)
- [TCG Mod Profiles](https://github.com/dr4z0or/TCG-Mod-Profiles)
- [Mods Translator](https://github.com/dr4z0or/Mods-Translator)

## License

[MIT](LICENSE). Not affiliated with OPNeon Games.

---

**En français :** Noms des cartes, éditions et produits des packs Enhanced Prefab Loader dans ta langue, sans modifier les packs. Noms français inclus pour les premières séries Pokémon (sans images). Les joueurs le téléchargent sur [Nexus Mods](https://www.nexusmods.com/tcgcardshopsimulator/mods/1930) (bientôt sur Thunderstore) ; ce dépôt contient le code source.
