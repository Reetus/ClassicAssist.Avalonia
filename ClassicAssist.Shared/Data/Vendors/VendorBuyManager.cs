using System.Collections.Generic;

namespace ClassicAssist.Data.Vendors
{
    public class VendorBuyManager
    {
        private static readonly object _instanceLock = new object();
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
                    if ( _instance == null )
                    {
                        _instance = new VendorBuyManager();
                    }
                }
            }

            return _instance;
        }
    }
}
