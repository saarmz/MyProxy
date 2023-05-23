using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static Proxy.ProxyUtils;

namespace Proxy
{
    internal static class Proxy
    {
        private static int _bufferLength = 8192;
        private static Socket _proxySock;
        private static Logger _netLogger = new Logger();

        // The dictionary tracks clients.
        // Each client has a host name, socket, connection lock and connection bool
        // Example: https://www.google.com:443, Socket, Lock (object), connecting?
        
        private static readonly ConcurrentDictionary<Peer, Peer?> _peerDic = new ConcurrentDictionary<Peer, Peer?>();
        static long totalCliHistory = 0;

        public static async Task RunProxyAsync(int port)
        {
            // Create the IpEndPoint needed for binding
            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, port);

            StartAccepting:
            try
            {
                // Create the socket
                _proxySock = new Socket(AddressFamily.InterNetwork,
                                        SocketType.Stream,
                                        ProtocolType.Tcp);

                // Bind the socket and configure listen amount
                _proxySock.Bind(ipEndPoint);
                _proxySock.Listen(100);
                Console.WriteLine($"Asynchronous Proxy is running on {IPAddress.Any}:{port}");

                // Accept clients and call the handler
                while (true)
                {
                    await AcceptHandleAsync();
                }
            }
            catch (SocketException se)
            {
                // Make sure proxy doesnt crash with no connections
                if (se.ErrorCode == 995)
                {
                    goto StartAccepting;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n{ex}\n");
            }
            Console.WriteLine("\n\nPress ENTER to continue...");
            Console.Read();

            // Release the socket. - from Microsoft
            _proxySock.Shutdown(SocketShutdown.Both);
            _proxySock.Close();
        }

        private static async Task AcceptHandleAsync()
        {
            // Wait for connection attempt
            Socket cliSock = await _proxySock.AcceptAsync();
            // Create a Peer object and add to dict
            totalCliHistory++;

            Peer peer = new Peer(cliSock, $"Client{totalCliHistory}", null);
            _peerDic.TryAdd(peer, null);

            // Start handling the peer
            HandlePeerAsync(peer);
        }

        private static async Task HandlePeerAsync(Peer peer)
        {
            Console.WriteLine($"\nPeer connected from: {peer.Sock.RemoteEndPoint}\n");

            // Wait (async) for client messages, build correct format and pass forward
            while (true)
            {
                try
                {
                    await WaitForMessageAsync(peer); 
                }
                catch (ObjectDisposedException e)
                {
                    Console.WriteLine($"\n{e}\n");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n{ex}\n");
                    try
                    {
                        // Indicates Socket was closed in WaitForMessage()
                        if (ex.Message == "Closed Socket")
                            break;

                        GracefulShutdown(peer);
                    }
                    finally
                    {
                        RemovePeerFromDic(_peerDic, peer);
                    }
                }
            }
        }

        private static void RemovePeerFromDic(ConcurrentDictionary<Peer, Peer?> dic, Peer peer)
        {
            // Remove the peers from dictionary
            try
            {
                var item = dic.First(kvp => kvp.Key == peer || kvp.Value == peer);
                dic.TryRemove(item);
                Console.WriteLine($"\nRemoved {item.Key.HostPort}\n");
                Console.WriteLine($"\nTotal Clients: {dic.Count}\n");
            }
            catch { }
        }

        private static async Task WaitForMessageAsync(Peer peer)
        {
            byte[] localBuffer = new byte[_bufferLength];

            // If peer is in dictionary
            if (_peerDic.ContainsKey(peer)
                || _peerDic.Values.Contains(peer))
            {
                int received = await peer.Sock.ReceiveAsync(localBuffer, SocketFlags.None);
                // If received end of connection
                if (received == 0)
                {
                    try
                    {
                        GracefulShutdown(peer);
                        RemovePeerFromDic(_peerDic, peer);
                    }
                    catch { }
                    // Notify the calling function
                    throw new Exception("Closed Socket");
                }
                // Get the message and forward control of it
                byte[] receivedBytes = new byte[received];
                Array.Copy(localBuffer, 0, receivedBytes, 0, received);

                string message = Encoding.UTF8.GetString(receivedBytes);
                //Console.WriteLine($"\n{peer.HostPort} sent: {message}\n");
                await HandleRequestAsync(peer, receivedBytes, message);
            }
            else
                throw new ArgumentException();
        }

        public static async Task HandleRequestAsync(Peer peer, byte[] receivedBytes, string message)
        {
            // Print to console accordingly
            if (peer.Addressee is not null)
                _netLogger.WriteLog($"{peer.HostPort} sent to {peer.Addressee.HostPort}:\n{message}");
            else
                _netLogger.WriteLog($"{peer.HostPort} sent to {peer.Addressee}:\n{message}");

            // Check if data is http by looking for a method
            if (CheckHTTP(message))
            {
                await HandleHTTPMessage(peer, receivedBytes, message);
                return;
            }
            // Try sending to and receiving from the server
            await HandleEncryptedMessage(peer, receivedBytes);
        }
        private static async Task HandleHTTPMessage(Peer peer, byte[] receivedBytes, string message)
        {
            byte[]? toReturn = null;

            // Get the host & port from message
            (string host, int port, string hostPort) = GetHostNPort(message);

            // Forward request to the addressee if CONNECT is not required
            if (!message.Contains("CONNECT"))
            {
                Peer? destPeer = GetAddressee(_peerDic, peer);
                if (destPeer is null
                    || destPeer.HostPort != hostPort)
                {
                    // Attempt to connect
                    string? answer = await AttemptConnection(peer, host, port, 
                                                             hostPort, message);
                    // Encode the relevant message
                    if (answer != string.Empty)
                        toReturn = Encoding.UTF8.GetBytes(answer);
                    destPeer = GetAddressee(_peerDic, peer);
                }

                await ForwardHTTPMessage(destPeer, receivedBytes, null);
            }
            else
            {
                toReturn = await HandleConnect(peer, host, port, 
                                               hostPort, receivedBytes);
            }
            // Send response to client
            try
            {
                if (toReturn is not null)
                    await peer.Sock.SendAsync(toReturn);
            }
            // Close connections and remove from cliDic
            catch
            {
                GracefulShutdown(peer);
                RemovePeerFromDic(_peerDic, peer);
            }
        }
        private static async Task HandleEncryptedMessage(Peer cliPeer, byte[] receivedBytes) 
        {
            // If there is no addresse, throw exception
            if (cliPeer.Addressee is null)
                throw new ArgumentNullException();

            // Get the addressee
            Socket destSock = cliPeer.Addressee.Sock;
            string? hostPort = cliPeer.Addressee.HostPort;
            if (hostPort is null)
                hostPort = "Client";

            // Send using Socket
            if (destSock is not null)
            {
                await destSock.SendAsync(receivedBytes);
                //Console.WriteLine($"\nForwarded Ecrypted Message to {hostPort}\n");
            }
        }

        private static async Task<byte[]?> HandleConnect(Peer peer, string? host, int port, 
                                                         string hostPort, byte[] receivedBytes)
        {
            // Get the relevant addressee, connect if necessary,
            // forward the message and the response
            string? answer;
            Peer? destPeer = GetAddressee(_peerDic, peer);
            if ((destPeer is null || destPeer.HostPort != hostPort)
                && host is not null)
            {
                // Attempt to connect
                answer = await AttemptConnection(peer, host, port, hostPort, null);
                if (answer != string.Empty)
                    return Encoding.UTF8.GetBytes(answer);
                return null;
            }
            return null;
        }

        private static async Task ForwardHTTPMessage(Peer destPeer, byte[] receivedBytes, string? hostPort)
        {
            // Get the relevant socket
            Socket destSock = destPeer.Sock;

            if (hostPort is null)
                hostPort = "Client";
            await destSock.SendAsync(receivedBytes);
        }
        private static async Task<string> AttemptConnection(Peer peer, string host, int port,
                                                            string hostPort, string? message)
        {
            string toReturn = string.Empty;

            // Check if destination is accepted by proxy
            // Only http and https are allowed for now
            if (port != 443 && port != 80)
            {
                // Return Forbidden
                toReturn = Get403Forbidden();
                //Console.WriteLine($"\nReplied to {peer.HostPort} with 403 Forbidden\n");
                return toReturn;
            }
            // Create an HTTP tunnel
            try
            {
                Socket sock = new Socket(AddressFamily.InterNetwork,
                                        SocketType.Stream,
                                        ProtocolType.Tcp);
                await sock.ConnectAsync(host, port);

                Console.WriteLine($"Connected to {host}");
                Peer destPeer = new Peer(sock, hostPort, peer);
                peer.Addressee = destPeer;

                // Add to dictionary
                if (!_peerDic.TryAdd(peer, destPeer))
                {
                    _peerDic.TryGetValue(peer, out Peer? val);
                    _peerDic.TryUpdate(peer, destPeer, val);
                }
                if (message is not null)
                {
                    await sock.SendAsync(Encoding.UTF8.GetBytes(message));
                }
                // Handle the destination like a normal peer
                HandlePeerAsync(destPeer);

                // respond to client with 200 OK
                if (message is null)
                {
                    toReturn = Get200OK();
                    //Console.WriteLine($"\nReplied to {peer.HostPort}  with {toReturn}\n");
                }
            }
            catch
            {
                // Return Bad Gateway
                toReturn = Get502BadGateway();
                //Console.WriteLine($"\nReplied to {peer.HostPort}  with 502 Bad Gateway\n");
            }
            return toReturn;
        }
    }
}
