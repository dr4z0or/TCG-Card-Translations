using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TCG.CardTranslations.Core;
using UnityEngine;

using static TCG.Shared.Lang;
namespace TCG.CardTranslations
{
    // Shows translated images and names for the cards that Enhanced Prefab Loader adds,
    // without modifying the packs. Translations are folders next to this DLL, one per language:
    //   <this folder>/FR/Pokemon/Cards/01 - Base/01 - Base Set/049.png (or 49_BS_Drowzee.png)
    //   <this folder>/FR/Pokemon/Cards/01 - Base/01 - Base Set/names.json  { "49": "Soporifik" }
    // The game language picks the folder. A card without a translated file keeps its original image and name.
    [BepInPlugin(Guid, "TCG Card Translations", "1.0.0")]
    [BepInDependency("EnhancedPrefabLoader", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.tcg.cardtranslations";
        static Plugin? instance;

        ConfigEntry<bool> active = null!;
        ConfigEntry<string> language = null!;
        ConfigEntry<int> maxImages = null!;
        ConfigEntry<bool> compress = null!;
        ConfigEntry<bool> diskCache = null!;
        TranslationCatalog catalog = null!;
        TextOverlay texts = null!;
        TranslatedImages images = null!;
        MethodInfo? cancelPending;
        MethodInfo? setSpriteAsync;
        FieldInfo? centerFrameImage;
        [ThreadStatic] static bool bypass;

        // MonsterData of each translated card -> translated name.
        readonly Dictionary<object, string> names = new Dictionary<object, string>(ReferenceComparer.Instance);
        // Bundle path + sprite of every card face: only these may match an image by card number.
        readonly HashSet<string> faces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        float lastForcedRefresh = -10f;
        int knownExpansions = -1, knownCards = -1;
        float nextCheck, reportAt = -1;
        int messages;
        volatile bool textsChanged;

        void Awake()
        {
            instance = this;
            active = Config.Bind("General", "Enabled", true,
                "Shows the translated images and names of the cards added by Enhanced Prefab Loader. / Affiche les images et les noms traduits des cartes ajoutées par Enhanced Prefab Loader.");
            language = Config.Bind("General", "Language", "auto",
                "Translation folder to use: auto (the game's language) or a folder name such as FR. Restart the game after a change. / Dossier de traduction : auto (langue du jeu) ou un nom de dossier comme FR. Relancer le jeu après un changement.");
            maxImages = Config.Bind("Images", "ImagesInMemory", 200,
                "Translated images kept in memory (about 0.6 MB each). Displayed images are never unloaded. / Images traduites gardées en mémoire (environ 0,6 Mo chacune). Les images affichées ne sont jamais déchargées.");
            compress = Config.Bind("Images", "CompressImages", true,
                "Compresses the images in memory (4 to 6 times less memory, very slight loss of sharpness). / Compresse les images en mémoire (4 à 6 fois moins de mémoire, très légère perte de netteté).");
            diskCache = Config.Bind("Images", "DiskCache", true,
                "Keeps prepared images in BepInEx\\cache\\TCGCardTranslations: much faster loading in later games. Rebuilt when an image changes. / Garde les images préparées dans BepInEx\\cache\\TCGCardTranslations : chargement bien plus rapide ensuite. Refait quand une image change.");
            if (!active.Value) { Logger.LogInfo(T("Traduction des cartes désactivée dans la configuration.")); return; }

            var pluginFolder = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
            var wanted = string.Equals(language.Value.Trim(), "auto", StringComparison.OrdinalIgnoreCase) ? GameLanguageName() : language.Value.Trim();
            var folder = Languages.Resolve(pluginFolder, wanted);
            if (folder == null)
            {
                var available = Languages.Available(pluginFolder);
                Logger.LogInfo(F("Aucune traduction pour la langue « {0} » (dossiers disponibles : {1}). Les cartes restent d'origine.",
                    wanted == "" ? "?" : wanted, available.Length == 0 ? "-" : string.Join(", ", available)));
                return;
            }
            var root = Path.Combine(pluginFolder, folder);
            Logger.LogInfo(F("Traductions utilisées : {0}", folder));
            catalog = TranslationCatalog.Load(root);
            foreach (var e in catalog.Editions)
            {
                Logger.LogInfo(F("Traduction : {0} — {1} image(s), {2} nom(s).", e.RelativePath, e.Images.Count, e.Names.Count));
                foreach (var p in e.Problems) Logger.LogWarning($"{e.RelativePath} : {p}");
            }
            texts = TextOverlay.Load(root);
            if (texts.Files.Count > 0) Logger.LogInfo(F("Textes des menus : {0} traduction(s) ({1}).", texts.Count, string.Join(", ", texts.Files)));
            foreach (var p in texts.Problems) Logger.LogWarning(p);
            if (catalog.Editions.Count == 0 && texts.Count == 0) Logger.LogInfo(F("Dossier {0} encore vide : les listes de cartes et de textes à traduire y seront écrites.", root));
            var cacheDir = diskCache.Value ? Path.Combine(Paths.CachePath, "TCGCardTranslations") : null;
            images = new TranslatedImages(ShowOriginal, Warn, () => maxImages.Value, () => compress.Value, cacheDir);
            if (cacheDir != null) PruneCache(cacheDir);
            Patch();
        }

        // Removes cache files of images that were edited or deleted since they were cached.
        void PruneCache(string cacheDir)
        {
            try
            {
                var keep = catalog.Editions.SelectMany(e => Directory.GetFiles(e.Directory))
                    .Where(f => new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(f => ImageCache.FileFor(cacheDir, f, compress.Value));
                var removed = ImageCache.Prune(cacheDir, keep);
                if (removed > 0) Logger.LogInfo(F("Cache des images : {0} fichier(s) périmé(s) supprimé(s).", removed));
            }
            catch (Exception e) { Warn(F("Nettoyage du cache impossible : {0}", e.Message)); }
        }

        void Patch()
        {
            var harmony = new Harmony(Guid);
            var extensions = AccessTools.TypeByName(EplBridge.ExtensionsType);
            setSpriteAsync = extensions == null ? null : AccessTools.Method(extensions, "SetSpriteAsync");
            cancelPending = extensions == null ? null : AccessTools.Method(extensions, "CancelPendingSpriteAsync");
            if (setSpriteAsync == null || cancelPending == null || !ImageSprite.Available)
                Logger.LogWarning(T("Enhanced Prefab Loader absent ou version non reconnue : les images des cartes restent d'origine."));
            else
                TryPatch(harmony, setSpriteAsync, prefix: nameof(SetSpriteAsyncPrefix));

            var cardUI = AccessTools.TypeByName("CardUI");
            centerFrameImage = cardUI == null ? null : AccessTools.Field(cardUI, "m_CenterFrameImage");
            var setCardUI = cardUI == null ? null : AccessTools.Method(cardUI, "SetCardUI");
            if (setCardUI != null && centerFrameImage != null) TryPatch(harmony, setCardUI, prefix: nameof(SetCardUIPrefix));

            var processing = AccessTools.TypeByName(EplBridge.ProcessingType);
            var processAsync = processing == null ? null : AccessTools.Method(processing, "ProcessAsync");
            if (processAsync != null) TryPatch(harmony, processAsync, prefix: nameof(ProcessAsyncPrefix));
            else Logger.LogWarning(T("Lecture des packs EPL non reconnue : les noms d'éditions et de produits restent d'origine."));

            var getName = AccessTools.Method(AccessTools.TypeByName("MonsterData"), "GetName");
            if (getName != null) TryPatch(harmony, getName, postfix: nameof(GetNamePostfix));
            else Logger.LogWarning(T("MonsterData.GetName introuvable : les noms des cartes restent d'origine."));
        }

        void TryPatch(Harmony harmony, MethodBase target, string? prefix = null, string? postfix = null)
        {
            try
            {
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(Plugin), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(Plugin), postfix) { priority = Priority.Last });
            }
            catch (Exception e) { Logger.LogWarning(F("Patch impossible sur {0}.{1} : {2}", target.DeclaringType?.Name, target.Name, e.Message)); }
        }

        void Update()
        {
            if (images == null) return;
            images.Update();
            if (Time.realtimeSinceStartup >= nextCheck)
            {
                nextCheck = Time.realtimeSinceStartup + 1f;
                RefreshNames();
            }
            if (textsChanged) { textsChanged = false; reportAt = Time.realtimeSinceStartup + 5f; }
            if (reportAt >= 0 && Time.realtimeSinceStartup >= reportAt) { reportAt = -1; WriteReport(); }
        }

        // ---- Images ----

        // StreamingAssetExtensions.SetSpriteAsync(UnityEngine.UI.Image image, StreamingAsset<Sprite> asset): EPL's
        // only way to show a card face. A translated card is shown from our file instead.
        static bool SetSpriteAsyncPrefix(Component image, object asset)
        {
            var self = instance;
            if (bypass || self == null || image == null || asset == null) return true;
            try
            {
                var path = self.ImageFor(asset);
                if (path == null) { self.images.Forget(image); return true; }
                self.cancelPending!.Invoke(null, new object[] { image });
                self.images.Show(image, path, asset);
                return false;
            }
            catch (Exception e)
            {
                self.Warn(F("Erreur d'affichage d'une carte traduite : {0}", e.Message));
                return true;
            }
        }

        string? ImageFor(object asset)
        {
            var bundle = EplBridge.Text(asset, "BundlePath");
            var edition = catalog.ForBundle(bundle);
            if (edition == null) return null;
            var sprite = EplBridge.Text(asset, "AssetName");
            var key = bundle + "|" + sprite;
            if (!faces.Contains(key) && Time.realtimeSinceStartup - lastForcedRefresh > 1f)
            {
                // EPL may show cards before our periodic refresh has read its list.
                lastForcedRefresh = Time.realtimeSinceStartup;
                RefreshNames();
            }
            var path = faces.Contains(key) ? edition.Images.Find(sprite) : edition.Images.FindExact(sprite);
            return path == null || images.HasFailed(path) ? null : path;
        }

        // Puts EPL's original image back, for a translated file that could not be read.
        void ShowOriginal(Component image, object asset)
        {
            bypass = true;
            try { setSpriteAsync?.Invoke(null, new[] { image, asset }); }
            catch (Exception e) { Warn(F("Image d'origine non rétablie : {0}", e.Message)); }
            finally { bypass = false; }
        }

        // A card UI is about to show another card: whatever we were loading for it no longer applies.
        static void SetCardUIPrefix(object __instance)
        {
            var self = instance;
            if (self?.images == null || self.centerFrameImage == null) return;
            try { self.images.Forget(self.centerFrameImage.GetValue(__instance) as Component); }
            catch (Exception e) { self.Warn("SetCardUI : " + e.Message); }
        }

        // ---- Menu texts ----

        // ProcessingService.ProcessAsync(BundleDescriptor descriptor, BundleInfo bundleInfo): EPL turns
        // a pack's JSON into game data. Display texts are translated just before, exactly as if
        // the JSON had been edited: edition names, products, decorations and shop apps.
        static void ProcessAsyncPrefix(object descriptor, object bundleInfo)
        {
            var self = instance;
            if (self?.texts == null || descriptor == null) return;
            lock (self.texts)
            try
            {
                var pack = string.Join("/", DetectedReport.PackFolders(EplBridge.Text(bundleInfo, "BundlePath")));
                self.TranslateList(descriptor, "CardExpansions", "Name", TextOverlay.Edition, pack);
                self.TranslateList(descriptor, "Items", "Name", TextOverlay.Product, pack);
                self.TranslateList(descriptor, "Prefabs", "Name", TextOverlay.Decoration, pack);
                self.TranslateList(descriptor, "CustomShops", "AppDisplayName", TextOverlay.PhoneShop, pack);
                // EPL may run this off the main thread: Unity is only touched from Update.
                self.textsChanged = true;
            }
            catch (Exception e) { self.Warn(F("Traduction des textes d'un pack impossible : {0}", e.Message)); }
        }

        void TranslateList(object descriptor, string list, string field, string kind, string pack)
        {
            if (!(EplBridge.Get(descriptor, list) is IEnumerable entries)) return;
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var french = texts.Translate(kind, pack, EplBridge.Text(entry, field));
                if (french != null && !EplBridge.Set(entry, field, french)) Warn(F("Champ {0} non modifiable ({1}).", field, entry.GetType().Name));
            }
        }

        // ---- Names ----

        static void GetNamePostfix(object __instance, ref string __result)
        {
            var self = instance;
            if (self == null || self.names.Count == 0 || __instance == null) return;
            if (self.names.TryGetValue(__instance, out var name)) __result = name;
        }

        // EPL fills its card list while bundles load; the name table follows it.
        void RefreshNames()
        {
            IDictionary? expansions;
            try { expansions = EplBridge.Expansions(); }
            catch (Exception e) { Warn(F("Lecture des éditions EPL impossible : {0}", e.Message)); return; }
            if (expansions == null || expansions.Count == knownExpansions) return;
            var cards = EplBridge.Cards(expansions);
            knownExpansions = expansions.Count;
            if (cards.Count == knownCards) return;
            knownCards = cards.Count;
            names.Clear();
            faces.Clear();
            foreach (var card in cards)
            {
                faces.Add(card.Bundle + "|" + card.Sprite);
                if (card.Monster == null) continue;
                var name = catalog.ForBundle(card.Bundle)?.Names.Find(card.Sprite);
                if (name != null) names[card.Monster] = name;
            }
            reportAt = Time.realtimeSinceStartup + 5f;
        }

        // detected-cards.txt: every EPL card, with the translation found for it.
        void WriteReport()
        {
            try
            {
                lock (texts) if (texts.SeenCount > 0)
                {
                    File.WriteAllText(Path.Combine(catalog.Root, TextOverlay.ReportName), texts.Report(), new UTF8Encoding(true));
                    Logger.LogInfo(F("Textes des packs : {0}/{1} traduits. Liste : {2}", texts.TranslatedCount, texts.SeenCount, TextOverlay.ReportName));
                }
                var expansions = EplBridge.Expansions();
                if (expansions == null) return;
                var cards = EplBridge.Cards(expansions).Select(c => new DetectedCard
                {
                    Bundle = c.Bundle,
                    Sprite = c.Sprite,
                    EnglishName = c.Monster == null ? "" : EplBridge.Text(c.Monster, "Name"),
                }).ToList();
                var text = DetectedReport.Build(catalog, cards, out var imageCount, out var nameCount);
                var file = Path.Combine(catalog.Root, DetectedReport.FileName);
                File.WriteAllText(file, text, new UTF8Encoding(true));
                Logger.LogInfo(F("{0} cartes EPL détectées : {1} image(s) et {2} nom(s) traduits. Liste : {3}", cards.Count, imageCount, nameCount, file));
            }
            catch (Exception e) { Warn(F("Liste des cartes non écrite : {0}", e.Message)); }
        }

        // Game language: I2 Localization's code ("fr"), else the language name the game saved ("French").
        static string GameLanguageName()
        {
            var code = TCG.Shared.Lang.GameLanguage();
            if (code != "") return code;
            try { return PlayerPrefs.GetString("I2 Language", ""); } catch (Exception) { return ""; }
        }

        void Warn(string message)
        {
            if (messages++ < 30) Logger.LogWarning(message);
        }

        void OnDestroy()
        {
            if (images != null) Logger.LogInfo(F("Traduction des cartes : {0}", images.Stats));
        }
    }
}
