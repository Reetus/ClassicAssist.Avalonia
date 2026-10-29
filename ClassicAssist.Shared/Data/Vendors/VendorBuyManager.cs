using System;
using System.Collections.Generic;

namespace ClassicAssist.Data.Vendors;

public class VendorBuyManager
{
    private static readonly Lazy<VendorBuyManager> _instance = new( () => new VendorBuyManager() );

    private VendorBuyManager()
    {
    }

    public IEnumerable<VendorBuyAgentEntry> Items { get; set; }

    public static VendorBuyManager GetInstance()
    {
        return _instance.Value;
    }
}
