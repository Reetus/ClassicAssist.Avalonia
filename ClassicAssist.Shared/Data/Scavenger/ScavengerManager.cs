using System;
using System.Collections.ObjectModel;

namespace ClassicAssist.Data.Scavenger;

public class ScavengerManager
{
    private static readonly Lazy<ScavengerManager> _instance = new( () => new ScavengerManager() );

    private ScavengerManager()
    {
    }

    public Action CheckArea { get; set; }

    public Func<bool> IsEnabled { get; set; }

    public ObservableCollection<ScavengerEntry> Items { get; set; }

    public Action<bool> SetEnabled { get; set; }

    public static ScavengerManager GetInstance()
    {
        return _instance.Value;
    }
}