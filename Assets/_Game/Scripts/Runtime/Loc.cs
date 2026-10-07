using NightCourier.Core;
using Template.Infra;
using Template.Infra.Settings;
using Template.UI;
using TMPro;
using UnityEngine;

namespace NightCourier
{
    /// <summary>
    /// UI text in the player's language (Settings → Language), from Resources/strings.csv, where the English is the key.
    /// Every label made through UiFactory already passes through <see cref="T"/>; Japanese glyphs come from M PLUS
    /// Rounded 1c, added at startup as a dynamic fallback font.
    /// </summary>
    public static class Loc
    {
        private static Strings _strings;

        public static Strings Table
        {
            get
            {
                if (_strings == null)
                {
                    var file = Resources.Load<TextAsset>("strings");
                    _strings = Strings.Parse(file != null ? file.text : "en,vi,ja\n");
                }

                return _strings;
            }
        }

        public static string Language => Services.TryGet<SettingsService>(out var settings) ? settings.Current.language : "en";

        public static string T(string english) => Table.Translate(english, Language);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            UiFactory.Localize = T; // every UiFactory label, the template's Settings included
            SettingsPanelView.Languages = new[] { ("en", "English"), ("vi", "Tiếng Việt"), ("ja", "日本語") };

            // Japanese: a dynamic atlas filled on demand from the TTF. Added to TMP's global fallback list in memory only
            // (never marked dirty), so the TMP Settings asset on disk doesn't change.
            var ttf = Resources.Load<Font>("MPLUSRounded1c-Bold");
            if (ttf != null && TMP_Settings.instance != null)
            {
                var japanese = TMP_FontAsset.CreateFontAsset(ttf);
                japanese.name = "M PLUS Rounded 1c (dynamic)";
                var fallbacks = TMP_Settings.fallbackFontAssets;
                if (fallbacks != null && !fallbacks.Exists(f => f != null && f.name == japanese.name))
                {
                    fallbacks.Add(japanese);
                }
            }
        }
    }
}
