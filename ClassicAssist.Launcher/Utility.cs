using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ClassicAssist.Launcher
{
    public static class Utility
    {
        public static async Task<IPAddress> ResolveAddress( string hostname )
        {
            if ( IPAddress.TryParse( hostname, out IPAddress address ) )
            {
                return address;
            }

            IPHostEntry hostentry = await Dns.GetHostEntryAsync( hostname );

            return hostentry?.AddressList.FirstOrDefault( i => i.AddressFamily == AddressFamily.InterNetwork );
        }
    }
}
