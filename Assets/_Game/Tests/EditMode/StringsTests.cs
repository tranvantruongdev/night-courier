using System.Linq;
using NUnit.Framework;

namespace NightCourier.Core.Tests
{
    public class StringsTests
    {
        [Test]
        public void Text_translates_by_its_english_and_falls_back_to_it()
        {
            var strings = Strings.Parse("en,vi,ja\nRetry,Thử lại,リトライ\n\"Best {0}\",Kỷ lục {0},ベスト {0}");
            Assert.AreEqual(("Retry", "Thử lại", "リトライ"), (strings.Translate("Retry", "en"), strings.Translate("Retry", "vi"), strings.Translate("Retry", "ja")));
            Assert.AreEqual("ベスト {0}", strings.Translate("Best {0}", "ja"));
            Assert.AreEqual("Not in the table", strings.Translate("Not in the table", "ja"), "unknown text stays English");
            Assert.AreEqual("Retry", strings.Translate("Retry", "fr"), "unknown language stays English");
            CollectionAssert.IsEmpty(strings.Validate());
        }

        [Test]
        public void Strings_must_be_translated_keep_their_holes_and_appear_once()
        {
            var strings = Strings.Parse("en,vi,ja\nRetry,,リトライ\n\"Best {0}\",Kỷ lục,ベスト {0}\nRetry,Thử lại,リトライ");
            CollectionAssert.AreEquivalent(new[]
            {
                "strings: 'Retry' appears twice",
                "strings: 'Retry' has no vi",
                "strings: 'Best {0}' in vi must keep the same {0} holes",
            }, strings.Validate().ToList());
        }
    }
}
