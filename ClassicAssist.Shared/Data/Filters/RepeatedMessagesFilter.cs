using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClassicAssist.Shared;
using ClassicAssist.Shared.UI;
using ClassicAssist.Shared.UI.ViewModels.Filters;
using ClassicAssist.Shared.UO.Data;
using ClassicAssist.UO.Data;
using Newtonsoft.Json.Linq;

namespace ClassicAssist.Data.Filters
{
    [FilterOptions( Name = "Repeated Messages", DefaultEnabled = false )]
    public class RepeatedMessagesFilter : FilterEntry, IConfigurableFilter
    {
        public static MessageFilterOptions FilterOptions { get; set; } = new MessageFilterOptions();

        public static bool IsEnabled { get; set; }
        private static List<RepeatedMessageEntry> RepeatedMessageEntries { get; } = new List<RepeatedMessageEntry>();

        public async Task Configure()
        {
            RepeatedMessagesFilterConfigureViewModel vm = new RepeatedMessagesFilterConfigureViewModel( FilterOptions );

            await Engine.UIInvoker.InvokeDialog( "RepeatedMessagesFilterConfigureWindow", dataContext: vm );
        }

        public void Deserialize( JToken token )
        {
            if ( token == null )
            {
                return;
            }

            try
            {
                FilterOptions = new MessageFilterOptions
                {
                    SendToJournal = token["SendToJournal"]?.ToObject<bool>() ?? false,
                    MessageLimit = token["MessageLimit"]?.ToObject<int>() ?? 5,
                    TimeLimit = token["TimeLimit"]?.ToObject<int>() ?? 5,
                    BlockedTime = token["BlockedTime"]?.ToObject<int>() ?? 5
                };
            }
            catch ( Exception )
            {
                FilterOptions = new MessageFilterOptions();
            }
        }

        public JObject Serialize()
        {
            return new JObject
            {
                { "SendToJournal", FilterOptions.SendToJournal },
                { "MessageLimit", FilterOptions.MessageLimit },
                { "TimeLimit", FilterOptions.TimeLimit },
                { "BlockedTime", FilterOptions.BlockedTime }
            };
        }

        public void ResetOptions()
        {
            FilterOptions = new MessageFilterOptions();
        }

        protected override void OnChanged( bool enabled )
        {
            IsEnabled = enabled;
        }

        public static bool CheckMessage( JournalEntry journalEntry )
        {
            if ( !IsEnabled )
            {
                return false;
            }

            if ( journalEntry.SpeechType != JournalSpeech.System &&
                 ( journalEntry.Name != "System" || journalEntry.Serial != -1 ) )
            {
                return false;
            }

            if ( FilterOptions.MessageLimit == 0 )
            {
                return true;
            }

            DateTime now = DateTime.Now;
            string text = journalEntry.Text;

            RepeatedMessageEntry entry = null;

            for ( int i = RepeatedMessageEntries.Count - 1; i >= 0; i-- )
            {
                if ( RepeatedMessageEntries[i].Message == text )
                {
                    entry = RepeatedMessageEntries[i];
                    break;
                }
            }

            if ( entry != null && entry.Blocked && entry.Expires < now )
            {
                RepeatedMessageEntries.Remove( entry );
                entry = null;
            }

            if ( entry != null && entry.Blocked && entry.Expires > now )
            {
                return true;
            }

            if ( entry == null )
            {
                RepeatedMessageEntries.Add( new RepeatedMessageEntry
                {
                    FirstReceived = now,
                    LastReceived = now,
                    Count = 1,
                    Message = text
                } );

                return false;
            }

            if ( entry.LastReceived < now - TimeSpan.FromSeconds( FilterOptions.TimeLimit ) )
            {
                RepeatedMessageEntries.Remove( entry );
                return false;
            }

            if ( entry.Count < FilterOptions.MessageLimit )
            {
                entry.Count++;
                entry.LastReceived = now;

                return false;
            }

            if ( Options.CurrentOptions.Debug )
            {
                Shared.UO.Commands.SystemMessage( $"Filtering message: {text}" );
            }

            entry.Blocked = true;
            entry.Expires = now + TimeSpan.FromSeconds( FilterOptions.BlockedTime );

            return true;
        }

        internal class RepeatedMessageEntry
        {
            public bool Blocked { get; set; }
            public int Count { get; set; }
            public DateTime Expires { get; set; } = DateTime.Now;
            public DateTime FirstReceived { get; set; }
            public DateTime LastReceived { get; set; }
            public string Message { get; set; }
        }

        /// <summary>
        ///     Notifies unlike WPF's plain POCO: the configure dialog binds these two-way, and Avalonia's
        ///     NumericUpDown needs the change notification to show a corrected/clamped value back.
        /// </summary>
        public class MessageFilterOptions : SetPropertyNotifyChanged
        {
            private int _blockedTime = 5;
            private int _messageLimit = 5;
            private bool _sendToJournal;
            private int _timeLimit = 5;

            public int BlockedTime
            {
                get => _blockedTime;
                set => SetProperty( ref _blockedTime, value );
            }

            public int MessageLimit
            {
                get => _messageLimit;
                set => SetProperty( ref _messageLimit, value );
            }

            public bool SendToJournal
            {
                get => _sendToJournal;
                set => SetProperty( ref _sendToJournal, value );
            }

            public int TimeLimit
            {
                get => _timeLimit;
                set => SetProperty( ref _timeLimit, value );
            }
        }
    }
}