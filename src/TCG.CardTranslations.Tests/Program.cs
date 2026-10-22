using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TCG.CardTranslations.Core;

// Tests hors jeu du mod de traduction des cartes. Argument facultatif : un dossier
// du mod assemblé à contrôler (artifacts\card-translations\TCGCardTranslations).
static class Program
{
    static int failures, checks;

    static void Check(bool ok, string what)
    {
        checks++;
        if (ok) Console.WriteLine("OK   " + what);
        else { failures++; Console.WriteLine("ECHEC " + what); }
    }

    static int Main(string[] args)
    {
        // The checked texts are the French ones.
        AppDomain.CurrentDomain.SetData("TCG.Language", "fr");
        Keys();
        LanguageFolders();
        Json();
        Cache();
        Texts();
        var root = Path.Combine(Path.GetTempPath(), "ct" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try { Catalog(root); }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
        if (args.Length > 0)
        {
            var languages = Languages.Available(args[0]);
            Check(languages.Length > 0, $"dossiers de langue dans {args[0]} : {string.Join(", ", languages)}");
            foreach (var language in languages) RealEditions(Path.Combine(args[0], language));
        }
        Console.WriteLine($"{checks - failures}/{checks} vérifications réussies.");
        return failures == 0 ? 0 : 1;
    }

    static void Keys()
    {
        Check(CardKeys.Number("49_BS_Drowzee") == "49", "numéro tiré du nom de la carte");
        Check(CardKeys.Number("049") == "49", "zéros initiaux ignorés");
        Check(CardKeys.Number("50a_AQ_Kingdra") == "50a" && CardKeys.Number("050A") == "50a", "suffixe a/b conservé");
        Check(CardKeys.Number("Charizard") == null && CardKeys.Number("A2_001") == null, "pas de numéro sans chiffre au début");
        Check(CardKeys.Number("000") == "0", "numéro zéro");
        var lookup = new KeyedLookup<string>();
        lookup.Add("049", "image49");
        lookup.Add("4_BS_Charizard", "dracaufeu");
        lookup.Add("4", "autre4");
        Check(lookup.Find("49_BS_Drowzee") == "image49", "image trouvée par numéro");
        Check(lookup.Find("4_bs_charizard") == "dracaufeu", "nom exact prioritaire, sans tenir compte de la casse");
        Check(lookup.Find("4_BS_Other") == null && lookup.AmbiguousNumbers.Contains("4"), "numéro ambigu refusé et signalé");
        Check(lookup.FindExact("49_BS_Drowzee_Foil") == null && lookup.Find("49_BS_Drowzee_Foil") == "image49" && lookup.FindExact("4_BS_CHARIZARD") == "dracaufeu",
            "masques et dos de carte : nom exact uniquement");
        Check(lookup.Unused(new[] { "49_BS_Drowzee" }).SequenceEqual(new[] { "4", "4_bs_charizard" }), "entrées inutilisées listées");
        Check(CardKeys.EndsWith(CardKeys.BundleFolder(@"C:\Jeu\BepInEx\plugins\Real TCG Overhaul\Pokemon\Cards\01 - Base\01 - Base Set_prefabloader\basesetexpansion"),
            CardKeys.Segments("Pokemon/Cards/01 - Base/01 - Base Set")), "dossier d'édition reconnu à la fin du chemin du bundle");
    }

    static void Json()
    {
        var d = FlatJson.Parse("\uFEFF{ \"_note\": \"ignoré\", \"49\": \"Soporifik\", \"x\": 3, \"y\": {\"a\":[1,2]}, \"e\": \"\\u00c9nergie \\\"Eau\\\"\" }");
        Check(d.Count == 2 && d["49"] == "Soporifik" && d["e"] == "Énergie \"Eau\"", "noms.json : chaînes, échappements, commentaires et valeurs non textuelles");
        var round = FlatJson.Parse(FlatJson.Write(new[] { new KeyValuePair<string, string>("1", "Mélofée\n\"x\"") }));
        Check(round["1"] == "Mélofée\n\"x\"", "écriture puis relecture identiques");
        Check(Throws(() => FlatJson.Parse("{\"a\": \"b\"")), "fichier tronqué refusé");
        Check(Throws(() => FlatJson.Parse("{\"a\": \"b\"} x")), "texte après l'objet refusé");
        Check(FlatJson.Parse("{ }").Count == 0, "objet vide accepté");
    }

    static void Cache()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ctc" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        try
        {
            var entry = new ImageCache.Entry { Width = 820, Height = 1024, Format = 10, MipCount = 11, Data = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray() };
            var back = ImageCache.Decode(ImageCache.Encode(entry));
            Check(back != null && back.Width == 820 && back.Height == 1024 && back.Format == 10 && back.MipCount == 11 && back.Data.SequenceEqual(entry.Data), "cache : écriture puis relecture identiques");
            var truncated = ImageCache.Encode(entry).Take(500).ToArray();
            Check(ImageCache.Decode(truncated) == null && ImageCache.Decode(new byte[] { 1, 2, 3 }) == null, "cache : fichier tronqué ou étranger ignoré");

            var png = Path.Combine(dir, "049.png");
            File.WriteAllBytes(png, new byte[] { 1, 2, 3 });
            var cacheDir = Path.Combine(dir, "cache");
            var first = ImageCache.FileFor(cacheDir, png, true);
            Check(first == ImageCache.FileFor(cacheDir, png, true) && first != ImageCache.FileFor(cacheDir, png, false), "cache : même image, même fichier ; réglage de compression distinct");
            ImageCache.Write(first, entry);
            Check(ImageCache.Read(first)?.Data.Length == 1000, "cache : fichier écrit et relu");
            File.WriteAllBytes(png, new byte[] { 1, 2, 3, 4 });
            File.SetLastWriteTimeUtc(png, DateTime.UtcNow.AddMinutes(1));
            var second = ImageCache.FileFor(cacheDir, png, true);
            Check(second != first, "cache : une image modifiée change de fichier de cache");
            ImageCache.Write(second, entry);
            File.WriteAllText(Path.Combine(cacheDir, "autre.txt"), "x");
            Check(ImageCache.Prune(cacheDir, new[] { second }) == 1 && !File.Exists(first) && File.Exists(second) && File.Exists(Path.Combine(cacheDir, "autre.txt")),
                "cache : seul l'ancien fichier de l'image modifiée est supprimé");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    static void Texts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ctt" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Pokemon"));
            Directory.CreateDirectory(Path.Combine(dir, "Autre"));
            File.WriteAllText(Path.Combine(dir, "Pokemon", "textes.json"), "{ \"_note\": \"x\", \"Base Set\": \"Set de Base\", \"Base Set Booster Pack\": \"Booster Set de Base\", \"Old\": \"Ancien\" }");
            File.WriteAllText(Path.Combine(dir, "Autre", "textes.json"), "{ \"Base Set\": \"Set de base\" }");
            Directory.CreateDirectory(Path.Combine(dir, "Anglais"));
            File.WriteAllText(Path.Combine(dir, "Anglais", "texts.json"), "{ \"Jungle\": \"Jungle\" }");
            var overlay = TextOverlay.Load(dir);
            Check(overlay.Files.Count == 3 && overlay.Count == 4, "textes.json et texts.json de chaque dossier réunis");
            Check(overlay.Problems.Any(p => p.Contains("Base Set")), "même texte traduit deux fois différemment : signalé");
            Check(overlay.Translate("produit", "Real TCG Overhaul/Pokemon", " Base Set Booster Pack ") == "Booster Set de Base", "texte exact traduit (espaces ignorés)");
            Check(overlay.Translate("produit", "Real TCG Overhaul/Pokemon", "Base Set Booster Box") == null, "texte inconnu laissé d'origine");
            Check(overlay.Translate("édition", "Real TCG Overhaul/Pokemon", "base set") == null, "casse respectée");
            var report = overlay.Report();
            Check(report.Contains("Base Set Booster Pack -> Booster Set de Base"), "rapport : texte traduit");
            Check(report.Contains("à traduire : \"Base Set Booster Box\": \"Base Set Booster Box\","), "rapport : ligne prête à copier pour un texte à traduire");
            Check(report.Contains("Old") && report.Contains("sans texte correspondant"), "rapport : traduction inutilisée signalée");
            Check(overlay.SeenCount == 3 && overlay.TranslatedCount == 1, "compteurs du rapport");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    static void LanguageFolders()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ctl" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            foreach (var name in new[] { "FR", "PT-BR", "de", "Pokemon" }) Directory.CreateDirectory(Path.Combine(dir, name));
            Check(Languages.Available(dir).SequenceEqual(new[] { "de", "FR", "PT-BR" }), "dossiers de langue reconnus (pas les autres dossiers)");
            Check(Languages.Resolve(dir, "fr") == "FR" && Languages.Resolve(dir, "fr-FR") == "FR" && Languages.Resolve(dir, "French") == "FR" && Languages.Resolve(dir, "Français") == "FR", "code ou nom de langue du jeu vers le dossier FR");
            Check(Languages.Resolve(dir, "pt-BR") == "PT-BR" && Languages.Resolve(dir, "DE") == "de" && Languages.Resolve(dir, "German") == "de", "variante régionale et casse");
            Check(Languages.Resolve(dir, "en") == null && Languages.Resolve(dir, "") == null && Languages.Resolve(dir, "Spanish") == null, "langue sans dossier : rien appliqué");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    static bool Throws(Action a) { try { a(); return false; } catch (FormatException) { return true; } }

    static void Catalog(string root)
    {
        var baseSet = Directory.CreateDirectory(Path.Combine(root, "Pokemon", "Cards", "01 - Base", "01 - Base Set")).FullName;
        File.WriteAllBytes(Path.Combine(baseSet, "049.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(baseSet, "4_BS_Charizard.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(baseSet, "150.png"), new byte[] { 1 });
        File.WriteAllText(Path.Combine(baseSet, "noms.json"), "{ \"49\": \"Soporifik\", \"4\": \"Dracaufeu\" }");
        var jungle = Directory.CreateDirectory(Path.Combine(root, "03 - Jungle")).FullName;
        File.WriteAllText(Path.Combine(jungle, "noms.json"), "{ \"1\": \"Mélodelfe\" }");
        var broken = Directory.CreateDirectory(Path.Combine(root, "Cassé")).FullName;
        File.WriteAllText(Path.Combine(broken, "noms.json"), "{ pas du json");
        Directory.CreateDirectory(Path.Combine(root, "Vide"));
        var fossil = Directory.CreateDirectory(Path.Combine(root, "05 - Fossil")).FullName;
        File.WriteAllText(Path.Combine(fossil, "names.json"), "{ \"1\": \"Aérodactyl\" }");
        File.WriteAllText(Path.Combine(root, "cartes-detectees.txt"), "rapport");

        var catalog = TranslationCatalog.Load(root);
        Check(catalog.Editions.Count == 4, "quatre dossiers d'édition (le dossier vide et la racine sont ignorés)");
        Check(catalog.Editions.Single(e => e.RelativePath == "05 - Fossil").Names.Find("1_FO_Aerodactyl") == "Aérodactyl", "names.json lu comme noms.json");
        const string bundle = @"C:\Jeu\BepInEx\plugins\Real TCG Overhaul\Pokemon\Cards\01 - Base\01 - Base Set_prefabloader\basesetexpansion";
        var edition = catalog.ForBundle(bundle);
        Check(edition?.RelativePath == "Pokemon/Cards/01 - Base/01 - Base Set", "Base Set associé à son bundle");
        Check(edition?.Images.Find("49_BS_Drowzee")?.EndsWith("049.png") == true, "image 049.png pour 49_BS_Drowzee");
        Check(edition?.Names.Find("49_BS_Drowzee") == "Soporifik" && edition?.Names.Find("4_BS_Charizard") == "Dracaufeu", "noms trouvés par numéro");
        Check(edition?.Images.Find("2_BS_Blastoise") == null, "carte sans image : reste d'origine");
        Check(catalog.ForBundle(@"D:/Autre/Pokemon/Cards/01 - Base/03 - Jungle_prefabloader/jungleexpansion")?.RelativePath == "03 - Jungle", "nom de dossier court accepté");
        Check(catalog.ForBundle(@"C:\Jeu\Pokemon\Cards\01 - Base\02 - 1st Edition Base Set_prefabloader\x") == null, "Édition 1 non confondue avec le Base Set");
        Check(catalog.ForBundle("") == null, "chemin vide");
        Check(catalog.Editions.Single(e => e.RelativePath == "Cassé").Problems.Any(p => p.Contains("noms.json")), "noms.json invalide signalé sans bloquer");

        var report = DetectedReport.Build(catalog, new[]
        {
            new DetectedCard { Bundle = bundle, Sprite = "49_BS_Drowzee", EnglishName = "Drowzee" },
            new DetectedCard { Bundle = bundle, Sprite = "4_BS_Charizard", EnglishName = "Charizard" },
            new DetectedCard { Bundle = bundle, Sprite = "2_BS_Blastoise", EnglishName = "Blastoise" },
            new DetectedCard { Bundle = @"C:\Jeu\BepInEx\plugins\Pack Gym\Gym\01 - Gym Heroes_prefabloader\gymheroes", Sprite = "1_GH_Blaine", EnglishName = "Blaine's Moltres" },
        }, out var images, out var names);
        Check(images == 2 && names == 2, "rapport : 2 images et 2 noms trouvés");
        Check(report.Contains("49_BS_Drowzee | Drowzee | 049.png | Soporifik"), "rapport : ligne d'une carte traduite");
        Check(report.Contains("2_BS_Blastoise | Blastoise | - | -"), "rapport : carte non traduite");
        Check(report.Contains("image sans carte correspondante : 150"), "rapport : image en trop signalée");
        Check(report.Contains("à créer : « Gym/01 - Gym Heroes »"), "rapport : dossier à créer proposé");
        Check(report.Contains("== Real TCG Overhaul/Pokemon/Cards/01 - Base/01 - Base Set"), "rapport : édition nommée comme dans le pack");
        Check(report.Contains("Dossier sans édition correspondante : 03 - Jungle"), "rapport : dossier inutilisé signalé");
        AppDomain.CurrentDomain.SetData("TCG.Language", "en");
        System.Threading.Thread.Sleep(1100); // each mod reads the shared choice at most once a second
        var english = DetectedReport.Build(catalog, new[] { new DetectedCard { Bundle = bundle, Sprite = "2_BS_Blastoise", EnglishName = "Blastoise" } }, out _, out _);
        Check(english.Contains("Cards loaded by Enhanced Prefab Loader") && english.Contains("Translation folder: Pokemon/Cards/01 - Base/01 - Base Set"), "rapport en anglais quand l'interface est en anglais");
        AppDomain.CurrentDomain.SetData("TCG.Language", "fr");
        System.Threading.Thread.Sleep(1100);
    }

    // Contrôle du vrai dossier du mod : chaque édition a des images PNG lisibles, sans numéro en double, et un nom par image.
    static void RealEditions(string root)
    {
        var catalog = TranslationCatalog.Load(root);
        Check(catalog.Editions.Count > 0, $"{catalog.Editions.Count} édition(s) dans {root}");
        foreach (var edition in catalog.Editions) RealEdition(edition);
        var texts = TextOverlay.Load(root);
        Check(texts.Problems.Count == 0, $"textes.json : {texts.Count} traduction(s), aucun problème ({string.Join("; ", texts.Problems)})");
    }

    static void RealEdition(EditionFolder edition)
    {
        var dir = edition.RelativePath;
        Check(edition.Problems.Count == 0, $"{dir} : aucun problème ({string.Join("; ", edition.Problems)})");
        var pngs = Directory.GetFiles(edition.Directory, "*.png");
        var signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Check(pngs.Length > 0 && pngs.All(p => File.ReadAllBytes(p).Take(8).SequenceEqual(signature)), $"{dir} : {pngs.Length} fichiers PNG valides");
        var numbered = pngs.Select(p => CardKeys.Number(Path.GetFileNameWithoutExtension(p))).Where(n => n != null).ToList();
        var missingNames = numbered.Where(n => edition.Names.Find(n!) == null).ToList();
        Check(missingNames.Count == 0, $"{dir} : un nom pour chaque image ({string.Join(", ", missingNames)})");
    }
}
