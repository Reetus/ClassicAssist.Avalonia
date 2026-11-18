using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClassicAssist.Data;
using ClassicAssist.Shared;
using ClassicAssist.UO.Data;
using Xunit;

namespace ClassicAssist.Tests
{
    public class ClilocTests
    {
        /// <summary>
        ///     Cliloc files from client 7.0.104 onwards are BWT compressed. Read raw they decode to garbage
        ///     rather than failing outright, which is the failure this guards: strings come out wrong instead
        ///     of missing.
        /// </summary>
        [Fact]
        public void WillDecompressNewFormat()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            byte[] raw = File.ReadAllBytes( Path.Combine( TestData.UOPath, "Cliloc.enu" ) );
            byte[] decompressed = BwtDecompress.Decompress( raw );

            Assert.True( decompressed.Length > raw.Length / 2, "decompression produced almost nothing" );

            // Every cliloc file starts with this 6-byte header, so its presence means the transform ran
            // to completion rather than merely returning something.
            Assert.Equal( new byte[] { 0x02, 0x00, 0x00, 0x00, 0x01, 0x00 },
                new[]
                {
                    decompressed[0], decompressed[1], decompressed[2], decompressed[3], decompressed[4],
                    decompressed[5]
                } );
        }

        [Fact]
        public void WillLoadClilocsFromCompressedFile()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            Engine.ClientVersion = new Version( 7, 0, 106, 0 );
            Cliloc.Initialize( TestData.UOPath );

            Dictionary<int, string> items = Cliloc.GetItems();

            // A modern cliloc has well over a hundred thousand entries. An earlier attempt stopped at the
            // first zero-length string and produced 3740, which still looked plausible in isolation.
            Assert.True( items.Count > 100000, $"expected a full cliloc list, got {items.Count} entries" );

            Assert.Equal( "Reputation aversion triggered.", Cliloc.GetProperty( 500000 ) );
            Assert.Equal( "~1_NOTHING~", Cliloc.GetProperty( 1042971 ) );
            Assert.Equal( "Hair Color", Cliloc.GetProperty( 3000184 ) );
            Assert.Equal( "Greater Heal", Cliloc.GetProperty( 1015012 ) );
        }

        [Fact]
        public void WillKeepEmptyStrings()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            Engine.ClientVersion = new Version( 7, 0, 106, 0 );
            Cliloc.Initialize( TestData.UOPath );

            Dictionary<int, string> items = Cliloc.GetItems();

            int emptyCount = 0;

            foreach ( KeyValuePair<int, string> item in items )
            {
                if ( item.Value.Length == 0 )
                {
                    emptyCount++;
                }
            }

            // Not a curiosity: zero-length entries are what the parse loop used to stop on.
            Assert.True( emptyCount > 0, "expected the file to contain empty strings" );
        }

        /// <summary>
        ///     A '#' that starts no token used to satisfy the loop's Contains check forever while the pass
        ///     below it did nothing, hanging whichever thread called in. Journal and gump text come straight
        ///     from the server, so a trailing '#' in a shard's own text was enough to lock the client up.
        /// </summary>
        [Theory( Timeout = 5000 )]
        [InlineData( "#" )]
        [InlineData( "you see: #" )]
        [InlineData( "## " )]
        [InlineData( "#a" )]
        [InlineData( "# 500000" )]
        public async Task WillNotHangOnUnterminatedToken( string input )
        {
            // On a worker so the Timeout can fail the test rather than hang the run when the loop spins.
            string result = await Task.Run( () => Cliloc.GetLocalString( input ), TestContext.Current.CancellationToken );

            Assert.Equal( input, result );
        }

        [Fact]
        public void WillReloadWhenInitializedAgain()
        {
            if ( !TestData.HasUOData )
            {
                return;
            }

            Engine.ClientVersion = new Version( 7, 0, 106, 0 );
            Cliloc.Initialize( TestData.UOPath );

            int first = Cliloc.GetItems().Count;

            Cliloc.Initialize( TestData.UOPath );

            Assert.Equal( first, Cliloc.GetItems().Count ); // "Initialize should rebuild the list rather than pin the first load"
        }
    }
}
