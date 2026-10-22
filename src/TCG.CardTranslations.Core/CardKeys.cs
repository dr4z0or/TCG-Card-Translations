using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TCG.CardTranslations.Core
{
    // How files in an edition folder are matched to the cards of a pack.
    public static class CardKeys
    {
        // Card number taken from a sprite or file name: the text before the first '_',
        // when it starts with a digit. Leading zeros are ignored so "049.png" matches
        // "49_BS_Drowzee"; a letter suffix is kept so "50a" and "50b" stay distinct.
        public static string? Number(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var token = name.Split('_')[0].Trim().ToLowerInvariant();
            if (token.Length == 0 || !char.IsDigit(token[0])) return null;
            var digits = 0;
            while (digits < token.Length && char.IsDigit(token[digits])) digits++;
            var number = token.Substring(0, digits).TrimStart('0');
            if (number.Length == 0) number = "0";
            return number + token.Substring(digits);
        }

        public static string Exact(string name) => (name ?? "").Trim().ToLowerInvariant();

        // Path segments of a folder, compared case-insensitively, without the
        // "_prefabloader" suffix that Enhanced Prefab Loader requires on pack folders.
        public static string[] Segments(string path)
        {
            return (path ?? "").Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Segment).Where(s => s.Length > 0).ToArray();
        }

        public static string Segment(string segment)
        {
            var s = segment.Trim().ToLowerInvariant();
            const string suffix = "_prefabloader";
            if (s.EndsWith(suffix, StringComparison.Ordinal)) s = s.Substring(0, s.Length - suffix.Length).TrimEnd();
            return s;
        }

        // Folder segments of a bundle file path (the bundle file name itself is dropped).
        public static string[] BundleFolder(string bundlePath)
        {
            var segments = Segments(bundlePath);
            return segments.Length == 0 ? segments : segments.Take(segments.Length - 1).ToArray();
        }

        public static bool EndsWith(string[] whole, string[] tail)
        {
            if (tail.Length == 0 || tail.Length > whole.Length) return false;
            var offset = whole.Length - tail.Length;
            for (var i = 0; i < tail.Length; i++)
                if (!string.Equals(whole[offset + i], tail[i], StringComparison.Ordinal)) return false;
            return true;
        }
    }

    // A lookup by exact name first, then by card number when exactly one entry has it.
    public sealed class KeyedLookup<T> where T : class
    {
        readonly Dictionary<string, T> exact = new Dictionary<string, T>(StringComparer.Ordinal);
        // Card number -> exact key, or null when several entries share the number.
        readonly Dictionary<string, string?> byNumber = new Dictionary<string, string?>(StringComparer.Ordinal);
        readonly List<string> duplicates = new List<string>();

        public int Count => exact.Count;
        public IReadOnlyList<string> AmbiguousNumbers => duplicates;
        public IEnumerable<string> Keys => exact.Keys;

        public void Add(string name, T value)
        {
            var key = CardKeys.Exact(name);
            exact[key] = value;
            var number = CardKeys.Number(name);
            if (number == null) return;
            if (byNumber.TryGetValue(number, out var existing))
            {
                if (existing != null && existing != key) { duplicates.Add(number); byNumber[number] = null; }
            }
            else byNumber[number] = key;
        }

        string? KeyFor(string cardName)
        {
            var key = CardKeys.Exact(cardName);
            if (exact.ContainsKey(key)) return key;
            var number = CardKeys.Number(cardName);
            return number != null && byNumber.TryGetValue(number, out var byNum) ? byNum : null;
        }

        // Exact name only: for assets that are not card faces (card backs, foil masks), whose
        // names may start with the card number too.
        public T? FindExact(string assetName) => exact.TryGetValue(CardKeys.Exact(assetName), out var v) ? v : null;

        public T? Find(string cardName)
        {
            var key = KeyFor(cardName);
            return key != null ? exact[key] : null;
        }

        // Entries that matched none of the given card names.
        public IEnumerable<string> Unused(IEnumerable<string> cardNames)
        {
            var used = new HashSet<string>(cardNames.Select(KeyFor).Where(k => k != null).Select(k => k!), StringComparer.Ordinal);
            return exact.Keys.Where(k => !used.Contains(k)).OrderBy(k => k, StringComparer.Ordinal);
        }
    }

    internal static class PathText
    {
        public static string Relative(string root, string path)
        {
            var r = Path.GetFullPath(root).TrimEnd('\\', '/');
            var p = Path.GetFullPath(path);
            return p.Length > r.Length && p.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? p.Substring(r.Length).TrimStart('\\', '/') : p;
        }
    }
}
