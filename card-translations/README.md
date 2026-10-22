# Traductions des cartes (mod « TCG Card Translations »)

Un dossier par langue (`FR`, `DE`…), choisi selon la langue du jeu (réglage
`General.Language` : `auto` ou un nom de dossier). Dans la langue, chaque dossier
d'édition reprend le chemin de l'édition dans le pack, sans le suffixe
`_prefabloader` et sans le dossier du pack lui-même :

```text
FR/Pokemon/Cards/01 - Base/01 - Base Set/
  names.json     { "49": "Soporifik", ... }   (noms.json est aussi lu)
  049.png        (ou 49_BS_Drowzee.png)
```

- Une image est associée à la carte qui porte le même nom dans le pack, ou au
  même numéro (`049.png` → `49_BS_Drowzee`).
- `names.json` donne le nom affiché dans le classeur et les menus. Les clés qui
  commencent par `_` sont des commentaires.
- Une carte sans image ni nom reste en anglais.

Éditions présentes : Base Set, Jungle, Fossil, Team Rocket et leurs 1st Edition,
Base Set 2, Wotc Promos (29 cartes) et Gym Heroes.

## Noms des éditions, produits et boutiques

Les textes affichés dans les menus (nom de l'édition, boosters, displays,
décorations, boutiques du téléphone) se traduisent dans un fichier
`texts.json` (ou `textes.json`), n'importe où dans le dossier de la langue (ici `FR/Pokemon/texts.json`) :

```json
{ "Base Set": "Set de Base", "Base Set Booster Pack": "Booster Set de Base" }
```

La clé est le texte anglais exact du pack. Le mod remplace ces textes au moment
où Enhanced Prefab Loader lit le JSON du pack, comme si le JSON avait été
traduit, sans modifier le fichier. Après une partie, `detected-texts.txt`
(dossier de la langue) liste tous les textes des packs ; ceux qui restent à traduire
y sont écrits au format de `texts.json`, prêts à copier.

## Images en cache

Les images déjà préparées sont gardées dans `BepInEx\cache\TCGCardTranslations`
(réglage `Images.DiskCache`) : la première apparition d'une carte reste un peu plus
lente, les suivantes, même après redémarrage, sont quasi instantanées. Le cache
se refait seul quand une image change.

Les noms sont versionnés ici. Les images, volumineuses, sont copiées à la
compilation depuis les dossiers indiqués dans `images.json`
(`tools\build-card-translations.ps1`, appelé par `Compiler-et-tester.cmd`).
Le résultat complet est `artifacts\card-translations\TCGCardTranslations`, que
le lanceur de test recopie dans `BepInEx\plugins\TCGCardTranslations`.

Au lancement du jeu, le mod écrit `detected-cards.txt` dans le dossier de la langue :
toutes les cartes chargées par Enhanced Prefab Loader, leur nom exact, et ce
qui est traduit.

## Publication

Le paquet publié (Thunderstore, Nexus) contient la DLL et les fichiers `names.json`
et `texts.json` du dossier `FR`, sans les images. Voir `docs/publication.md`.
