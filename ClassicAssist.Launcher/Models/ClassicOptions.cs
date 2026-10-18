namespace ClassicAssist.Launcher.Models;

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

    /// <summary>
    ///     Launcher-only (no client argument): load the net10.0 ClassicAssist.dll into a NativeAOT
    ///     client instead of the ClassicAssistNE native shim. See <see cref="Client.PluginPathResolver" />.
    /// </summary>
    public bool ForceManagedPlugin { get; set; }

    public bool OverrideClientVersion { get; set; }

    [ClassicOption( "-reconnect_time", CanIncludeProperty = nameof( AutoReconnect ), DefaultValue = 60 )]
    public long ReconnectTime { get; set; } = 60000;

    [ClassicOption( "-skiploginscreen", IncludeIfFalse = false )]
    public bool SkipLoginScreen { get; set; }
}
