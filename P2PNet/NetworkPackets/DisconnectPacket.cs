using P2PNet.NetworkPackets.NetworkPacketBase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace P2PNet.NetworkPackets
    {
    /// <summary>
    /// Standard packet used to relay information about disconnecting peers.
    /// </summary>
    public sealed class DisconnectPacket : NetworkPacket
        {
        /// <summary>
        ///  Peer's IP address.
        /// </summary>
        public string IP { get; set; }

        /// <summary>
        /// Peer's port number.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Disconnection time-stamp.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Optional data to include pertaining to disconnection.
        /// </summary>
        public Dictionary<string, string> Data { get; set; }

        public DisconnectPacket() { }


        /// <summary>
        /// Initializes a new instance of the  DisconnectPacket class with the specified IP and port. Time-stamp is initialized to the current DateTime value.
        /// </summary>
        /// <param name="ip">The IP address of the peer.</param>
        /// <param name="port">The port of the peer.</param>
        public DisconnectPacket(string ip, int port) : this()
            {
            IP = ip;
            Port = port;
            Timestamp = DateTime.UtcNow;
            Data = new Dictionary<string, string>();
            }
        }
}