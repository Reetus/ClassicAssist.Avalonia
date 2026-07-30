using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClassicAssist.Data;
using ClassicAssist.Shared.Resources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassicAssist.Tests
{
    [TestClass]
    public class LanguageTests
    {
        /// <summary>
        ///     The cultures that ship as satellite resources. Kept next to the test rather than derived from
        ///     the enum, so that adding one without the other fails here instead of silently shipping a
        ///     dropdown entry that falls back to English.
        /// </summary>
        private static readonly Dictionary<Language, string> _expectedCultures = new Dictionary<Language, string>
        {
            { Language.English, "en-US" },
            { Language.Korean, "ko-KR" },
            { Language.Chinese, "zh" },
            { Language.Italian, "it-IT" },
            { Language.Polish, "pl-PL" },
            { Language.Czech, "cs-CZ" }
        };

        [TestMethod]
        public void WillMapEveryLanguageToACulture()
        {
            CultureInfo original = CultureInfo.CurrentUICulture;

            try
            {
                foreach ( Language language in Enum.GetValues( typeof( Language ) ).Cast<Language>() )
                {
                    // SetLanguage throws ArgumentOutOfRangeException for anything it does not handle, so a
                    // value added to the enum and forgotten here fails rather than falling through.
                    AssistantOptions.SetLanguage( language );

                    if ( language == Language.Default )
                    {
                        continue;
                    }

                    Assert.IsTrue( _expectedCultures.ContainsKey( language ),
                        $"{language} is selectable but this test does not know its culture" );

                    Assert.AreEqual( _expectedCultures[language], CultureInfo.CurrentUICulture.Name );
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
                CultureInfo.CurrentUICulture = original;
                CultureInfo.DefaultThreadCurrentUICulture = original;
            }
        }

        /// <summary>
        ///     The point of the dropdown is that picking a language changes the text. This asserts the actual
        ///     translated value rather than merely that a resource set exists - a satellite can be present and
        ///     still be bypassed if the culture mapping is wrong.
        /// </summary>
        [TestMethod]
        public void WillTranslateForEveryLanguage()
        {
            Dictionary<Language, string> expectedOptions = new Dictionary<Language, string>
            {
                { Language.English, "Options" },
                { Language.Korean, "\uc635\uc158" },
                { Language.Chinese, "\u9009\u4ef6" },
                { Language.Italian, "Opzioni" },
                { Language.Polish, "Opcje" },
                { Language.Czech, "Nastaven\u00ed" }
            };

            CultureInfo original = CultureInfo.CurrentUICulture;

            try
            {
                foreach ( KeyValuePair<Language, string> expected in expectedOptions )
                {
                    AssistantOptions.SetLanguage( expected.Key );

                    Assert.AreEqual( expected.Value, Strings.Options,
                        $"{expected.Key} did not resolve its translation" );
                }

                // Every selectable language needs an entry above, or it could regress unnoticed.
                foreach ( Language language in Enum.GetValues( typeof( Language ) ).Cast<Language>() )
                {
                    if ( language != Language.Default )
                    {
                        Assert.IsTrue( expectedOptions.ContainsKey( language ),
                            $"{language} is selectable but has no expected translation here" );
                    }
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
                CultureInfo.CurrentUICulture = original;
                CultureInfo.DefaultThreadCurrentUICulture = original;
            }
        }

    }
}
