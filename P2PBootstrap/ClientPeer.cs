using P2PNet.Distribution.NetworkTasks;
using P2PNet.Peers;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;

namespace P2PBootstrap
{
    public class ClientPeer : IPeer
    {
        /// <summary>
        /// Gets or sets the IP address of the peer.
        /// </summary>
        public IPAddress IP { get; set; }

        public string Address { get; set; }

        /// <summary>
        /// Gets or sets the port of the peer.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Gets or sets the TCP client associated with the peer.
        /// </summary>
        [JsonIgnore]
        public TcpClient Client { get; set; }

        /// <summary>
        /// Gets or sets the network stream associated with the peer.
        /// </summary>
        [JsonIgnore]
        public NetworkStream Stream { get; set; }

        /// <summary>
        /// Gets or sets an identifier for the peer. This can optionally be used to store complementary IDs for whitelisting and blacklisting peers in your network (ie MAC address or other unique identifiers).
        /// </summary>
        public string Identifier { get; set; }

        /// <summary>
        /// Gets or sets the last time data was received from this peer.
        /// </summary>
        public DateTime LastIncomingDataReceived { get; set; } = DateTime.Now;

        /// <summary>
        /// Gets or sets the queue of outgoing tasks to be sent to this peer.
        /// </summary>
        public Queue<NetworkTask> OutgoingTasks { get; set; } = new Queue<NetworkTask>();

        public void UpdateTimeIn()
        {
            LastIncomingDataReceived = DateTime.Now;
        }

        public ClientPeer() { }

        public ClientPeer(IPAddress ipAddress, string identifier, int port)
        {
            IP = ipAddress;
            Address = ipAddress.ToString();
            Identifier = identifier;
            Port = port;
        }
        public ClientPeer(IPeer peer)
        {
            IP = peer.IP;
            Address = peer.Address;
            Identifier = peer.Identifier;
            Port = peer.Port;
        }
    }
}
