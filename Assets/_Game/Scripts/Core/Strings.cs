using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Template.Core.Data;

namespace NightCourier.Core
{
    /// <summary>
    /// UI text by language, read from strings.csv (columns en, vi, ja) and keyed by the English, so code reads
    /// <c>Loc.T("Retry")</c>. Format strings keep their {0} holes in every language.
    /// </summary>
    public sealed class Strings
    {
        public static readonly string[] Languages = { "en", "vi", "ja" };

        private readonly Dictionary<string, Dictionary<string, string>> _byLanguage = new Dictionary<string, Dictionary<string, string>>();
        private readonly List<string> _problems = new List<string>();

        public static Strings Parse(string csv)
        {
            var strings = new Strings();
            foreach (var language in Languages.Skip(1))
            {
                strings._byLanguage[language] = new Dictionary<string, string>();
            }

            foreach (var row in CsvTable.Parse(csv, "strings.csv").Rows)
            {
                string english = row.Get("en");
                foreach (var language in Languages.Skip(1))
                {
                    var table = strings._byLanguage[language];
                    if (table.ContainsKey(english))
                    {
                        strings._problems.Add($"strings: '{english}' appears twice");
                        break;
                    }

                    table[english] = row.GetOrDefault(language);
                }
            }

            return strings;
        }

        /// <summary><paramref name="english"/> in <paramref name="language"/>; the English itself when there's no translation.</summary>
        public string Translate(string english, string language) =>
            english != null && _byLanguage.TryGetValue(language ?? "", out var table) && table.TryGetValue(english, out var text) && text.Length > 0
                ? text
                : english;

        public bool Has(string english, string language) =>
            _byLanguage.TryGetValue(language, out var table) && table.TryGetValue(english, out var text) && text.Length > 0;

        /// <summary>Everything wrong with the table: duplicates, missing translations, translations that lose a {0}.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>(_problems.Distinct());
            foreach (var language in _byLanguage)
            {
                foreach (var entry in language.Value)
                {
                    if (entry.Value.Length == 0)
                    {
                        problems.Add($"strings: '{entry.Key}' has no {language.Key}");
                    }
                    else if (Holes(entry.Value) != Holes(entry.Key))
                    {
                        problems.Add($"strings: '{entry.Key}' in {language.Key} must keep the same {{0}} holes");
                    }
                }
            }

            return problems;
        }

        private static string Holes(string text) =>
            string.Concat(Regex.Matches(text, @"\{\d+(:[^}]*)?\}").Cast<Match>().Select(m => m.Value).OrderBy(h => h, StringComparer.Ordinal));
    }
}
