using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using ClassicAssist.Data.Filters;
using ClassicAssist.Shared;
using ClassicAssist.UO.Network.PacketFilter;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ClassicAssist.Tests.Filters
{
    /// <summary>
    ///     <see cref="LightLevelFilter" /> blocks personal light (0x4E) and rewrites the global light level
    ///     (0x4F) to a fixed level or the server's level clamped into a min/max band, and the min/max
    ///     options push each other rather than cross. The filter's state is static; the instance is made
    ///     without its constructor so it does not subscribe to Engine.ConnectedEvent for good.
    /// </summary>
    public class LightLevelFilterLevelTests : IDisposable
    {
        private readonly LightLevelFilter _filter;
        private readonly LightLevelOptions _originalOptions;
        private readonly bool _originalEnabled;
        private readonly int _originalServerLevel;
        private static readonly FieldInfo ServerLevelField =
            typeof( LightLevelFilter ).GetField( "_serverLightLevel", BindingFlags.NonPublic | BindingFlags.Static );

        public LightLevelFilterLevelTests()
        {
            EngineState.Reset();

            _originalOptions = LightLevelFilter.FilterOptions;
            _originalEnabled = LightLevelFilter.IsEnabled;
            _originalServerLevel = (int) ServerLevelField.GetValue( null );

            _filter = (LightLevelFilter) RuntimeHelpers.GetUninitializedObject( typeof( LightLevelFilter ) );
            LightLevelFilter.FilterOptions = new LightLevelOptions();
            LightLevelFilter.IsEnabled = true;
        }

        public void Dispose()
        {
            LightLevelFilter.IsEnabled = false;
            LightLevelFilter.FilterOptions = _originalOptions;
            LightLevelFilter.IsEnabled = _originalEnabled;
            LightLevelFilter.SetServerLightLevel( _originalServerLevel );
            EngineState.Reset();
        }

        private byte Rewrite( int serverLevel )
        {
            byte[] packet = { 0x4F, (byte) serverLevel };
            int length = packet.Length;

            Assert.False( _filter.CheckPacket( ref packet, ref length, PacketDirection.Incoming ) );

            return packet[1];
        }

        [Fact]
        public void WillBlockPersonalLightAndPassOtherPacketsOnlyWhileEnabled()
        {
            byte[] personal = { 0x4E, 0, 0, 0, 1, 5 };
            int length = personal.Length;

            Assert.True( _filter.CheckPacket( ref personal, ref length, PacketDirection.Incoming ) );

            byte[] other = { 0x1D, 0, 0, 0, 1 };
            Assert.False( _filter.CheckPacket( ref other, ref length, PacketDirection.Incoming ) );

            LightLevelFilter.IsEnabled = false;
            Assert.False( _filter.CheckPacket( ref personal, ref length, PacketDirection.Incoming ) );
            Assert.Equal( 20, Rewrite( 20 ) );
        }

        [Fact]
        public void FixedModeWillReplaceTheServerLevel()
        {
            LightLevelFilter.FilterOptions.FixedLevel = 4;

            Assert.Equal( 4, Rewrite( 25 ) );
            Assert.Equal( 4, Rewrite( 0 ) );
        }

        [Fact]
        public void MinMaxModeWillClampTheServerLevelIntoTheBand()
        {
            LightLevelOptions options = LightLevelFilter.FilterOptions;
            options.Mode = LightLevelMode.MinMax;
            options.MinLevel = 5;
            options.MaxLevel = 20;

            Assert.Equal( 5, Rewrite( 0 ) );
            Assert.Equal( 12, Rewrite( 12 ) );
            Assert.Equal( 20, Rewrite( 30 ) );
        }

        [Fact]
        public void MinAndMaxWillPushEachOtherRatherThanCross()
        {
            LightLevelOptions options = new LightLevelOptions { MinLevel = 5, MaxLevel = 20 };

            options.MinLevel = 25;
            Assert.Equal( 25, options.MaxLevel );

            options.MaxLevel = 3;
            Assert.Equal( 3, options.MinLevel );

            options.MaxLevel = 100;
            options.MinLevel = -4;
            Assert.Equal( LightLevelFilter.MAX_LIGHT_LEVEL, options.MaxLevel );
            Assert.Equal( 0, options.MinLevel );
        }

        [Fact]
        public void ModeWillDriveTheIsFixedAndIsMinMaxViews()
        {
            LightLevelOptions options = new LightLevelOptions();

            Assert.True( options.IsFixed );

            options.IsMinMax = true;
            Assert.Equal( LightLevelMode.MinMax, options.Mode );
            Assert.False( options.IsFixed );

            options.IsFixed = false;
            Assert.Equal( LightLevelMode.MinMax, options.Mode );
        }

        [Fact]
        public void ChangingTheOptionsWhileConnectedWillPushTheLevelToTheClient()
        {
            LightLevelFilter.SetServerLightLevel( 30 );
            Engine.Connected = true;

            using PacketRecorder recorder = new PacketRecorder();

            LightLevelFilter.FilterOptions.Mode = LightLevelMode.MinMax;
            LightLevelFilter.FilterOptions.MaxLevel = 10;

            byte[] last = recorder.ReceivedWithId( 0x4F )[^1];
            Assert.Equal( 10, last[1] );

            LightLevelFilter.IsEnabled = false;
            int before = recorder.ReceivedWithId( 0x4F ).Count;
            LightLevelFilter.FilterOptions.MaxLevel = 15;

            Assert.Equal( before, recorder.ReceivedWithId( 0x4F ).Count );
        }

        [Fact]
        public void SerializeAndDeserializeWillRoundTripTheOptions()
        {
            LightLevelFilter.FilterOptions = new LightLevelOptions { Mode = LightLevelMode.MinMax, MinLevel = 3, MaxLevel = 18, FixedLevel = 7 };

            JObject saved = _filter.Serialize();

            _filter.ResetOptions();
            Assert.Equal( LightLevelMode.Fixed, LightLevelFilter.FilterOptions.Mode );

            _filter.Deserialize( saved );
            _filter.Deserialize( null );

            LightLevelOptions options = LightLevelFilter.FilterOptions;
            Assert.Equal( LightLevelMode.MinMax, options.Mode );
            Assert.Equal( 3, options.MinLevel );
            Assert.Equal( 18, options.MaxLevel );
            Assert.Equal( 7, options.FixedLevel );
        }

        [Fact]
        public void DeserializeWillKeepDefaultsForMissingOrBadFields()
        {
            _filter.Deserialize( JObject.Parse( "{ \"Mode\": \"Sideways\", \"FixedLevel\": 99 }" ) );

            LightLevelOptions options = LightLevelFilter.FilterOptions;
            Assert.Equal( LightLevelMode.Fixed, options.Mode );
            Assert.Equal( LightLevelFilter.MAX_LIGHT_LEVEL, options.FixedLevel );
            Assert.Equal( 0, options.MinLevel );
        }

        [Fact]
        public void LegacyLightLevelWillBecomeAFixedLevel()
        {
            LightLevelFilter.MigrateLegacyLightLevel( new JValue( 9 ) );

            Assert.Equal( LightLevelMode.Fixed, LightLevelFilter.FilterOptions.Mode );
            Assert.Equal( 9, LightLevelFilter.FilterOptions.FixedLevel );

            LightLevelFilter.MigrateLegacyLightLevel( null );

            Assert.Equal( 9, LightLevelFilter.FilterOptions.FixedLevel );
        }
    }
}
