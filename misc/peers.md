---
uid: peersinfo
---

### Peers 👥

---

Peers are representative of client users within the peer network. When a connection is established to a peer through the TCP listener, the connection by default is wrapped in an instance of the `GenericPeer` implementation of `IPeer` and the `PeerChannel` which stores an `IPeer` implementation.

The `IPeer` interface defines the essential properties and methods for a peer, including the IP address, port, TCP client, network stream, and a unique identifier. The `GenericPeer` class provides a default implementation of the `IPeer` interface, encapsulating the peer's IP address, port, TCP client, and network stream. It also includes a unique identifier for the peer, which can be used for whitelisting and blacklisting peers in the network.

The `PeerChannel` class represents a communication channel with a peer in the P2P network. It manages the sending and receiving of data packets, handles connection retries, and maintains the state of the communication channel. The `PeerChannel` class also includes methods for opening and closing the channel, as well as handling incoming and outgoing data packets. The `PacketHandleProtocol` class stores the Action delegates for each packet type, and the `PeerChannel` will invoke these by default depending on the respective data packet type.

##### Peer Lifecycle

---

The lifecycle of a peer in the P2P network begins with the discovery and connection phase. When a new peer is discovered, the handler checks if the peer is a valid connection. If the peer is a new connection and not duplicate, and is also not blocked, it will be wrapped in an instance of the `GenericPeer` class and depending on the `IncomingPeerTrustPolicy.IncomingPeerTrustPolicy` value, will be either enqueued and/or passed to the event `OnIncomingPeerConnectionAttempt` by the `InboundConnectingPeersQueue`, then finally will be added to the `KnownPeers` list.

Once the connection is established, a `PeerChannel` is created to manage the communication with the peer. The `PeerChannel` handles the sending and receiving of data packets, connection retries, and maintains the state of the communication channel. The `PeerChannel` will then be added to the `ActivePeerChannels` list. The `PeerChannel` also invokes the appropriate Action delegates from the `PacketHandleProtocol` class based on the type of data packet received. This ensures that the correct actions are taken for each type of data packet, facilitating efficient and reliable communication between peers.

<p>
    <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/Peerlifecycle.png" alt="widescan chart">
</p>

There are some trust policies under `IncomingPeerTrustPolicy` that can slighly modify the initial behavior of the established `PeerChannel`

1. `AllowDefaultCommunication` - as the name implies, allows default communication between peers to exchange `PureMessagePackets` and `DisconnectPackets`. Default is **true**.
2. `AllowEnhancedPacketExchange` - determines if peers will be trusted to exchange all other packet types, such as `DataTransmissionPackets`, which contain binary data such as files and network-related tasks. Default is **false**.
3. `RunDefaultTrustProtocol` - determines if, upon opening the peer channel, the default routine `IncomingPeerTrustPolicy.DefaultTrustProtocol` will be invoked to determine if the peer is a truster member of the network or not

If you intend to erect a more secure and private network that will leverage encryption or certificates for identification, you might set `AllowDefaultCommunication` to true, `AllowEnhancedPacketExchange` to false, and `RunDefaultTrustProtocol` to true. You would then devise a multi-step method called `PeerTrustHandshake` that takes a `PeerChannel` parameter. For example, the method would exchange a few `PureMessagePackets` for some kind of handshake, temporarily elevate the trust level of the peer, then swap a `DataTransmissionPacket` to exchange a key or signed message. Then confirm whether to keep trust elevated, or to demote trust and then proceed to end the connection with the peer. Then you would set the `IncomingPeerTrustPolicy.DefaultTrustProtocol` delegate to the `PeerTrustHandshake` you just made.

##### Peer Channel

---

The `PeerChannel` is a managed wrapper for the connection with the peer. It is designed to handle the inbound and outbound relay of data and information, control accessibility and permissions, and safely handle other network logic. Much of the logic uses delegates in order to remain modular for development needs.

<p>
    <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/peerchannel.png" alt="widescan chart">
</p>

This is the list of delegates in the `PacketHandleProtocol` class that the `PeerChannel` uses for handling different types of packets. These are all Action< string > delegates, where the string is a JSON serialized form of the packet:


| Name                                                    | Description                                                            |
| --------------------------------------------------------- | :----------------------------------------------------------------------- |
| PacketHandleProtocol.HandleIdentityPacketAction         | The default delegate for handling inbound IdentityPackets.             |
| PacketHandleProtocol.HandleDisconnectPacketAction       | The default delegate for handling inbound DisconnectPackets.           |
| PacketHandleProtocol.HandlePeerGroupPacketAction        | The default delegate for handling inbound peer CollectionSharePackets. |
| PacketHandleProtocol.HandleDataTransmissionPacketAction | The default delegate for handling inbound DataTransmissionPackets.     |
| PacketHandleProtocol.HandlePureMessagePacketAction      | The default delegate for handling inbound PureMessagePackets.          |

These all have default implementations that can be overridden. The `PeerChannel` will always pass packets to the respective delegate, regardless if the `OnDataReceived` event is subscribed to and utilized. If you want to handle all inbound data through the `OnDataReceived` event, you will need to create empty filler methods that simply do not preform any actions and assign them to override the default delegates.
