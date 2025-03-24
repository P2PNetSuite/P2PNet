using P2PNet.Distribution;
using P2PNet.NetworkPackets;
using P2PNet.Peers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static P2PNet.Distribution.DistributionProtocol;
using static P2PNet.Distribution.DistributionHandler;
using static P2PNet.PeerNetwork;

namespace P2PNet.DicoveryChannels
    {

    internal static class BroadcastRateControl // Variabalize the broadcast rate like a sine wave
        {
        private static readonly Random random = new Random();
        private static System.Timers.Timer intervalTimer;

        private const double MIN_INTERVAL = 20000;  // 20 seconds
        private const double MAX_INTERVAL = 1000;    // 1 second
        private const double WAVE_DURATION = 2 * 60 * 1000; // 4 minutes

        internal static double currentInterval = 2000;

        static BroadcastRateControl()
            {
            intervalTimer = new System.Timers.Timer(1000);  // Update interval frequently
            intervalTimer.Elapsed += UpdateInterval;
            intervalTimer.AutoReset = true;
            intervalTimer.Start();
            }

        private static void UpdateInterval(object sender, System.Timers.ElapsedEventArgs e)
            {
            // Calculate new interval based on sine wave
            double time = Environment.TickCount64 & Int32.MaxValue; // Get time reliably
            double sineValue = Math.Sin(2 * Math.PI * time / WAVE_DURATION);
            currentInterval = (MAX_INTERVAL - MIN_INTERVAL) * (sineValue + 1) / 2 + MIN_INTERVAL;
            }

        internal static int GetCurrentInterval()
            {
            int variation_ = random.Next(100, 300);
            return (int)currentInterval + variation_;
            }
        public static void DownThrottle()
            {
            currentInterval = MIN_INTERVAL;
            intervalTimer.Interval += 10000;
            }
        }

    internal abstract class LANDiscoveryChannelBase
        {

        public static Queue<int> dutypackets = new Queue<int>();
        internal static Random randomizer = new Random();
        public readonly IdentifierPacket packet_ = new IdentifierPacket("discovery", ListeningPort, PeerNetwork.LocalIPV4Address);
        public static CollectionSharePacket collectionpacket_;

        public static IPEndPoint listenerendpoint;
        public static IPEndPoint broadcasterendpoint;

        /// <summary>
        /// The time when the channel was created.
        /// </summary>
        public DateTime ChannelCreatedTime { get; init; } = DateTime.Now; // record time created

        public virtual async Task StartBroadcaster(CancellationToken cancellationToken) { }
        internal CancellationTokenSource cancelBroadcaster;
        public virtual async Task StartListener(CancellationToken cancellationToken) { }
        public virtual async Task StartListener(CancellationToken cancellationToken, int port) { } // port-designated overload
        internal CancellationTokenSource cancelListener;

        public LANDiscoveryChannelBase()
            {
            collectionpacket_ = new CollectionSharePacket();

            }

        public void HandlePacket(string packet)
            {
            var identifierPacket = Deserialize<IdentifierPacket>(packet);
            var collectionPacket = Deserialize<CollectionSharePacket>(packet);
            if (identifierPacket != null)
                {

            //    DebugMessage($"Identifier packet received: {identifierPacket.Data} from {identifierPacket.IP}" + Environment.NewLine + $"\t\t\tSecret Port: {identifierPacket.Data}");

                IPeer newPeer = new GenericPeer(IPAddress.Parse(identifierPacket.IP), identifierPacket.Data);
                PeerNetwork.AddPeer(newPeer);
                }
            else if (collectionPacket != null)
                {

                DebugMessage($"Packet received: {identifierPacket.Message}");

                PeerNetwork.ProcessPeerList(collectionPacket);
                }
            }

        public void DownThrottle()
            {
            BroadcastRateControl.DownThrottle();
            }

        private static double RandomizeInterval()
            {
            return randomizer.Next(3 * 60000, 5 * 60000); // 3-5 minutes in milliseconds
            }


        public int CreateTimeVariation(int min, int max) { return randomizer.Next(min, max); }
        private string packet_serialized()
            {
            string ps_ = Serialize<IdentifierPacket>(packet_);
            return ps_;
            //return System.Text.Json.JsonSerializer.Serialize(packet_, typeof(IdentifierPacket), new IdentifierPacketContext());
            }
        public byte[] UniqueIdentifier() { string temp_ = packet_serialized(); return Encoding.UTF8.GetBytes(temp_); }

        private string colpacket_serialized()
            {
            string colp_ = Serialize<IdentifierPacket>(packet_);
            return colp_;
            }
        public byte[] CollectionShare() { string temp_ = colpacket_serialized(); return Encoding.UTF8.GetBytes(temp_); }

        public async Task DutyPacketGenerator()
            {
            int a = 1;
            dutypackets.Enqueue(a);
            }


        }
       
    }
