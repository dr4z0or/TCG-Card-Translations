using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TCG.CardTranslations.Core
{
    // One folder per language directly in the mod folder: FR/, DE/, PT-BR/…
    // The game language picks the folder; the player can force another one in the configuration.
    public static class Languages
    {
        static readonly Regex Code = new Regex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,4})?$");
        static readonly string[][] Names =
        {
            new[] { "FR", "french", "français", "francais" }, new[] { "EN", "english" }, new[] { "DE", "german", "deutsch" },
            new[] { "ES", "spanish", "español", "espanol" }, new[] { "IT", "italian", "italiano" }, new[] { "PT", "portuguese", "português", "portugues" },
            new[] { "NL", "dutch", "nederlands" }, new[] { "PL", "polish", "polski" }, new[] { "RU", "russian" }, new[] { "TR", "turkish", "türkçe" },
            new[] { "JA", "japanese" }, new[] { "KO", "korean" }, new[] { "ZH", "chinese" }, new[] { "TH", "thai" }, new[] { "ID", "indonesian" },
            new[] { "VI", "vietnamese" }, new[] { "UK", "ukrainian" }, new[] { "SV", "swedish" }, new[] { "DA", "danish" }, new[] { "NO", "norwegian" },
            new[] { "FI", "finnish" }, new[] { "CS", "czech" }, new[] { "HU", "hungarian" }, new[] { "RO", "romanian" }, new[] { "AR", "arabic" },
        };

        public static bool IsFolder(string name) => Code.IsMatch(name);

        public static string[] Available(string root)
        {
            if (!Directory.Exists(root)) return new string[0];
            return Directory.GetDirectories(root).Select(Path.GetFileName).Where(IsFolder).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        // "fr", "fr-FR", "French", "Français", "pt-BR"… -> the matching folder ("FR", "PT-BR" then "PT"), or null.
        public static string? Resolve(string root, string? language)
        {
            if (string.IsNullOrWhiteSpace(language)) return null;
            var value = language!.Trim();
            var named = Names.FirstOrDefault(n => n.Skip(1).Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)));
            var candidates = named != null ? new[] { named[0] }
                : new[] { value.Replace('_', '-'), value.Replace('_', '-').Split('-')[0] };
            var available = Available(root);
            foreach (var candidate in candidates)
            {
                var match = available.FirstOrDefault(a => string.Equals(a, candidate, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return null;
        }
    }
}
