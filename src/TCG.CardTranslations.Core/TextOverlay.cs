using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using static TCG.Shared.Lang;
namespace TCG.CardTranslations.Core
{
    // Translations of the texts that packs declare in their JSON and that the menus show:
    // edition names, product names, decorations and shop apps. Every "texts.json" (or "textes.json")
    // under the language folder is read: { "Base Set": "Set de Base", "Base Set Booster Pack": "Booster Set de Base" }.
    // The English text must match exactly (spaces at both ends ignored).
    public sealed class TextOverlay
    {
        public const string FileName = "texts.json";
        public const string FrenchFileName = "textes.json";
        public const string ReportName = "detected-texts.txt";

        readonly Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, Seen> seen = new Dictionary<string, Seen>(StringComparer.Ordinal);

        public List<string> Files { get; } = new List<string>();
        public List<string> Problems { get; } = new List<string>();
        public int Count => texts.Count;

        sealed class Seen
        {
            public string Kind = "", Pack = "", English = "";
            public string? French;
        }

        public static TextOverlay Load(string root)
        {
            var overlay = new TextOverlay();
            if (!Directory.Exists(root)) return overlay;
            var files = Directory.GetFiles(root, FileName, SearchOption.AllDirectories).Concat(Directory.GetFiles(root, FrenchFileName, SearchOption.AllDirectories));
            foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var relative = PathText.Relative(root, file).Replace('\\', '/');
                try
                {
                    foreach (var kv in FlatJson.Parse(File.ReadAllText(file, Encoding.UTF8)))
                    {
                        var english = kv.Key.Trim();
                        var french = kv.Value.Trim();
                        if (english.Length == 0 || french.Length == 0) continue;
                        if (overlay.texts.TryGetValue(english, out var other) && other != french)
                            overlay.Problems.Add(F("{0} : « {1} » déjà traduit autrement (« {2} »), traduction remplacée", relative, english, other));
                        overlay.texts[english] = french;
                    }
                    overlay.Files.Add(relative);
                }
                catch (Exception e) when (e is FormatException || e is IOException) { overlay.Problems.Add(F("{0} ignoré : {1}", relative, e.Message)); }
            }
            return overlay;
        }

        // Returns the translation of a pack text and remembers it for the report.
        public string? Translate(string kind, string pack, string? english)
        {
            if (english == null) return null;
            var key = english.Trim();
            if (key.Length == 0) return null;
            texts.TryGetValue(key, out var french);
            var id = kind + "|" + pack + "|" + key;
            if (!seen.ContainsKey(id)) seen[id] = new Seen { Kind = kind, Pack = pack, English = key, French = french };
            return french;
        }

        // Kinds of pack texts (the plugin passes these French keys); shown translated in the report.
        public const string Edition = "édition", Product = "produit", Decoration = "décoration ou meuble", PhoneShop = "boutique du téléphone";
        static string KindLabel(string kind) => kind == Edition ? T("édition") : kind == Product ? T("produit") :
            kind == Decoration ? T("décoration ou meuble") : kind == PhoneShop ? T("boutique du téléphone") : kind;
        public int SeenCount => seen.Count;
        public int TranslatedCount => seen.Values.Count(s => s.French != null);

        // detected-texts.txt: every text met, grouped by pack, ready to copy into texts.json.
        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine(T("Textes des packs Enhanced Prefab Loader (éditions, produits, décorations, boutiques)."));
            sb.AppendLine(T("Pour traduire : ajouter dans un fichier texts.json (ou textes.json) du dossier de la langue une ligne"));
            sb.AppendLine(T("\"texte anglais exact\": \"traduction\". Les lignes « à traduire » ci-dessous sont déjà au bon format."));
            foreach (var pack in seen.Values.GroupBy(s => s.Pack).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine("== " + pack.Key);
                foreach (var kind in pack.GroupBy(s => s.Kind).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    sb.AppendLine("   [" + KindLabel(kind.Key) + "]");
                    foreach (var s in kind.OrderBy(s => s.English, StringComparer.OrdinalIgnoreCase))
                        sb.AppendLine(s.French != null
                            ? $"   {s.English} -> {s.French}"
                            : "   " + F("à traduire : {0}: {0},", FlatJson.Quote(s.English)));
                }
            }
            var used = new HashSet<string>(seen.Values.Select(s => s.English), StringComparer.Ordinal);
            var unused = texts.Keys.Where(k => !used.Contains(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            if (unused.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("! " + T("Traductions sans texte correspondant dans les packs chargés :"));
                foreach (var k in unused) sb.AppendLine("   " + k);
            }
            foreach (var p in Problems) sb.AppendLine("! " + p);
            return sb.ToString();
        }
    }
}
