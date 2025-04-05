using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using P2PNet.Peers;

namespace P2PNet.Peers
    {
    /// <summary>
    /// Represents a default peer implementation using IPeer.
    /// </summary>
    public class GenericPeer : IPeer
    {
        /// <summary>
        /// Gets or sets the IP address of the peer.
        /// </summary>
        [JsonIgnore]
        public IPAddress IP { get; set; }
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
        /// Gets or sets the address for the peer.
        /// </summary>
        public string Address
        {
            get { return IP.ToString(); }
            set { IP = IPAddress.Parse(value); }
        }
        /// <summary>
        /// Gets or sets the unique identifier for the peer.
        /// </summary>
        public string Identifier { get; set; }
        public GenericPeer() { }

        public GenericPeer(IPAddress ip, int port)
        {
            IP = ip;
            Port = port;
        }

        public GenericPeer(IPAddress ip, string identifies, int port)
        {
            IP = ip;
            Identifier = identifies;
            Port = port;
        }

    }

}
