using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ClassicAssist.Data;
using ClassicAssist.Misc;
using ClassicAssist.Shared.Resources;
using ClassicAssist.UI.Misc;
using ClassicAssist.UI.ViewModels;
using ClassicAssist.UO.Network.PacketFilter;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Shared.UI.ViewModels
{
    public class DebugViewModel : BaseViewModel, ISettingProvider
    {
        private ICommand _changePacketEnabledCommand;
        private ICommand _clearCommand;
        private ICommand _exportLogCommand;
        private ObservableCollection<PacketEntry> _items = new ObservableCollection<PacketEntry>();

        private ObservableCollection<PacketEnabledEntry>
            _packetEntries = new ObservableCollection<PacketEnabledEntry>();

        /// <summary>
        ///     Per-packet-id enable flags, mirroring <see cref="PacketEntries" />. The collection is what
        ///     the UI binds to; this array is what the packet handlers read, because those run on the
        ///     network hot path and a LINQ scan of 256 entries per packet is not free.
        /// </summary>
        private readonly bool[] _packetEnabled = new bool[256];

        private PacketDirection _direction = PacketDirection.Any;
        private bool _enabled;
        private bool _includeInternal = true;
        private PacketEntry _selectedItem;
        private bool _topmost = true;
        private ICommand _viewPlayerEquipmentCommand;

        public DebugViewModel()
        {
            PacketEntries.Add( new PacketEnabledEntry { Name = Strings.All_Packets, PacketID = -1, Enabled = true } );

            for ( byte i = 0; i < 0xFF; i++ )
            {
                // 0x73 is the client/server ping - on by default it drowns out everything else.
                bool enabled = i != 0x73;

                PacketEntries.Add( new PacketEnabledEntry { Name = $"0x{i:x2}", PacketID = i, Enabled = enabled } );
                _packetEnabled[i] = enabled;
            }

            foreach ( PacketEnabledEntry entry in PacketEntries )
            {
                entry.PropertyChanged += OnPacketEntryPropertyChanged;
            }

            Queue = new ThreadQueue<PacketEntry>( ProcessQueue );
            Engine.PacketReceivedEvent += OnPacketReceivedEvent;
            Engine.PacketSentEvent += OnPacketSentEvent;
            Engine.InternalPacketReceivedEvent += OnInternalPacketReceivedEvent;
            Engine.InternalPacketSentEvent += OnInternalPacketSentEvent;
        }

        public ICommand ChangePacketEnabledCommand =>
            _changePacketEnabledCommand ??
            ( _changePacketEnabledCommand = new RelayCommand( EnableDisable, o => true ) );

        public ICommand ClearCommand => _clearCommand ?? ( _clearCommand = new RelayCommand( Clear, o => true ) );

        public ICommand ExportLogCommand =>
            _exportLogCommand ?? ( _exportLogCommand = new RelayCommand( ExportLog, o => true ) );

        public ObservableCollection<PacketEntry> Items
        {
            get => _items;
            set => SetProperty( ref _items, value );
        }

        public ObservableCollection<PacketEnabledEntry> PacketEntries
        {
            get => _packetEntries;
            set => SetProperty( ref _packetEntries, value );
        }

        public ThreadQueue<PacketEntry> Queue { get; set; }

        /// <summary>
        ///     Whether packets are being captured. Named and defaulted to match WPF: capture is opt-in,
        ///     because leaving it on grows <see cref="Items" /> without bound for the whole session.
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set => SetProperty( ref _enabled, value );
        }

        /// <summary>Include packets the assistant itself injected, not just real client/server traffic.</summary>
        public bool IncludeInternal
        {
            get => _includeInternal;
            set => SetProperty( ref _includeInternal, value );
        }

        /// <summary>Restrict capture to one direction; <see cref="PacketDirection.Any" /> captures both.</summary>
        public PacketDirection Direction
        {
            get => _direction;
            set => SetProperty( ref _direction, value );
        }

        public PacketEntry SelectedItem
        {
            get => _selectedItem;
            set => SetProperty( ref _selectedItem, value );
        }

        public bool Topmost
        {
            get => _topmost;
            set => SetProperty( ref _topmost, value );
        }

        public ICommand ViewPlayerEquipmentCommand =>
            _viewPlayerEquipmentCommand ??
            ( _viewPlayerEquipmentCommand = new RelayCommand( ViewPlayerEquipment, o => true ) );

        public void Serialize( JObject json )
        {
            if ( json == null )
            {
                return;
            }

            JObject options = new JObject
            {
                { "Enabled", Enabled },
                { "IncludeInternal", IncludeInternal },
                { "Direction", Direction.ToString() },
                { "Topmost", Topmost }
            };

            JArray packets = new JArray();

            foreach ( PacketEnabledEntry packetEntry in PacketEntries )
            {
                packets.Add( new JObject
                {
                    { "PacketID", packetEntry.PacketID }, { "Enabled", packetEntry.Enabled }
                } );
            }

            options.Add( "Packets", packets );

            json.Add( "Packets", options );
        }

        public void Deserialize( JObject json, Options options )
        {
            JToken config = json?["Packets"];

            if ( config == null )
            {
                return;
            }

            Enabled = config["Enabled"]?.ToObject<bool>() ?? false;
            IncludeInternal = config["IncludeInternal"]?.ToObject<bool>() ?? true;
            Topmost = config["Topmost"]?.ToObject<bool>() ?? true;

            Direction = Enum.TryParse( config["Direction"]?.ToObject<string>() ?? string.Empty,
                out PacketDirection direction )
                ? direction
                : PacketDirection.Any;

            if ( config["Packets"] == null )
            {
                return;
            }

            foreach ( JToken packet in config["Packets"] )
            {
                if ( packet["PacketID"] == null || packet["Enabled"] == null )
                {
                    continue;
                }

                int packetId = packet["PacketID"].ToObject<int>();

                PacketEnabledEntry entry = PacketEntries.FirstOrDefault( e => e.PacketID == packetId );

                if ( entry != null )
                {
                    // Setting this raises PropertyChanged, which keeps _packetEnabled in step.
                    entry.Enabled = packet["Enabled"].ToObject<bool>();
                }
            }
        }

        private void ExportLog( object obj )
        {
            if ( !( obj is IEnumerable<PacketEntry> items ) )
            {
            }

            //TODO UI
            //SaveFileDialog sfd = new SaveFileDialog
            //{
            //    InitialDirectory = Engine.StartupPath ?? Environment.CurrentDirectory,
            //    Filter = "JSON Packet Log|*.packets.json",
            //    FileName = "export.packets.json"
            //};

            //bool? result = sfd.ShowDialog();

            //if ( !result.HasValue || !result.Value || string.IsNullOrEmpty( sfd.FileName ) )
            //{
            //    return;
            //}

            //string fileName = sfd.FileName;

            //JArray jArray = new JArray();

            //foreach ( PacketEntry packetEntry in items )
            //{
            //    jArray.Add( new JObject
            //    {
            //        { "Title", packetEntry.Title },
            //        { "DateTime", packetEntry.DateTime },
            //        { "Direction", packetEntry.Direction.ToString() },
            //        { "Length", packetEntry.Length },
            //        { "Data", packetEntry.Data.Aggregate( string.Empty, ( current, b ) => current + $"{b:x2} " ) },
            //        { "Base64", Convert.ToBase64String( packetEntry.Data ) }
            //    } );
            //}

            //File.WriteAllText( fileName, jArray.ToString() );
        }

        private void ProcessQueue( PacketEntry entry )
        {
            _dispatcher.Invoke( () => { Items.Add( entry ); } );
        }

        /// <summary>
        ///     Single gate for all four capture events: the master toggle, the direction filter and the
        ///     per-packet-id flags. WPF checks the same three, spread across each handler.
        /// </summary>
        private bool ShouldCapture( byte[] data, PacketDirection direction, bool internalPacket )
        {
            if ( !Enabled || data == null || data.Length == 0 )
            {
                return false;
            }

            if ( internalPacket && !IncludeInternal )
            {
                return false;
            }

            if ( Direction != PacketDirection.Any && Direction != direction )
            {
                return false;
            }

            return _packetEnabled[data[0]];
        }

        private void Capture( byte[] data, PacketDirection direction, bool internalPacket, string title )
        {
            if ( !ShouldCapture( data, direction, internalPacket ) )
            {
                return;
            }

            Queue.Enqueue( new PacketEntry { Title = title, Data = data, Direction = direction } );
        }

        private void OnInternalPacketSentEvent( byte[] data, int length )
        {
            Capture( data, PacketDirection.Outgoing, true, "Internal Outgoing Packet" );
        }

        private void OnInternalPacketReceivedEvent( byte[] data, int length )
        {
            Capture( data, PacketDirection.Incoming, true, "Internal Incoming Packet" );
        }

        private void OnPacketSentEvent( byte[] data, int length )
        {
            Capture( data, PacketDirection.Outgoing, false, "Outgoing Packet" );
        }

        private void OnPacketReceivedEvent( byte[] data, int length )
        {
            Capture( data, PacketDirection.Incoming, false, "Incoming Packet" );
        }

        private void ViewPlayerEquipment( object obj )
        {
            if ( Engine.Player?.Equipment == null )
            {
                return;
            }

            Engine.UIInvoker?.Invoke( "EntityCollectionViewer", null, typeof( EntityCollectionViewerViewModel ),
                new object[] { Engine.Player.Equipment } );
        }

        private void Clear( object obj )
        {
            Items.Clear();
        }

        private void OnPacketEntryPropertyChanged( object sender, PropertyChangedEventArgs e )
        {
            if ( e.PropertyName != nameof( PacketEnabledEntry.Enabled ) ||
                 !( sender is PacketEnabledEntry entry ) || entry.PacketID < 0 || entry.PacketID > 255 )
            {
                return;
            }

            _packetEnabled[entry.PacketID] = entry.Enabled;
        }

        private void EnableDisable( object obj )
        {
            if ( !( obj is PacketEnabledEntry packetEnabledEntry ) )
            {
                return;
            }

            if ( packetEnabledEntry.PacketID != -1 )
            {
                return;
            }

            foreach ( PacketEnabledEntry entry in PacketEntries )
            {
                entry.Enabled = packetEnabledEntry.Enabled;
            }
        }

        public class PacketEnabledEntry : INotifyPropertyChanged
        {
            private bool _enabled;
            private string _name;
            private int _packetId;

            public bool Enabled
            {
                get => _enabled;
                set => SetProperty( ref _enabled, value );
            }

            public string Name
            {
                get => _name;
                set => SetProperty( ref _name, value );
            }

            public int PacketID
            {
                get => _packetId;
                set => SetProperty( ref _packetId, value );
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged( [CallerMemberName] string propertyName = null )
            {
                PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
            }

            // ReSharper disable once RedundantAssignment
            public virtual void SetProperty<T>( ref T obj, T value, [CallerMemberName] string propertyName = "" )
            {
                obj = value;
                OnPropertyChanged( propertyName );
            }
        }
    }
}