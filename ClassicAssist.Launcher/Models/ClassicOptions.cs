namespace ClassicAssist.Launcher.Models
{
    public class ClassicOptions
    {
        [ClassicOption( "-autologin", IncludeIfFalse = true )]
        public bool Autologin { get; set; }

        [ClassicOption( "-reconnect" )]
        public bool AutoReconnect { get; set; }

        [ClassicOption( "-clientversion", CanIncludeProperty = nameof( OverrideClientVersion ), DefaultValue = "" )]
        public string ClientVersion { get; set; }

        [ClassicOption( "-debug", IncludeIfFalse = false )]
        public bool Debug { get; set; }

        public bool OverrideClientVersion { get; set; }

        [ClassicOption( "-reconnect_time", CanIncludeProperty = nameof( AutoReconnect ), DefaultValue = 60 )]
        public long ReconnectTime { get; set; } = 60000;

        [ClassicOption( "-skiploginscreen", IncludeIfFalse = false )]
        public bool SkipLoginScreen { get; set; }
    }
}
