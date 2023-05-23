using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Proxy
{
    internal static class ProxyUtils
    {
        /// <summary>
        /// Shut down and close the socket connection gracefully.
        /// </summary>
        public static void GracefulShutdown(Peer peer)
        {
            // Release the socket. - from Microsoft
            peer.Sock.Shutdown(SocketShutdown.Both);
            peer.Addressee?.Sock.Shutdown(SocketShutdown.Both);

            // Close the sockets
            peer.Sock.Close();
            peer.Addressee?.Sock.Close();
        }
        public static async Task ForwardHTTPMessage(Peer destPeer, byte[] receivedBytes, string? hostPort)
        {
            // Get the relevant socket
            Socket destSock = destPeer.Sock;

            if (hostPort is null)
                hostPort = "Client";
            await destSock.SendAsync(receivedBytes);
        }
        /// <summary>
        /// Returns the peer's pal in the <c>_peerDic</c> dictionary.
        /// </summary>
        public static Peer? GetAddressee(ConcurrentDictionary<Peer, Peer?> dic, Peer peer)
        {
            // find the relevant key - value pair
            var item = dic.First(kvp => kvp.Key == peer || kvp.Value == peer);
            // get the destination peer
            Peer? destPeer = item.Key == peer ? item.Value : item.Key;
            return destPeer;
        }
        public static string Get200OK()
        {
            // Return the full 200 OK HTTP message
            return "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n";
        }

        public static string Get502BadGateway()
        {
            // return the full 502 Bad Gateway message
            string badReq = "HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n";
            return badReq;
        }

        public static string Get403Forbidden()
        {
            // return the full 403 Forbidden message
            string forbidden = "HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n";
            return forbidden;
        }

        public static (string? host, int port, string hostPort) GetHostNPort(string message)
        {
            try
            {
                // Decode the HOST header to IP address
                string host = Regex.Match(message, @"(?<=Host: ).+(?=\r\n)").ToString();
                if (host == string.Empty)
                    host = Regex.Match(message, @"(?<=host: ).+(?=\r\n)").ToString();

                string hostPort = string.Empty;
                int port = 0;

                // Check if host name includes port
                if (host.Contains(':'))
                {
                    // Remove the port then try converting to int
                    hostPort = host;
                    host = Regex.Match(host, @".+(?=:)").ToString();
                    try
                    {
                        port = Convert.ToInt32(Regex.Match(hostPort, @"(?<=:).+").ToString());
                        return (host, port, hostPort);
                    }
                    catch
                    {
                        // Return bad request/port
                        throw new ArgumentException("Bad Port");
                    }
                }
                // Get the correct port by message content
                if (message.Contains("https"))
                {
                    hostPort = host + ":443";
                    port = 443;
                }
                else if (message.Contains("http"))
                {
                    hostPort = host + ":80";
                    port = 80;
                }
                return (host, port, hostPort);
            }
            catch { return (null, 0, "Client"); }
        }

        public static bool CheckHTTP(string message)
        {
            return (message.Contains("GET ") || message.Contains("HEAD ")
                              || message.Contains("POST ") || message.Contains("PUT ")
                              || message.Contains("DELETE ") || message.Contains("CONNECT ")
                              || message.Contains("OPTIONS ") || message.Contains("TRACE ")
                              || message.Contains("PATH "));
        }

        public static async Task<byte[]> SendReceiveAsync(Socket destination, byte[] message, string hostPort,
                                                          int bufferLength)
        {
            await destination.SendAsync(message, SocketFlags.None);
            //Console.WriteLine($"\nSent to {hostPort}\n");

            byte[] localBuffer = new byte[bufferLength];

            int receivedAmount = await destination.ReceiveAsync(localBuffer, SocketFlags.None);
            byte[] toReturn = new byte[receivedAmount];
            Array.Copy(localBuffer, 0, toReturn, 0, receivedAmount);
            //Console.WriteLine($"\nReceived from {hostPort}: {Encoding.UTF8.GetString(toReturn)}\n");

            return toReturn;
        }
        public static async Task<byte[]> SendReceiveAsync(Socket destination, string message, string hostPort,
                                                          int bufferLength)
        {
            byte[] toSend = Encoding.UTF8.GetBytes(message);
            await destination.SendAsync(toSend, SocketFlags.None);
            ////Console.WriteLine($"\nSent to {hostPort}\n");

            byte[] localBuffer = new byte[bufferLength];

            int receivedAmount = await destination.ReceiveAsync(localBuffer, SocketFlags.None);
            byte[] toReturn = new byte[receivedAmount];
            Array.Copy(localBuffer, 0, toReturn, 0, receivedAmount);
            ////Console.WriteLine($"\nReceived from {hostPort}: {Encoding.UTF8.GetString(toReturn)}\n");

            return toReturn;
        }
    }
}
