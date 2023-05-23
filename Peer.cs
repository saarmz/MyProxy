using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Proxy
{
    internal class Peer
    {
        // The peer's socket
        public Socket Sock { get; set; }
        // The host (with the port number) - mainly used for debug
        public string? HostPort { get; set; }
        // Represents the other Peer this current one is talking to
        public Peer? Addressee { get; set; }
        public Peer(Socket sock, string hostPort, Peer? addressee)
        {
            Sock = sock;
            HostPort = hostPort;
            Addressee = addressee;
        }

        public static bool operator ==(Peer b1, Peer b2)
        {
            if (b1 is null)
                return b2 is null;

            return b1.Equals(b2);
        }

        public static bool operator !=(Peer b1, Peer b2)
        {
            return !(b1 == b2);
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;
            
            // Not checking the Addressee's equality
            // since it would cause Overflow Exception
            return obj is Peer b2 && (Sock == b2.Sock
                                      && HostPort == b2.HostPort);
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Sock, HostPort);
        }
    }
}