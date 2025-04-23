using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
using P2PNet.Distribution.NetworkTasks;
using P2PNet.NetworkPackets;

namespace P2PNet.Distribution
{
    /// <summary>
    /// Provides static methods and queues for managing network tasks within the peer-to-peer distribution system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This static class maintains two concurrent queues—one for outgoing network tasks to be sent to trusted peers,
    /// and one for incoming network tasks that need to be processed. It uses timers to periodically check these queues
    /// and invoke appropriate handlers to process tasks based on their <see cref="TaskType"/>. Tasks may include actions
    /// such as blocking a peer, sending messages, pinging, and disconnecting peers.
    /// </para>
    /// <para>
    /// The class also exposes a helper method to extract the PGP clear-signed signature from a network task, which is used
    /// for validation and verification purposes.
    /// </para>
    /// </remarks>
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

        #region Public Methods

        public static async Task<string> ExtractSignatureFromNetworkTask(NetworkTask networkTask)
        {
            string task = Encoding.UTF8.GetString(networkTask.ToByte());
            using (JsonDocument doc = JsonDocument.Parse(task))
            {
                if (doc.RootElement.TryGetProperty("TaskData", out JsonElement taskData))
                {
                    if (taskData.TryGetProperty("Signature", out JsonElement signatureElement))
                    {
                        string signature = signatureElement.GetString();
                        string originalSignature = EncryptionAndSecurityHandler.HexStringToOriginal(signature);
                        return originalSignature;
                    }
                    else
                    {
                        return string.Empty;
                    }
                }
                else
                {
                    return string.Empty;
                }
            }
        }
        #endregion

    }
}
