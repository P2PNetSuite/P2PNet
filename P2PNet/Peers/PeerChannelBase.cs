using P2PNet.Distribution;
using P2PNet.Distribution.P2PNet.Distribution;
using P2PNet.NetworkPackets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static P2PNet.PeerNetwork;

namespace P2PNet.Peers
    {
    public abstract class PeerChannelBase
        {
        internal readonly static Dictionary<int, string> ErrorCodeDescriptions = new Dictionary<int, string>()
        {
            { 10050, "Network is down. Socket operation encountered a dead network." },
            { 10051, "Network is unreachable. Socket operation attempted to unreachable network." },
            { 10052, "Network dropped connection on reset. Connection broken due to keep-alive activity." },
            { 10053, "Software caused connection abort. Established connection aborted by software." },
            { 10054, "Connection reset by peer. Existing connection forcibly closed by remote host." },
            { 10055, "No buffer space available. Operation on socket couldn't be performed due to lack of buffer space or full queue." },
            { 10056, "Socket is already connected. Connect request made on already-connected socket." },
            { 10057, "Socket is not connected. Request to send or receive data disallowed because socket is not connected." },
            { 10058, "Cannot send after socket shutdown. Request to send or receive data disallowed due to previous socket shutdown." },
            { 10059, "Too many references. Exceeded limit of references to kernel object." },
            { 10060, "Connection timed out. Connection attempt failed due to no response from connected party." }
        }; // TODO implement more pleasant exception handling

        /// <summary>
        /// Gets or sets the peer associated with this channel.
        /// </summary>
        public IPeer peer { get; set; }
        private bool _identifierSet = false;

        protected ConcurrentQueue<string> IncomingDataQueue = new ConcurrentQueue<string>();
        protected ConcurrentQueue<string> OutgoingDataQueue = new ConcurrentQueue<string>();
        protected ConcurrentQueue<PacketTypeRelay> packetQueue = new ConcurrentQueue<PacketTypeRelay>();

        protected Dictionary<string, Type> CustomData = new Dictionary<string, Type>();
        /// <summary>
        /// Adds custom properties or data to the peer channel. This is to help with custom needs associated with the peer channel.
        /// </summary>
        public void AddProperties(string key, Type type)
        {
            try
            {
                CustomData.Add(key, type);
            }
            catch(Exception e)
            {
                DebugMessage(e.ToString(), MessageType.Critical);
            }
        }
        /// <summary>
        /// Attempts to retrieve a custom property or data from the peer channel.
        /// </summary>
        /// <returns>The target value stored in the peer channel.</returns>
        public Type TryGetProperty(string key)
        {
            if (CustomData.ContainsKey(key))
            {
                return CustomData[key];
            }
            else
            {
                return null; // or throw an exception, depending on your error handling preference
            }
        }

        /// <summary>
        /// Add the outgoing information to the broadcast queue.
        /// </summary>
        /// <param name="outgoing">The information to be queued for broadcast.</param>
        public virtual void LoadOutgoingData(string outgoing)
            {
            OutgoingDataQueue.Enqueue(outgoing);
            }
        public virtual void LoadOutgoingData(DataTransmissionPacket dataTransmissionPacket)
        {
            string outgoing = Serialize<DataTransmissionPacket>(dataTransmissionPacket);
            WrapPacket(PacketType.DataTransmissionPacket, ref outgoing);
            OutgoingDataQueue.Enqueue(outgoing);
        }
        public virtual void LoadOutgoingData(PureMessagePacket pureMessagePacket)
        {
            string outgoing = Serialize<PureMessagePacket>(pureMessagePacket);
            WrapPacket(PacketType.PureMessage, ref outgoing);
            OutgoingDataQueue.Enqueue(outgoing);
        }
        public virtual void LoadOutgoingData(CollectionSharePacket peerColPacket)
        {
            string outgoing = Serialize<CollectionSharePacket>(peerColPacket);
            WrapPacket(PacketType.PeerGroupPacket, ref outgoing);
            OutgoingDataQueue.Enqueue(outgoing);
        }
        public virtual void LoadOutgoingData(DisconnectPacket disconnectPacket)
        {
            string outgoing = Serialize<DisconnectPacket>(disconnectPacket);
            WrapPacket(PacketType.DisconnectPacket, ref outgoing);
            OutgoingDataQueue.Enqueue(outgoing);
        }

        internal Task sendTask;
        internal CancellationTokenSource cancelSender;

        internal Task receiveTask;
        internal CancellationTokenSource cancelReceiver;
        internal readonly object receiveLock = new object();
        private EventHandler<DataReceivedEventArgs> _dataReceived;

        /// <summary>
        /// Occurs when a peer channel receives incoming data or information.
        /// Subscribers can use this event to handle and process incoming data and information.
        /// </summary>
        /// <example>
        /// <code>
        ///    // Event is raised when a new known peer is discovered, regardless of point of origin
        ///    private static void HandleNewKnownPeer(object sender, PeerNetwork.NewPeerEventArgs e)
        ///    {
        ///        // The peer channel's DataReceived event subscribed to HandleIncomingData function
        ///        e.peerChannel.DataReceived += HandleIncomingData;
        ///    }
        ///    
        ///    private static void HandleIncomingData(object? sender, Peer_Channel_Base.DataReceivedEventArgs e)
        ///    {
        ///        Console.WriteLine(e.Data); // incoming information received by the PeerChannel is printed to console
        ///    }
        /// 
        /// </code>
        /// </example>
        public event EventHandler<DataReceivedEventArgs> DataReceived
                {
                add
                    {
                    _dataReceived += value;
                    }
                remove
                    {
                    _dataReceived -= value;
                    }
                }
        protected virtual void OnDataReceived(string data)
                {
                _dataReceived?.Invoke(this, new DataReceivedEventArgs(data));
                }
        public delegate void DataReceivedEventHandler(object sender, DataReceivedEventArgs e);
        public class DataReceivedEventArgs : EventArgs
            {
            public string Data { get; }

            public DataReceivedEventArgs(string data)
                {
                Data = data;
                }
            }

        internal Task packetHandler;
        internal CancellationTokenSource cancelPacketHandler;
        internal readonly object sendLock = new object();

        protected bool _isTrustedPeer { get; set; } = false;
        public bool IsTrustedPeer { get { return _isTrustedPeer; } }

        virtual protected void TerminateChannel()
            {
            cancelSender.Cancel();
            cancelReceiver.Cancel();
            cancelPacketHandler.Cancel();

            UntrustPeer();

            // Free up resources
            IncomingDataQueue.Clear();
            OutgoingDataQueue.Clear();
            packetQueue.Clear();
            }
        /// <summary>
        /// Terminates the sender task.
        /// </summary>
        /// <remarks>Peer channel will cease broadcasting outbound data packets.</remarks>
        virtual public void TerminateCurrentSender()
            {
            cancelSender.Cancel();
            }
        /// <summary>
        /// Terminates the receiver task.
        /// </summary>
        /// <remarks>Peer channel will cease processing inbound data packets.</remarks>
        virtual public void TerminateCurrentReceiver()
            {
            cancelReceiver.Cancel();
            }
        /// <summary>
        /// Terminates the packet handler task.
        /// </summary>
        virtual public void TerminatePacketHandler()
            {
            cancelPacketHandler.Cancel();
            }
        /// <summary>
        /// Promote trust level of peer.
        /// </summary>
        virtual public void TrustPeer()
            {
            _isTrustedPeer = true;
            }
        /// <summary>
        /// Demote trust level of peer.
        /// </summary>
        virtual public void UntrustPeer()
            {
            _isTrustedPeer = false;
            }

        protected void StartPacketHandling()
            {
            cancelPacketHandler = new CancellationTokenSource();
            packetHandler = Task.Run(() => PacketHandler(cancelPacketHandler.Token));
            }
        protected async Task PacketHandler(CancellationToken cancellationToken)
            {
            while (!cancellationToken.IsCancellationRequested)
                {
                while (packetQueue.TryDequeue(out PacketTypeRelay packet))
                    {
                    switch (packet.packetType)
                        {
                        case PacketType.IdentityPacket:
                            HandleIdentityPacket(packet.Data);
                            break;
                        case PacketType.DisconnectPacket:
                            HandleDisconnectPacket(packet.Data);
                            break;
                        case PacketType.PeerGroupPacket:
                            HandlePeerGroupPacket(packet.Data);
                            break;
                        case PacketType.DataTransmissionPacket:
                            HandleDataTransmissionPacket(packet.Data);
                            break;
                        case PacketType.PureMessage:
                            HandlePureMessagePacket(packet.Data);
                            break;
                        default:
                            // BADPACKET somehow made it here

                            DebugMessage("Bad packet recieved.", MessageType.Warning);

                            break;
                        }
                    }
                Thread.Sleep(75);
                }
            }

        // default delegate implementation using PacketHandleProtocol
        #region Packet Handling
        protected virtual void HandleIdentityPacket(string data)
        {
            if (_isTrustedPeer || TrustPolicies.IncomingPeerTrustPolicy.AllowEnhancedPacketExchange)
            {
                PacketHandleProtocol.HandleIdentityPacketAction?.Invoke(data);
            } // enhanced packet exchange defines behavior with impact
        }
        protected virtual void HandleDisconnectPacket(string data)
        {
            if (_isTrustedPeer || TrustPolicies.IncomingPeerTrustPolicy.AllowDefaultCommunication == true)
            {
                PacketHandleProtocol.HandleDisconnectPacketAction?.Invoke(data);
            } // default communication defines relay of info with little to impact
        }
        protected virtual void HandlePeerGroupPacket(string data)
        {
            if (_isTrustedPeer || TrustPolicies.IncomingPeerTrustPolicy.AllowEnhancedPacketExchange)
            {
                PacketHandleProtocol.HandlePeerGroupPacketAction?.Invoke(data);
            } // enhanced packet exchange defines behavior with impact
        }
        protected virtual void HandleDataTransmissionPacket(string data)
        {
            if (_isTrustedPeer || TrustPolicies.IncomingPeerTrustPolicy.AllowEnhancedPacketExchange)
            {
                PacketHandleProtocol.HandleDataTransmissionPacketAction?.Invoke(data);
            } // enhanced packet exchange defines behavior with impact
        }
        protected virtual void HandlePureMessagePacket(string data)
        {
            if (_isTrustedPeer || TrustPolicies.IncomingPeerTrustPolicy.AllowDefaultCommunication == true)
            {
                PacketHandleProtocol.HandlePureMessagePacketAction?.Invoke(data);
            } // default communication defines relay of info with little to impact
        }
        #endregion

        protected PacketTypeRelay ExtractWholeMessage(string receivedData)
            {
            MessageTags tags_ = new MessageTags();
            foreach (var pair in PacketTagMap)
                {
                var tags = pair.Value;
                if (receivedData.Contains(tags.OpeningTag) && receivedData.Contains(tags.ClosingTag))
                    {
                    tags_.OpeningTag = tags.OpeningTag;
                    tags_.ClosingTag = tags.ClosingTag;
                    break;
                    }
                }

            int startIndex = receivedData.IndexOf(tags_.OpeningTag); // find first opening tag
            startIndex += tags_.OpeningTag.Length;
            if (startIndex < 0)
                {
                return new PacketTypeRelay() { packetType = PacketType.BADPACKET, Data = "" }; // no opening tag found
                }

            int endIndex = receivedData.IndexOf(tags_.ClosingTag, startIndex); // Look for closing tag after opening tag

            if (endIndex < 0)
                {
                return new PacketTypeRelay() { packetType = PacketType.BADPACKET, Data = "" }; // No opening tag found
                // Not a complete message, return all data (might be partial message)
                }


            PacketTypeRelay validPacket = new PacketTypeRelay()
                {
                Data = receivedData.Replace(tags_.OpeningTag, "").Replace(tags_.ClosingTag, ""),
                packetType = PacketTagMap.FirstOrDefault(x => x.Value.OpeningTag == tags_.OpeningTag).Key
                };
            return validPacket;
            }
        protected bool IsValidMessageFormat(string inputString)
            {
            foreach (var pair in PacketTagMap)
                {
                var tags = pair.Value;
                if (inputString.Contains(tags.OpeningTag) && inputString.Contains(tags.ClosingTag))
                    {
                    return true;
                    }
                }

            return false;
            }

        }
    }
