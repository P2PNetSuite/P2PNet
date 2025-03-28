using P2PNet.Peers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace P2PNet.NetworkPackets.NetworkPacketBase.NetworkPacketBase
    {
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(NetworkPacket), "NetworkPacket")]
    public interface INetworkPacket
        {
            public string SourceOriginIdentifier { get; set; }
            public DateTime SourceOriginTime { get; set; }
        }
    }
