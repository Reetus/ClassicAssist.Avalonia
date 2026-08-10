using System.Collections.Generic;
using System.Threading;

namespace ClassicAssist.Data.Vendors;

public class VendorBuyManager
{
    private static readonly Lock _instanceLock = new();
    private static VendorBuyManager _instance;

    private VendorBuyManager()
    {
    }

    public IEnumerable<VendorBuyAgentEntry> Items { get; set; }

    public static VendorBuyManager GetInstance()
    {
        // ReSharper disable once InvertIf
        if ( _instance == null )
        {
            lock ( _instanceLock )
            {
                _instance ??= new VendorBuyManager();
            }
        }

        return _instance;
    }
}
