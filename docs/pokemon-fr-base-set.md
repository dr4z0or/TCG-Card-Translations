# Prototype visuel français — Pokémon Base Set

Ce prototype remplace uniquement les faces des 102 cartes de **Base Set** par
les scans français indexés par TCGdex. Avant l'import, chaque scan natif
`600 × 825` suit la même restauration que les textures anglaises :
`realesrgan-x4plus-anime` en ×4, puis réduction Lanczos vers `820 × 1024`.
Le tirage est ensuite uniformisé sur la variante normale, sans logo
« Édition 1 ». La géométrie à retirer est déduite de la différence entre les
deux bundles anglais ; le remplissage emploie exclusivement les pixels
français voisins. Il conserve le texte français, les identifiants, l'ordre des
cartes, les noms de sprites, les raretés, les masques holographiques et le
descripteur JSON du pack original.

Le bundle source de `lab/instance-005` n'est jamais modifié. La construction
refuse toute version source dont les empreintes ne correspondent pas au pack
Pokémon 3.6 actuellement audité.

## Construction

Depuis la racine du projet :

```powershell
.\tools\build-pokemon-fr-base-set.ps1
```

Les images téléchargées sont conservées dans
`lab/pokemon-fr-base-set/cache`. Les rendus restaurés et leur manifeste de
cache sont conservés dans `lab/pokemon-fr-base-set/uniform-cache`. Les rendus
après normalisation du tirage sont placés dans
`lab/pokemon-fr-base-set/normalized-cache`. Une reconstruction sans réseau est
ensuite possible avec `-Offline`. Utiliser `-Force` pour reconstruire un
artifact déjà présent.

Le constructeur vérifie les empreintes de l'exécutable portable Real-ESRGAN et
du modèle avant toute restauration. Le canal alpha original est traité
séparément puis remis en place après réduction afin de préserver les coins.

La sortie locale est créée sous
`artifacts/pokemon-fr-base-set-0.3.0`. Elle contient le bundle à son chemin
BepInEx final, un manifeste détaillé et `SHA256.txt`. Huit cartes décodées du
bundle reconstruit sont exportées sous `lab/pokemon-fr-base-set/preview-0.3.0`
pour le contrôle visuel, dont Soporifik, Lippoutou, une carte Dresseur et deux
Énergies.

Le constructeur réencode chaque image au format BC7 d'origine, puis remplace
directement sa plage de taille fixe dans le flux Unity `.resS`. Il refuse la
sortie si un objet sérialisé, une référence de sprite, une texture hors cartes,
un offset ou un octet extérieur aux 102 plages est modifié.

## Instance de test isolée

Une fois l'artifact construit, préparer une copie séparée du laboratoire
Pokémon :

```powershell
.\tools\prepare-pokemon-fr-test-instance.ps1
```

Cette commande copie `instance-005` vers une nouvelle base de jeu indépendante
`instance-006`, crée des sauvegardes vides et isolées, conserve une copie du
bundle anglais, installe le bundle français avec contrôle SHA256 puis vérifie
la redirection des sauvegardes. Elle ne modifie jamais `instance-005`.

Pour actualiser une `instance-006` déjà préparée en conservant son ancien
bundle français comme retour arrière :

```powershell
.\tools\update-pokemon-fr-test-instance.ps1
```

Le test interactif se lance ensuite avec **Lancer-test-pokemon-fr.cmd**.

## Vérifications effectuées

- 102 images françaises présentes, restaurées et associées aux numéros 1 à
  102.
- 81 logos « Édition 1 » détectés puis retirés à partir de la géométrie des
  cartes anglaises correspondantes ; aucun pixel anglais n'est injecté dans
  les cartes françaises.
- 21 cartes sans logo laissées intactes par l'étape de normalisation.
- 102 textures BC7 redécodées et contrôlées par rapport aux rendus français
  normalisés.
- Tous les objets Unity sérialisés, sprites, offsets, tailles et pixels hors
  cartes identiques à la source.
- Le texte, les identifiants de cartes et les références Unity ne sont pas
  modifiés par la normalisation du tirage.
- Reconstruction déterministe et empreintes SHA256 consignées dans le
  manifeste de l'artifact 0.3.0.
- Validation du lanceur et de l'isolation de `instance-006` réussie.
- Démarrage sans affichage : préchargement Enhanced Prefab Loader terminé,
  35 champs d'édition ajoutés et aucune erreur de lecture AssetBundle. Les
  erreurs de shaders du périphérique graphique nul sont attendues dans ce mode
  et ne permettent pas de valider le rendu.

## Limites actuelles

- Il s'agit d'un artifact de laboratoire, pas d'un paquet à publier.
- Les noms affichés par les interfaces restent inchangés : cette étape porte
  uniquement sur le texte réellement imprimé dans les images des cartes.
- Le grain, la netteté, le rendu des couleurs et la variante de tirage sont
  homogénéisés. Le bundle Base Set normal ne présente plus les logos
  « Édition 1 » issus du mélange des scans TCGdex.
- Le rendu en jeu doit encore être contrôlé dans `instance-006`, notamment
  l'alignement des zones holographiques et la lisibilité sur les cartes tenues.
- La redistribution des visuels et des assets du mod demanderait les
  autorisations appropriées ; l'artifact est destiné au test local.
