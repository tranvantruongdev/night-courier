using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace NightCourier.Core.Tests
{
    /// <summary>
    /// The shipped strings.csv is valid, and everything the player reads has a Vietnamese and a Japanese row: every
    /// literal handed to UiFactory (which localizes all its labels), to Loc/Localize/Radio, the card and garage text,
    /// dispatch's radio lines, the bike names and the template's Settings labels. dotnet only (it reads repo files).
    /// </summary>
    public class StringsFileTests
    {
        /// <summary>Placeholders the code overwrites before anyone sees them.</summary>
        private static readonly HashSet<string> Placeholders = new HashSet<string> { "0:00", "Lv 1", "Card", "Buy", "II", "" };

        private static string Root()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Assets", "_Game", "Resources", "strings.csv")))
            {
                dir = dir.Parent;
            }

            Assert.IsNotNull(dir, "Assets/_Game/Resources/strings.csv not found above the test folder");
            return dir.FullName;
        }

        private static IEnumerable<string> Literals(string code, string pattern) =>
            Regex.Matches(code, pattern, RegexOptions.Multiline).Cast<Match>()
                .SelectMany(m => m.Groups.Cast<Group>().Skip(1)).Where(g => g.Success).Select(g => Regex.Unescape(g.Value));

        [Test]
        public void Every_text_on_screen_has_vietnamese_and_japanese()
        {
            string root = Root();
            var strings = Strings.Parse(File.ReadAllText(Path.Combine(root, "Assets", "_Game", "Resources", "strings.csv")));
            CollectionAssert.IsEmpty(strings.Validate());

            var texts = new HashSet<string>();
            const string literal = @"""((?:[^""\\]|\\.)*)""";
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Assets", "_Game", "Scripts", "Runtime"), "*.cs", SearchOption.AllDirectories))
            {
                string code = File.ReadAllText(file);
                texts.UnionWith(Literals(code, @"Create(?:Text|Button|Slider|Toggle)\([^,()]+, " + literal));
                texts.UnionWith(Literals(code, @"(?:Panel|\bButton)\([^,()]+, " + literal)); // the HUD's own helpers
                texts.UnionWith(Literals(code, @"\b(?:Loc|Localize|Radio|ShowRadio)\(\s*" + literal));
                texts.UnionWith(Literals(code, @"ShowResults\(won \? " + literal + " : " + literal));
                texts.UnionWith(Literals(code, @"=> " + literal + ","));       // switch arms: card text, garage rows, bikes
                texts.UnionWith(Literals(code, @"^\s*" + literal + @",\s*$"));  // array entries: dispatch's radio lines
                texts.UnionWith(Literals(code, @"\? " + literal + @"\s*$"));    // the Freight Hauler's radio line
            }

            string settings = File.ReadAllText(Path.Combine(root, "Assets", "_Project", "Scripts", "Runtime", "UI", "SettingsPanelView.cs"));
            texts.UnionWith(Literals(settings, @"Create(?:Text|Slider|Toggle|Button)\(card, " + literal));
            texts.UnionWith(Enum.GetNames(typeof(BikeModel)));
            texts.ExceptWith(Placeholders);

            Assert.Greater(texts.Count, 60, "the scan found the game's texts");
            foreach (var language in new[] { "vi", "ja" })
            {
                var missing = texts.Where(t => !strings.Has(t, language)).OrderBy(t => t, StringComparer.Ordinal).ToList();
                TestContext.WriteLine($"missing {language}:\n" + string.Join("\n", missing)); // NUnit's message cuts the list at 10
                CollectionAssert.IsEmpty(missing, $"strings.csv has no {language} for these");
            }
        }
    }
}
