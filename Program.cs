using System.Net;
using System.Text.RegularExpressions;
using System.Net.Sockets;

namespace Proxy
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            await Proxy.RunProxyAsync(2109);
        }
    }
}