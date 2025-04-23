using P2PNet.Distribution.FileManager;
using P2PNet.Distribution.NetworkTasks;
using P2PNet.NetworkPackets;
using P2PNet.Peers;
using System;
using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Timers;

namespace P2PNet.Distribution
    {
    /// <summary>
    /// Provides methods and properties to handle data distribution in the peer-to-peer network.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The DistributionHandler static class orchestrates the distribution of data packets such as files,
    /// network tasks, and miscellaneous data across trusted peer channels. It maintains separate concurrent
    /// queues for outgoing and incoming data packets.
    /// </para>
    /// <para>
    /// Outgoing packets are queued and then distributed to all trusted peers by periodically checking the queue.
    /// Incoming packets are similarly enqueued and processed in distinct logic branches based on their payload type.
    /// Additionally, the class exposes methods to wrap raw data into a data transmission packet and to 
    /// asynchronously queue incoming serialized packets.
    /// </para>
    /// <para>
    /// A file manager is used to dispatch files via memory-mapped files and the internal <see cref="MemoryHandler"/>
    /// class provides support for loading and managing file data.
    /// </para>
    /// </remarks>
    public static class DistributionHandler
        {

        /// <summary>
        /// Gets the list of trusted peer channels.
        /// </summary>
        /// <remarks>
        /// This property returns a list of active peer channels filtered to include only those that have been designated
        /// as trusted. It uses the <see cref="PeerNetwork.ActivePeerChannels"/> collection.
        /// </remarks>
        static List<PeerChannel> _trustedPeerChannels
        {
            get { return PeerNetwork.ActivePeerChannels.Where(pc => pc.IsTrustedPeer).ToList(); }
        }

        /// <summary>
        /// Queue for outgoing data packets to be distributed to trusted peers.
        /// </summary>
        public static ConcurrentQueue<DataTransmissionPacket> outgoingDataQueue = new ConcurrentQueue<DataTransmissionPacket>();

        /// <summary>
        /// Queue for incoming data packets to be processed.
        /// </summary>
        public static ConcurrentQueue<DataTransmissionPacket> incomingDataQueue = new ConcurrentQueue<DataTransmissionPacket>();

        /// <summary>
        /// Gets or sets the network file manager instance.
        /// </summary>
        /// <remarks>
        /// The file manager is responsible for handling file data from the network and mapping inbound file data
        /// to a storage mechanism; by default, an instance of <see cref="MemoryMappedFileManager"/> is used.
        /// </remarks>
        public static IFileManager NetworkFileManager { get; set; } = new MemoryMappedFileManager();

        private static Timer _outboundChecker;
        private static Timer _queueChecker;

        /// <summary>
        /// Queues a data transmission packet for distribution to trusted peers.
        /// </summary>
        /// <param name="packet">The data transmission packet to enqueue.</param>
        public static void QueueDataForDistribution(DataTransmissionPacket packet)
            {
            outgoingDataQueue.Enqueue(packet);
            }

        /// <summary>
        /// Queues raw data for distribution by wrapping it into a data transmission packet.
        /// </summary>
        /// <param name="data">The raw data bytes to be distributed.</param>
        /// <param name="dataType">The type of data indicated by <see cref="DataPayloadFormat"/>.</param>
        /// <remarks>
        /// This overload allows enqueueing data that is not already wrapped in a data transmission packet.
        /// The packet is constructed using the data and its specified payload format.
        /// </remarks>
        public static void QueueDataForDistribution(byte[] data, DataPayloadFormat dataType)
            {
            outgoingDataQueue.Enqueue(new DataTransmissionPacket { Data = data, DataType = dataType });
            } // overload for raw data not wrapped in DTP

        /// <summary>
        /// Enqueues an incoming data packet for processing.
        /// </summary>
        /// <param name="packet">The data packet to enqueue.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public static async Task EnqueueIncomingDataPacket(DataTransmissionPacket packet)
            {
            incomingDataQueue.Enqueue(packet);
            }

        /// <summary>
        /// Enqueues a serialized incoming data packet for processing.
        /// </summary>
        /// <param name="packet">The serialized data packet to enqueue.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public static async Task EnqueueIncomingDataPacket(string packet)
            {
            DataTransmissionPacket packet_ = Deserialize<DataTransmissionPacket>(packet);
            incomingDataQueue.Enqueue(packet_);
            }

        static DistributionHandler()
            {
            _outboundChecker = new System.Timers.Timer(500); // half second
            _outboundChecker.Elapsed += HandleOutgoingData;
            _outboundChecker.AutoReset = true;
            _outboundChecker.Enabled = true;

            _queueChecker = new System.Timers.Timer(500); // half seconds
            _queueChecker.Elapsed += HandleIncomingDataPackets;
            _queueChecker.AutoReset = true;
            _queueChecker.Enabled = true;
            }

        /// <summary>
        /// Handles outgoing data by continuously checking and processing the outgoing data queue.
        /// </summary>
        /// <param name="source">The source object, typically the timer.</param>
        /// <param name="e">The elapsed event arguments.</param>
        internal static void HandleOutgoingData(System.Object source, ElapsedEventArgs e)
            {
            if (outgoingDataQueue.IsEmpty)
                return;
            while (!outgoingDataQueue.IsEmpty)
                {
                outgoingDataQueue.TryDequeue(out DataTransmissionPacket incomingpacket);
                DistributeData(incomingpacket);
                }
            }

        /// <summary>
        /// Handles incoming data packets by continuously checking the incoming data queue and processing each packet.
        /// </summary>
        /// <param name="source">The source object, generally the timer that triggers this event.</param>
        /// <param name="e">Elapsed event arguments.</param>
        /// <remarks>
        /// The method processes each dequeued packet based on its <see cref="DataPayloadFormat"/>. Files are forwarded
        /// to the network file manager for inbound handling. Tasks are converted from a byte representation to a UTF-8 string,
        /// deserialized, and then enqueued into the incoming network task queue.
        /// </remarks>
        private static async void HandleIncomingDataPackets(System.Object source, ElapsedEventArgs e)
            {
            while (!incomingDataQueue.IsEmpty)
                {
                if (incomingDataQueue.TryDequeue(out DataTransmissionPacket packet))
                    {
                    // Logic for handling the packet based on DataType
                    switch (packet.DataType)
                        {
                        case DataPayloadFormat.File:
                            NetworkFileManager.InboundDatapacketToFile(packet);
                            break;
                        case DataPayloadFormat.Task:
                            string _out = Encoding.UTF8.GetString(UnwrapData(packet));
                            try
                            {
                                var _nt = Deserialize<NetworkTask>(_out);
                                if(_nt != null)
                                {
                                    NetworkTaskHandler.incomingNetworkTasks.Enqueue(_nt);
                                }
                            } catch { 
                            // do nothing here for now
                            }
                            break;
                        case DataPayloadFormat.MiscData:

                            break;
                    }
                    }
                }
            }

        /// <summary>
        /// Distributes outgoing data by serializing the data transmission packet, wrapping it,
        /// and sending it to all trusted peer channels.
        /// </summary>
        /// <param name="outgoingpacket">The data transmission packet to distribute.</param>
        private static void DistributeData(DataTransmissionPacket outgoingpacket)
            {
            string outdata = Serialize<DataTransmissionPacket>(outgoingpacket);
            WrapPacket(PacketType.DataTransmissionPacket, ref outdata);
            foreach (var peer in _trustedPeerChannels)
                {
                peer.LoadOutgoingData(outdata);
                }
            }

        /// <summary>
        /// Distributes a file asynchronously by reading its contents and queuing it for distribution.
        /// </summary>
        /// <param name="filePath">The path of the file to distribute.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public static async Task DistributeFileAsync(string filePath)
            {
            DataTransmissionPacket outgoingpacket = new DataTransmissionPacket();
            outgoingpacket.DataType = DataPayloadFormat.File;
            outgoingpacket.Data = await File.ReadAllBytesAsync(filePath);
            QueueDataForDistribution(outgoingpacket);
            }

        }

    }