using System;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using P2PNet.DicoveryChannels.WAN;
using P2PNet.Peers.CommProtocols;

namespace P2PNet.Peers
{
    /// <summary>
    /// Represents a peer connected through a TURN relay.
    /// TURN peers communicate through a bootstrap server relay when direct peer-to-peer connections are not possible.
    /// </summary>
    public class TurnPeer : IPeer
    {
        private readonly BootstrapChannelBase _parentChannel;

        /// <summary>
        /// Gets or sets the IP address of the peer. For TURN peers, this may be null or represent the relay server.
        /// </summary>
        [JsonIgnore]
        public IPAddress IP { get; set; }

        /// <summary>
        /// Gets or sets the address for the peer. For TURN peers, this returns the relay server address.
        /// </summary>
        public string Address
        {
            get
            {
                if (_parentChannel != null && _parentChannel.BootstrapServerEndpoint != null)
                    return _parentChannel.BootstrapServerEndpoint.Host;
                if (IP != null)
                    return IP.ToString();
                return string.Empty;
            }
            set
            {
                IPAddress ip;
                if (IPAddress.TryParse(value, out ip))
                    IP = ip;
            }
        }

        /// <summary>
        /// Gets or sets the port of the peer. For TURN peers, this represents the relay server port.
        /// </summary>
        public int Port
        {
            get
            {
                if (_parentChannel != null && _parentChannel.BootstrapServerEndpoint != null)
                    return _parentChannel.BootstrapServerEndpoint.Port;
                return 0;
            }
            set { }
        }

        /// <summary>
        /// Gets or sets the TCP client associated with the peer. 
        /// For TURN peers, this is typically null as communication is relay-based.
        /// </summary>
        [JsonIgnore]
        public TcpClient Client { get; set; }

        /// <summary>
        /// Gets or sets the network stream associated with the peer.
        /// For TURN peers, this is typically null; use the Protocol.GetStream() instead.
        /// </summary>
        [JsonIgnore]
        public NetworkStream Stream { get; set; }

        /// <summary>
        /// Gets or sets the unique identifier for the peer.
        /// </summary>
        public string Identifier { get; set; }

        /// <summary>
        /// Gets or sets the protocol handler for the peer. For TURN peers, this should be a <see cref="TurnNetProtocol"/>.
        /// </summary>
        public INetProtocol Protocol { get; set; }

        /// <summary>
        /// Gets the parent bootstrap channel that owns this TURN connection.
        /// </summary>
        [JsonIgnore]
        public BootstrapChannelBase ParentChannel => _parentChannel;

        /// <summary>
        /// Gets the URI of the bootstrap server providing TURN relay services.
        /// Derived from the parent channel.
        /// </summary>
        [JsonIgnore]
        public Uri BootstrapServerUri
        {
            get { return _parentChannel != null ? _parentChannel.BootstrapServerEndpoint : null; }
        }

        /// <summary>
        /// Gets the unique connection identifier for this TURN session.
        /// Derived from the protocol.
        /// </summary>
        public string ConnectionId
        {
            get
            {
                TurnNetProtocol turnProtocol = Protocol as TurnNetProtocol;
                return turnProtocol != null ? turnProtocol.ConnectionId : null;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this peer is connected via TURN relay.
        /// </summary>
        [JsonIgnore]
        public bool IsTurnConnection => Protocol is TurnNetProtocol;

        /// <summary>
        /// Initializes a new instance of the <see cref="TurnPeer"/> class.
        /// </summary>
        public TurnPeer() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="TurnPeer"/> class with a parent bootstrap channel.
        /// </summary>
        /// <param name="parentChannel">The bootstrap channel that owns this TURN connection.</param>
        /// <param name="identifier">The unique identifier of the remote peer.</param>
        /// <param name="turnProtocol">The TURN protocol handler for this peer.</param>
        public TurnPeer(BootstrapChannelBase parentChannel, string identifier, TurnNetProtocol turnProtocol)
        {
            _parentChannel = parentChannel;
            Identifier = identifier;
            Protocol = turnProtocol;
        }

        /// <summary>
        /// Closes the TURN connection and releases associated resources.
        /// </summary>
        public void Disconnect()
        {
            if (Protocol != null)
            {
                Protocol.Close();
                Protocol.Dispose();
                Protocol = null;
            }
        }
    }
}
