using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace P2PNet.Peers
{
    /// <summary>
    /// Represents a peer with an IP address, port, TCP client, network stream, and identifier.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(GenericPeer), "GenericPeer")]
    public interface IPeer
    {
        /// <summary>
        /// Gets or sets the IP address of the peer.
        /// </summary>
        [JsonIgnore]
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
    }

}