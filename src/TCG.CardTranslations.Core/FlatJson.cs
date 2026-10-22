using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using static TCG.Shared.Lang;
namespace TCG.CardTranslations.Core
{
    // Reads a JSON object whose values are strings: { "49": "Soporifik", ... }.
    // Keys starting with '_' are comments. Values that are not strings are ignored.
    public static class FlatJson
    {
        public static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var p = new Reader(text ?? "");
            p.Skip();
            if (p.Peek() == '﻿') { p.Next(); p.Skip(); }
            p.Expect('{');
            p.Skip();
            if (p.Peek() == '}') { p.Next(); return result; }
            while (true)
            {
                p.Skip();
                var key = p.String();
                p.Skip(); p.Expect(':'); p.Skip();
                if (p.Peek() == '"')
                {
                    var value = p.String();
                    if (!key.StartsWith("_", StringComparison.Ordinal)) result[key] = value;
                }
                else p.SkipValue();
                p.Skip();
                var c = p.Next();
                if (c == '}') break;
                if (c != ',') throw p.Error(T("',' ou '}' attendu"));
            }
            p.Skip();
            if (!p.End) throw p.Error(T("texte inattendu après l'objet"));
            return result;
        }

        public static string Write(IEnumerable<KeyValuePair<string, string>> entries)
        {
            var sb = new StringBuilder("{\n");
            var first = true;
            foreach (var kv in entries)
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("  ").Append(Quote(kv.Key)).Append(": ").Append(Quote(kv.Value));
            }
            return sb.Append("\n}\n").ToString();
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        sealed class Reader
        {
            readonly string s; int i;
            public Reader(string text) { s = text; }
            public bool End => i >= s.Length;
            public char Peek() => i < s.Length ? s[i] : '\0';
            public char Next() => i < s.Length ? s[i++] : '\0';
            public FormatException Error(string what) => new FormatException(F("JSON invalide ({0}) à la position {1}.", what, i));
            public void Expect(char c) { if (Next() != c) throw Error(F("'{0}' attendu", c)); }
            public void Skip() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            public string String()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error(T("chaîne non terminée"));
                    var c = Next();
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    var e = Next();
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error(T("séquence \\u incomplète"));
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw Error(T("échappement inconnu"));
                    }
                }
            }

            public void SkipValue()
            {
                var c = Peek();
                if (c == '"') { String(); return; }
                if (c == '{' || c == '[')
                {
                    var depth = 0;
                    while (!End)
                    {
                        var d = Peek();
                        if (d == '"') { String(); continue; }
                        Next();
                        if (d == '{' || d == '[') depth++;
                        else if ((d == '}' || d == ']') && --depth == 0) return;
                    }
                    throw Error(T("objet non terminé"));
                }
                var start = i;
                while (!End && Peek() != ',' && Peek() != '}' && !char.IsWhiteSpace(Peek())) Next();
                if (i == start) throw Error(T("valeur attendue"));
            }
        }
    }
}
