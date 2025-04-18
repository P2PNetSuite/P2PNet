using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using P2PNet.Distribution.NetworkTasks;
using P2PNet.NetworkPackets;

namespace P2PNet.Distribution
{
    public static class NetworkTaskHandler
    {

        /// <summary>
        /// Queue for outgoing data packets to be distributed to trusted peers.
        /// </summary>
        public static ConcurrentQueue<NetworkTask> outgoingNetworkTasks = new ConcurrentQueue<NetworkTask>();

        /// <summary>
        /// Queue for incoming data packets to be processed.
        /// </summary>
        public static ConcurrentQueue<NetworkTask> incomingNetworkTasks = new ConcurrentQueue<NetworkTask>();

        private static Timer _outboundChecker;
        private static Timer _queueChecker;

        static NetworkTaskHandler()
        {
            _outboundChecker = new System.Timers.Timer(500); // half second
            _outboundChecker.Elapsed += HandleOutgoingData;
            _outboundChecker.AutoReset = true;
            _outboundChecker.Enabled = true;

            _queueChecker = new System.Timers.Timer(500); // half second
            _queueChecker.Elapsed += HandleIncomingNetworkTasks;
            _queueChecker.AutoReset = true;
            _queueChecker.Enabled = true;
        }

        internal static void HandleOutgoingData(System.Object source, ElapsedEventArgs e)
        {
            if(outgoingNetworkTasks.IsEmpty) 
                return;

            while (!outgoingNetworkTasks.IsEmpty)
            {
                if (outgoingNetworkTasks.TryDequeue(out NetworkTask task))
                {
                    // target recipients are designated by the "Recipient" key in the TaskData dictionary
                    if (task.TaskData != null && task.TaskData.ContainsKey("Recipient"))
                    {
                        string recipient = task.TaskData["Recipient"];
                        if (recipient != null)
                        {
                            var targetRecipient = PeerNetwork.ActivePeerChannels.FirstOrDefault(x => x.peer.Identifier == recipient);
                            if (targetRecipient != null)
                            {
                                targetRecipient.LoadOutgoingData(new DataTransmissionPacket(task.ToByte(), DataPayloadFormat.Task));
                            }
                        }
                        // if we cannot easily find a recipient we will just let the NetworkTask dispose of itself
                    }
                }
            }
        }

        private static async void HandleIncomingNetworkTasks(System.Object source, ElapsedEventArgs e)
        {
            while(!incomingNetworkTasks.IsEmpty)
            {
                if (incomingNetworkTasks.TryDequeue(out NetworkTask task))
                {
                    switch (task.TaskType)
                    {
                        case TaskType.BlockAndRemovePeer:
                            // Logic to block and remove a peer
                            break;
                        case TaskType.BlockIP:
                            // Logic to block an IP address
                            break;
                        case TaskType.SendMessage:
                            // Logic to send a message
                            break;
                        case TaskType.PingPeer:
                            // Logic to ping a peer
                            break;
                        case TaskType.DisconnectPeer:
                            // Logic to disconnect a peer
                            break;
                        case TaskType.AuthorizePeer:
                            // Logic to authorize a peer
                            break;
                        default:
                            throw new NotSupportedException($"Task type {task.TaskType} is not supported.");
                    }
                }
            }
        }

    }
}
