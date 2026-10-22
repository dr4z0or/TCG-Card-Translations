# TCG Card Translations

**Cards, editions and products of Enhanced Prefab Loader packs in your language.** Shows translated card names (binder, menus), and optionally translated card images, edition names, product names, decorations and phone shops — **without modifying the packs**. Works with any EPL pack (Pokémon, Yu-Gi-Oh!…).

Included: **French names** of the Pokémon cards of Base Set, Jungle, Fossil, Team Rocket (and their 1st Edition), Base Set 2, Gym Heroes and part of the Wizards promos, plus the French names of the related editions and products. Card images are **not** included.

*Français plus bas.*

## How it works

The mod folder holds one folder per language, picked from the **game's language**:

```
TCGCardTranslations/
  TCG.CardTranslations.dll
  FR/
    Pokemon/texts.json                                   edition and product names
    Pokemon/Cards/01 - Base/01 - Base Set/names.json     { "49": "Soporifik", ... }
    Pokemon/Cards/01 - Base/01 - Base Set/049.png        optional translated image
```

- An **edition folder** repeats the edition's path inside the pack, without the pack folder itself and without the `_prefabloader` suffix. A short name (`01 - Base Set`) also works when it is unique.
- **names.json** gives the displayed name of each card, by card number (`"49"`) or exact card name (`"49_BS_Drowzee"`). Keys starting with `_` are comments.
- **Images** are named like the card (`49_BS_Drowzee.png`) or by number (`049.png`). PNG or JPG.
- **texts.json** (anywhere in the language folder) translates the texts of the pack's JSON shown in menus: `{ "Base Set": "Set de Base", "Base Set Booster Pack": "Booster Set de Base" }`. The key is the exact English text.
- A card or text without a translation keeps its original version.

## Making a translation

1. Create a folder named after your language code in the mod folder: `DE`, `ES`, `IT`, `PT-BR`…
2. Start the game and open your packs once. The mod writes in that folder:
   - **detected-cards.txt** — every card loaded by EPL, by edition, with the folder to create and what is already translated;
   - **detected-texts.txt** — every edition, product, decoration and shop name, with the lines still to translate already written in the `texts.json` format.
3. Add your `names.json`, `texts.json` and images, then restart the game.

## Settings

`BepInEx/config/local.tcg.cardtranslations.cfg`:

| Setting | Default | |
|---|---|---|
| `General.Enabled` | `true` | |
| `General.Language` | `auto` | `auto` = game language, or a folder name such as `FR` (to use French cards with the game in English). Restart after a change. |
| `Images.ImagesInMemory` | `200` | Translated images kept in memory (~0.6 MB each). |
| `Images.CompressImages` | `true` | 4–6 times less memory, very slight loss of sharpness. |
| `Images.DiskCache` | `true` | Prepared images kept in `BepInEx/cache/TCGCardTranslations` for much faster loading. |

## Installation

**r2modman / Thunderstore Mod Manager:** install *TCG Card Translations* (Enhanced Prefab Loader is installed with it).

**Manual:** extract the archive's `BepInEx` folder into the game folder. Requires Enhanced Prefab Loader and the packs to translate.

## Notes

- With a mod manager, the mod folder is replaced on update: keep a copy of your own translation folders.
- Card names and images belong to their respective owners. This mod ships only text translations.

---

## Français

Noms des cartes, des éditions et des produits des packs Enhanced Prefab Loader en français (dossier `FR`, choisi selon la langue du jeu), sans modifier les packs. Inclus : les noms français des cartes Pokémon du Set de Base, Jungle, Fossile, Team Rocket (et Édition 1), Set de Base 2, Gym Heroes et d'une partie des promos Wizards, ainsi que les noms des éditions et produits. Les images ne sont pas fournies : déposez vos images (`049.png` ou nom exact de la carte) dans le dossier de l'édition. `detected-cards.txt` et `detected-texts.txt` (dans `FR`) listent tout ce qui reste à traduire.
