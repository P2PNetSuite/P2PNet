using P2PNet.NetworkPackets.NetworkPacketBase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace P2PNet.NetworkPackets
    {
    /// <summary>
    /// Represents a simple human-readable message packet.
    /// </summary>
    public sealed class PureMessagePacket : NetworkPacket
        {

        /// <summary>
        /// Gets or sets the message contained in the packet.
        /// </summary>
        public string Message { get; set; } = "Pinging";

        public PureMessagePacket() { } // empty constructor (keep for serialization/JSON raisins)

        /// <summary>
        /// Initializes a new instance of the <see cref="PureMessagePacket"/> class with a specified message.
        /// </summary>
        /// <param name="message">The message to include in the packet.</param>
        public PureMessagePacket(string message) : this() { Message = message; }

        }
    }