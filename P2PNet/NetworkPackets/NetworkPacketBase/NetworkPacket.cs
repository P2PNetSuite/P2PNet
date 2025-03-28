using P2PNet.NetworkPackets.NetworkPacketBase.NetworkPacketBase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace P2PNet.NetworkPackets.NetworkPacketBase
{
    public class NetworkPacket : INetworkPacket
    {
        /// <summary>
        /// Gets or sets the origin identifier of the packet.
        /// </summary>
        public string SourceOriginIdentifier { get; set; } = PeerNetwork.Identifier;

        /// <summary>
        /// Gets or sets the source origin time of the packet.
        /// </summary>
        public DateTime SourceOriginTime { get; set; } = DateTime.UtcNow;
        [JsonConstructor]
        public NetworkPacket() { }

    }
}
