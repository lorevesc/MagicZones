using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MagicZones
{
    /// <summary>
    /// Tiny JSON reader/writer. Objects map to Dictionary&lt;string, object&gt;, arrays to
    /// List&lt;object&gt;, numbers to double. Enough for a human-editable config file.
    /// </summary>
    internal static class Json
    {
        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.SkipWs();
            var value = p.ReadValue();
            p.SkipWs();
            if (!p.AtEnd) throw p.Error("contenuto extra dopo il valore JSON");
            return value;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        // ---- Reading helpers used by the config loader ------------------------------
        public static T Get<T>(Dictionary<string, object> obj, string key, T fallback)
        {
            if (obj == null || !obj.TryGetValue(key, out var v) || v == null) return fallback;
            try
            {
                if (typeof(T) == typeof(int)) return (T)(object)Convert.ToInt32(v, CultureInfo.InvariantCulture);
                if (typeof(T) == typeof(double)) return (T)(object)Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (v is T t) return t;
            }
            catch { }
            return fallback;
        }

        // ---- Writer ------------------------------------------------------------------
        private static void WriteValue(StringBuilder sb, object v, int indent)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: sb.Append(FormatNumber(d)); break;
                case float f: sb.Append(FormatNumber(f)); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> dict: WriteObject(sb, dict, indent); break;
                case IEnumerable list: WriteArray(sb, list, indent); break;
                default: WriteString(sb, v.ToString()); break;
            }
        }

        private static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            return Math.Round(d, 5).ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> dict, int indent)
        {
            if (dict.Count == 0) { sb.Append("{}"); return; }
            // Small flat objects (like zones) stay on one line for readability.
            bool inline = dict.Count <= 6 && IsFlat(dict.Values);
            sb.Append(inline ? "{ " : "{\n");
            int n = 0;
            foreach (var kv in dict)
            {
                if (!inline) sb.Append(' ', (indent + 1) * 2);
                WriteString(sb, kv.Key);
                sb.Append(": ");
                WriteValue(sb, kv.Value, indent + 1);
                if (++n < dict.Count) sb.Append(inline ? ", " : ",\n");
            }
            if (inline) sb.Append(" }");
            else { sb.Append('\n'); sb.Append(' ', indent * 2); sb.Append('}'); }
        }

        private static void WriteArray(StringBuilder sb, IEnumerable list, int indent)
        {
            var items = new List<object>();
            foreach (var o in list) items.Add(o);
            if (items.Count == 0) { sb.Append("[]"); return; }
            bool inline = IsFlat(items);
            sb.Append(inline ? "[" : "[\n");
            for (int i = 0; i < items.Count; i++)
            {
                if (!inline) sb.Append(' ', (indent + 1) * 2);
                WriteValue(sb, items[i], indent + 1);
                if (i < items.Count - 1) sb.Append(inline ? ", " : ",\n");
            }
            if (inline) sb.Append(']');
            else { sb.Append('\n'); sb.Append(' ', indent * 2); sb.Append(']'); }
        }

        private static bool IsFlat(IEnumerable values)
        {
            foreach (var v in values)
                if (v is IDictionary<string, object> || (v is IEnumerable && !(v is string))) return false;
            return true;
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- Parser ------------------------------------------------------------------
        private sealed class Parser
        {
            private readonly string s;
            private int i;

            public Parser(string text) { s = text ?? ""; }

            public bool AtEnd => i >= s.Length;

            public FormatException Error(string msg) => new FormatException($"JSON non valido (pos {i}): {msg}");

            public void SkipWs()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (char.IsWhiteSpace(c) || c == '\uFEFF') { i++; continue; }
                    // Allow // comments so people can annotate their config.
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                    {
                        while (i < s.Length && s[i] != '\n') i++;
                        continue;
                    }
                    break;
                }
            }

            public object ReadValue()
            {
                if (AtEnd) throw Error("fine inattesa");
                char c = s[i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't' && Match("true")) return true;
                if (c == 'f' && Match("false")) return false;
                if (c == 'n' && Match("null")) return null;
                if (c == '-' || char.IsDigit(c)) return ReadNumber();
                throw Error($"carattere inatteso '{c}'");
            }

            private bool Match(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
                i += word.Length;
                return true;
            }

            private Dictionary<string, object> ReadObject()
            {
                var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                i++;
                SkipWs();
                if (i < s.Length && s[i] == '}') { i++; return obj; }
                while (true)
                {
                    SkipWs();
                    if (AtEnd || s[i] != '"') throw Error("attesa chiave");
                    string key = ReadString();
                    SkipWs();
                    if (AtEnd || s[i] != ':') throw Error("atteso ':'");
                    i++;
                    SkipWs();
                    obj[key] = ReadValue();
                    SkipWs();
                    if (AtEnd) throw Error("oggetto non chiuso");
                    if (s[i] == ',') { i++; SkipWs(); if (i < s.Length && s[i] == '}') { i++; return obj; } continue; }
                    if (s[i] == '}') { i++; return obj; }
                    throw Error("atteso ',' o '}'");
                }
            }

            private List<object> ReadArray()
            {
                var list = new List<object>();
                i++;
                SkipWs();
                if (i < s.Length && s[i] == ']') { i++; return list; }
                while (true)
                {
                    SkipWs();
                    list.Add(ReadValue());
                    SkipWs();
                    if (AtEnd) throw Error("array non chiuso");
                    if (s[i] == ',') { i++; SkipWs(); if (i < s.Length && s[i] == ']') { i++; return list; } continue; }
                    if (s[i] == ']') { i++; return list; }
                    throw Error("atteso ',' o ']'");
                }
            }

            private string ReadString()
            {
                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (AtEnd) throw Error("stringa non chiusa");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("escape incompleto");
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error("escape \\u incompleto");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
            }

            private double ReadNumber()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && "0123456789.eE+-".IndexOf(s[i]) >= 0) i++;
                if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    throw Error("numero non valido");
                return d;
            }
        }
    }
}
