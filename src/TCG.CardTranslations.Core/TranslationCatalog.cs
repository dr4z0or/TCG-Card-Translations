using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using static TCG.Shared.Lang;
namespace TCG.CardTranslations.Core
{
    // One folder of translated cards: images named after the cards and an optional names.json
    // (noms.json is read too).
    public sealed class EditionFolder
    {
        public const string NamesFile = "names.json";
        public const string FrenchNamesFile = "noms.json";
        public static string NamesPath(string directory)
        {
            var names = Path.Combine(directory, NamesFile);
            return File.Exists(names) ? names : Path.Combine(directory, FrenchNamesFile);
        }
        static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

        public string Directory { get; }
        public string RelativePath { get; }
        public string[] Segments { get; }
        public KeyedLookup<string> Images { get; } = new KeyedLookup<string>();
        public KeyedLookup<string> Names { get; } = new KeyedLookup<string>();
        public List<string> Problems { get; } = new List<string>();

        public EditionFolder(string root, string directory)
        {
            Directory = directory;
            RelativePath = PathText.Relative(root, directory).Replace('\\', '/');
            Segments = CardKeys.Segments(RelativePath);
            foreach (var file in System.IO.Directory.GetFiles(directory).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ImageExtensions.Contains(ext)) Images.Add(Path.GetFileNameWithoutExtension(file), file);
            }
            var names = NamesPath(directory);
            if (File.Exists(names))
            {
                try { foreach (var kv in FlatJson.Parse(File.ReadAllText(names, Encoding.UTF8))) if (kv.Value.Trim().Length > 0) Names.Add(kv.Key, kv.Value.Trim()); }
                catch (Exception e) when (e is FormatException || e is IOException) { Problems.Add(F("{0} ignoré : {1}", Path.GetFileName(names), e.Message)); }
            }
            foreach (var n in Images.AmbiguousNumbers.Distinct()) Problems.Add(F("plusieurs images portent le numéro {0} : utiliser le nom exact de la carte", n));
            foreach (var n in Names.AmbiguousNumbers.Distinct()) Problems.Add(F("plusieurs noms portent le numéro {0} dans {1}", n, Path.GetFileName(names)));
        }

        public static bool LooksLikeEdition(string directory)
        {
            try
            {
                return File.Exists(Path.Combine(directory, NamesFile)) || File.Exists(Path.Combine(directory, FrenchNamesFile)) ||
                    System.IO.Directory.EnumerateFiles(directory).Any(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    // All translated editions under the mod folder, matched to pack bundles by folder path.
    public sealed class TranslationCatalog
    {
        readonly Dictionary<string, EditionFolder?> byBundle = new Dictionary<string, EditionFolder?>(StringComparer.OrdinalIgnoreCase);

        public string Root { get; }
        public IReadOnlyList<EditionFolder> Editions { get; }

        TranslationCatalog(string root, List<EditionFolder> editions) { Root = root; Editions = editions; }

        public static TranslationCatalog Load(string root)
        {
            var editions = new List<EditionFolder>();
            if (Directory.Exists(root))
            {
                foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                    if (EditionFolder.LooksLikeEdition(dir)) editions.Add(new EditionFolder(root, dir));
            }
            return new TranslationCatalog(root, editions);
        }

        // The edition folder whose path ends the bundle's folder path, e.g.
        // "Pokemon/Cards/01 - Base/01 - Base Set" for ".../Pokemon/Cards/01 - Base/01 - Base Set_prefabloader/basesetexpansion".
        // The longest match wins, so a short name such as "01 - Base Set" also works when it is unique.
        public EditionFolder? ForBundle(string bundlePath)
        {
            if (string.IsNullOrEmpty(bundlePath)) return null;
            if (byBundle.TryGetValue(bundlePath, out var cached)) return cached;
            var folder = CardKeys.BundleFolder(bundlePath);
            var match = Editions.Where(e => CardKeys.EndsWith(folder, e.Segments)).OrderByDescending(e => e.Segments.Length).FirstOrDefault();
            byBundle[bundlePath] = match;
            return match;
        }
    }

    // One card as Enhanced Prefab Loader loaded it, for the cards-detected report.
    public sealed class DetectedCard
    {
        public string Bundle = "";
        public string Sprite = "";
        public string EnglishName = "";
    }

    public static class DetectedReport
    {
        public const string FileName = "detected-cards.txt";

        public static string Build(TranslationCatalog catalog, IEnumerable<DetectedCard> cards, out int images, out int names)
        {
            images = 0; names = 0;
            var sb = new StringBuilder();
            sb.AppendLine(T("Cartes chargées par Enhanced Prefab Loader, par édition."));
            sb.AppendLine(T("Pour traduire une carte : placer dans le dossier de l'édition une image nommée comme la carte"));
            sb.AppendLine(T("(ou par son numéro, ex. 049.png) et/ou ajouter son nom dans names.json (ou noms.json)."));
            sb.AppendLine(T("Colonnes : carte | nom anglais | image traduite | nom traduit"));
            var usedEditions = new HashSet<EditionFolder>();
            foreach (var group in cards.GroupBy(c => c.Bundle).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var edition = catalog.ForBundle(group.Key);
                var inPack = PackFolders(group.Key);
                sb.AppendLine();
                sb.AppendLine("== " + string.Join("/", inPack));
                sb.AppendLine(edition == null
                    ? "   " + F("Dossier de traduction : aucun (à créer : « {0} »)", string.Join("/", inPack.Skip(inPack.Count > 1 ? 1 : 0)))
                    : "   " + F("Dossier de traduction : {0}", edition.RelativePath));
                if (edition != null) usedEditions.Add(edition);
                foreach (var card in group)
                {
                    var image = edition?.Images.Find(card.Sprite);
                    var name = edition?.Names.Find(card.Sprite);
                    if (image != null) images++;
                    if (name != null) names++;
                    sb.Append("   ").Append(card.Sprite).Append(" | ").Append(card.EnglishName).Append(" | ")
                      .Append(image != null ? Path.GetFileName(image) : "-").Append(" | ").Append(name ?? "-").AppendLine();
                }
                if (edition != null)
                {
                    var sprites = group.Select(c => c.Sprite).ToList();
                    foreach (var unused in edition.Images.Unused(sprites)) sb.AppendLine("   ! " + F("image sans carte correspondante : {0}", unused));
                    foreach (var unused in edition.Names.Unused(sprites)) sb.AppendLine("   ! " + F("nom sans carte correspondante : {0}", unused));
                }
            }
            foreach (var edition in catalog.Editions.Where(e => !usedEditions.Contains(e)))
            {
                sb.AppendLine();
                sb.AppendLine("! " + F("Dossier sans édition correspondante : {0}", edition.RelativePath));
            }
            foreach (var edition in catalog.Editions)
                foreach (var problem in edition.Problems) sb.AppendLine("! " + edition.RelativePath + " : " + problem);
            return sb.ToString();
        }

        // Folders of the bundle below BepInEx/plugins, as written on disk, without the
        // "_prefabloader" suffix and without the bundle file: "Real TCG Overhaul/Pokemon/Cards/01 - Base/01 - Base Set".
        // The first one is the pack's own folder, which translation folders do not need to repeat.
        public static List<string> PackFolders(string bundlePath)
        {
            var raw = bundlePath.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (raw.Count > 0) raw.RemoveAt(raw.Count - 1);
            var plugins = raw.FindLastIndex(r => string.Equals(r, "plugins", StringComparison.OrdinalIgnoreCase));
            var tail = plugins >= 0 ? raw.Skip(plugins + 1) : raw.Skip(Math.Max(0, raw.Count - 4));
            return tail.Select(r => r.EndsWith("_prefabloader", StringComparison.OrdinalIgnoreCase) ? r.Substring(0, r.Length - "_prefabloader".Length).TrimEnd() : r).ToList();
        }
    }
}
