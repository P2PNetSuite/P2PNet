using P2PNet.Distribution.FileManager;
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
    /// Provides methods and properties to handle data distribution to trusted peer channels.
    /// </summary>
    public static class DistributionHandler
        {
        
        /// <summary>
        /// Gets the list of trusted peer channels.
        /// </summary>
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


        public static IFileManager NetworkFileManager { get; set; } = new VirtualImageFileManager();

        private static Timer _outboundChecker;
        private static Timer _queueChecker;

        /// <summary>
        /// Queues a data packet for distribution.
        /// </summary>
        /// <param name="packet">The data packet to queue.</param>
        public static void QueueDataForDistribution(DataTransmissionPacket packet)
            {
            outgoingDataQueue.Enqueue(packet);
            }

        /// <summary>
        /// Queues raw data for distribution by wrapping it in a data transmission packet.
        /// </summary>
        /// <param name="data">The raw data to queue.</param>
        /// <param name="dataType">The type of data being queued.</param>
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

                            break;
                        case DataPayloadFormat.MiscData:

                            break;
                    }
                    }
                }
            }

        internal static class MemoryHandler
            {

            internal struct MemoryEntry
            {
                internal string Filename { get; set; }
                internal int Id { get; set; }
                internal object Data { get; set; }
            }

            private static object _lock = new object(); // For thread safety

            // memory mapped files dictionary
            private static readonly Dictionary<string, MemoryMappedFile> _memoryMappedFiles = new Dictionary<string, MemoryMappedFile>();

            private static readonly Dictionary<string, MemoryEntry> _miscDataEntries = new Dictionary<string, MemoryEntry>();

            public static async Task LoadFileToMemoryMappedFile(DataTransmissionPacket packet)
            {
                byte[] fileData = UnwrapData(packet);
                string tempFileName = Path.GetTempFileName();
                await File.WriteAllBytesAsync(tempFileName, fileData);
 
                lock (_lock)
                {
                    if (!_memoryMappedFiles.ContainsKey(tempFileName))
                    {
                        var memoryMappedFile = MemoryMappedFile.CreateFromFile(tempFileName, FileMode.Open, tempFileName);
                        _memoryMappedFiles[tempFileName] = memoryMappedFile;
                        DebugMessage($"\nLoaded file into memory-mapped file.\n", ConsoleColor.Magenta);
                    }
                    else
                    {
                        DebugMessage($"\nFile is already loaded as a memory-mapped file.\n", ConsoleColor.Yellow);
                    }
                }
            }

            
            }

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