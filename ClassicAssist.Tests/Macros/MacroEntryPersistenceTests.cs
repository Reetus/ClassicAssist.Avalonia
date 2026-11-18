using System;
using System.IO;
using ClassicAssist.Data.Macros;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.Macros
{
    /// <summary>
    ///     How a <see cref="MacroEntry" /> is saved to and loaded from the profile, including macros
    ///     backed by a .py file. The file-backed rules decide which copy of a macro wins when the file and
    ///     the profile disagree; getting them wrong silently loses the user's latest edit or overwrites
    ///     their file with an empty one. Global and per-profile aliases use two different JSON shapes for
    ///     backwards compatibility, and both must still load. Name uniqueness is checked against the
    ///     manager's list.
    /// </summary>
    public class MacroEntryPersistenceTests : IDisposable
    {
        private readonly MacroHarness _harness = new MacroHarness();
        private readonly string _directory = Path.Combine( Path.GetTempPath(), "CAMacroEntry" + Guid.NewGuid().ToString( "N" ) );

        public MacroEntryPersistenceTests()
        {
            Directory.CreateDirectory( _directory );
        }

        public void Dispose()
        {
            _harness.Dispose();

            try
            {
                Directory.Delete( _directory, true );
            }
            catch ( IOException )
            {
                // Best effort.
            }
        }

        [Fact]
        public void WillRoundTripAProfileMacro()
        {
            MacroEntry original = new MacroEntry
            {
                Name = "RoundTrip",
                Macro = "Msg('hi')",
                Loop = true,
                DoNotAutoInterrupt = true,
                IsBackground = true,
                IsAutostart = true,
                PassToUO = false,
                Disableable = false
            };

            original.Aliases.Add( "target", 0x1234 );
            original.Metadata["PublicId"] = "abc";
            original.Breakpoints.Add( 4 );

            JObject json = original.ToJObject();

            // A second entry with the same name would be renamed; the original is not in the manager.
            MacroEntry loaded = new MacroEntry( json );

            Assert.Equal( original.Id, loaded.Id );
            Assert.Equal( "RoundTrip", loaded.Name );
            Assert.Equal( "Msg('hi')", loaded.Macro );
            Assert.True( loaded.Loop );
            Assert.True( loaded.DoNotAutoInterrupt );
            Assert.True( loaded.IsBackground );
            Assert.True( loaded.IsAutostart );
            Assert.False( loaded.PassToUO );
            Assert.False( loaded.Disableable );
            Assert.False( loaded.IsFileBacked );
            Assert.Equal( 0x1234, loaded.Aliases["target"] );
            Assert.Equal( "abc", loaded.Metadata["PublicId"] );
            Assert.Equal( new[] { 4 }, loaded.Breakpoints );
        }

        /// <summary>
        ///     Global macros write their aliases as plain properties (the old format); per-profile ones as
        ///     a Key/Value array. Each must load back.
        /// </summary>
        [Fact]
        public void WillRoundTripGlobalMacroAliasesInTheLegacyShape()
        {
            MacroEntry original = new MacroEntry { Name = "Global", Global = true };
            original.Aliases.Add( "mount", 0x2000 );

            JObject json = original.ToJObject();

            Assert.Equal( JTokenType.Object, json["Aliases"].Type );
            Assert.Equal( 0x2000, json["Aliases"]["mount"].ToObject<int>() );

            MacroEntry loaded = new MacroEntry( json );

            Assert.True( loaded.Global );
            Assert.Equal( 0x2000, loaded.Aliases["mount"] );
        }

        [Fact]
        public void WillSkipAliasEntriesWithNoName()
        {
            JObject json = new JObject
            {
                { "Name", "Aliases" },
                {
                    "Aliases",
                    new JArray { new JObject { { "Key", "" }, { "Value", 1 } }, new JObject { { "Key", "ok" }, { "Value", 2 } } }
                }
            };

            MacroEntry loaded = new MacroEntry( json );

            Assert.Equal( 2, Assert.Single( loaded.Aliases ).Value );
        }

        [Fact]
        public void WillUseDefaultsForMissingFields()
        {
            MacroEntry loaded = new MacroEntry( new JObject { { "Name", "Sparse" } } );

            Assert.Equal( string.Empty, loaded.Macro );
            Assert.True( loaded.PassToUO );
            Assert.True( loaded.Disableable );
            Assert.False( loaded.Loop );
            Assert.False( string.IsNullOrEmpty( loaded.Id ) );
        }

        [Fact]
        public void WillLoadAFileBackedMacroFromItsFile()
        {
            string path = Path.Combine( _directory, "fromfile.py" );
            File.WriteAllText( path, "SetAlias('x', 1)" );

            MacroEntry loaded = new MacroEntry( new JObject { { "Name", "File" }, { "FilePath", path }, { "Macro", "" } } );

            Assert.True( loaded.IsFileBacked );
            Assert.Equal( "SetAlias('x', 1)", loaded.Macro );
            Assert.False( loaded.BackingFileWritePending );
            Assert.False( loaded.BackingFileReadFailed );
        }

        /// <summary>
        ///     Content embedded in the profile next to a FilePath means the last save could not write the
        ///     file. That content is newer than the file and must win, and the write must be retried.
        /// </summary>
        [Fact]
        public void WillPreferContentEmbeddedAfterAFailedFileWrite()
        {
            string path = Path.Combine( _directory, "stale.py" );
            File.WriteAllText( path, "old content" );

            MacroEntry loaded = new MacroEntry( new JObject
            {
                { "Name", "Pending" }, { "FilePath", path }, { "Macro", "new content" }
            } );

            Assert.Equal( "new content", loaded.Macro );
            Assert.True( loaded.BackingFileWritePending );

            // And it keeps embedding the content until the file write succeeds.
            Assert.Equal( "new content", loaded.ToJObject()["Macro"].ToObject<string>() );
        }

        [Fact]
        public void WontEmbedAFileBackedMacrosContentInTheProfile()
        {
            string path = Path.Combine( _directory, "clean.py" );
            File.WriteAllText( path, "content" );

            MacroEntry loaded = new MacroEntry( new JObject { { "Name", "Clean" }, { "FilePath", path } } );

            JObject json = loaded.ToJObject();

            Assert.Equal( string.Empty, json["Macro"].ToObject<string>() );
            Assert.Equal( path, json["FilePath"].ToObject<string>() );
        }

        [Fact]
        public void WillLoadEmptyWhenTheBackingFileIsMissing()
        {
            string path = Path.Combine( _directory, "missing.py" );

            MacroEntry loaded = new MacroEntry( new JObject { { "Name", "Missing" }, { "FilePath", path } } );

            Assert.True( loaded.IsFileBacked );
            Assert.Equal( string.Empty, loaded.Macro );
        }

        /// <summary>
        ///     A locked or unreadable file must not take the whole profile load down, and must be flagged
        ///     so the next save does not write the (empty) loaded content over it.
        /// </summary>
        [Fact]
        public void WillFlagAnUnreadableBackingFile()
        {
            string path = Path.Combine( _directory, "locked.py" );
            File.WriteAllText( path, "content" );

            MacroEntry loaded;

            using ( new FileStream( path, FileMode.Open, FileAccess.ReadWrite, FileShare.None ) )
            {
                loaded = new MacroEntry( new JObject { { "Name", "Locked" }, { "FilePath", path } } );
            }

            Assert.True( loaded.BackingFileReadFailed );
            Assert.Equal( string.Empty, loaded.Macro );
        }

        [Fact]
        public void WillSuffixANewMacroWhoseNameIsTaken()
        {
            _harness.Add( "Taken", string.Empty );

            MacroEntry second = new MacroEntry { Name = "Taken" };

            Assert.Equal( "Taken-", second.Name );
        }

        [Fact]
        public void WontRenameAMacroToATakenName()
        {
            _harness.Add( "Taken", string.Empty );
            MacroEntry other = _harness.Add( "Other", string.Empty );

            other.Name = "Taken";

            Assert.Equal( "Other", other.Name );
        }

        [Fact]
        public void HashFollowsTheMacroText()
        {
            MacroEntry entry = new MacroEntry { Macro = "a" };
            string first = entry.Hash;

            entry.Macro = "b";

            Assert.NotEqual( first, entry.Hash );

            entry.Macro = "a";

            Assert.Equal( first, entry.Hash );
        }
    }
}
