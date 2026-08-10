using System;
using System.Collections.ObjectModel;
using System.Threading;

namespace ClassicAssist.Data.Scavenger;

public class ScavengerManager
{
    private static ScavengerManager _instance;
    private static readonly Lock _instanceLock = new();

    private ScavengerManager()
    {
    }

    public Action CheckArea { get; set; }

    public Func<bool> IsEnabled { get; set; }

    public ObservableCollection<ScavengerEntry> Items { get; set; }

    public Action<bool> SetEnabled { get; set; }

    public static ScavengerManager GetInstance()
    {
        // ReSharper disable once InvertIf
        if ( _instance == null )
        {
            lock ( _instanceLock )
            {
                _instance ??= new ScavengerManager();
            }
        }

        return _instance;
    }
}