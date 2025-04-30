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
    public static partial class NetworkTaskHandler
    {

        /// <summary>
        /// Queue for outgoing data packets to be distributed to trusted peers.
        /// </summary>
        private static ConcurrentQueue<NetworkTask> outgoingNetworkTasks = new ConcurrentQueue<NetworkTask>();

        /// <summary>
        /// Queue for incoming data packets to be processed.
        /// </summary>
        private static ConcurrentQueue<(NetworkTask, NetworkTaskOriginInfo)> incomingNetworkTasks = new ConcurrentQueue<(NetworkTask, NetworkTaskOriginInfo)>();

        private static Timer _outboundChecker;
        private static Timer _queueChecker;

        static NetworkTaskHandler()
        {
            delegateLib = new Dictionary<TaskType, NetworkTaskDelegate>()
            {
                { TaskType.BlockAndRemovePeer, WrapWithChecks(BlockAndRemovePeerHandler) },
                { TaskType.BlockIP, WrapWithChecks(BlockIPHandler) },
                { TaskType.SendMessage, WrapWithChecks(SendMessageHandler) },
                { TaskType.PingPeer, WrapWithChecks(PingPeerHandler) },
                { TaskType.DisconnectPeer, WrapWithChecks(DisconnectPeerHandler) },
                { TaskType.AuthorizePeer, WrapWithChecks(AuthorizePeerHandler) },
                { TaskType.RevokePeerAuthorization, WrapWithChecks(RevokePeerAuthorizationHandler) },
                { TaskType.RequestData, WrapWithChecks(RequestDataHandler) },
                { TaskType.SendData, WrapWithChecks(SendDataHandler) },
                { TaskType.SynchronizeData, WrapWithChecks(SynchronizeDataHandler) },
                { TaskType.UpdateSettings, WrapWithChecks(UpdateSettingsHandler) },
                { TaskType.RequestVerifyHashRecord, WrapWithChecks(RequestVerifyHashRecordHandler) },
                { TaskType.RequestVerifySignature, WrapWithChecks(RequestVerifySignatureHandler) },
                { TaskType.RequestPublicKey, WrapWithChecks(RequestPublicKeyHandler) },
                { TaskType.BootstrapInitialization, WrapWithChecks(BootstrapInitializationHandler) },
                { TaskType.Heartbeat, WrapWithChecks(HeartbeatHandler) },
                { TaskType.HeartbeatResponse, WrapWithChecks(HeartbeatResponseHandler) },
                { TaskType.SetLocalIdentifier, WrapWithChecks(SetLocalIdentifierHandler) },
                { TaskType.AssignIdentifierToPeer, WrapWithChecks(AssignIdentifierToPeerHandler) }
            };

            _outboundChecker = new System.Timers.Timer(500); // half second
            _outboundChecker.Elapsed += HandleOutgoingData;
            _outboundChecker.AutoReset = true;
            _outboundChecker.Enabled = true;
            _outboundChecker.Start();

            _queueChecker = new System.Timers.Timer(500); // half second
            _queueChecker.Elapsed += HandleIncomingNetworkTasks;
            _queueChecker.AutoReset = true;
            _queueChecker.Enabled = true;
            _queueChecker.Start();
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
                if (incomingNetworkTasks.TryDequeue(out var outTuple))
                {
                    try
                    {
                        var task = outTuple.Item1;
                        var originInfo = outTuple.Item2;
                        if (delegateLib.TryGetValue(task.TaskType, out var handler) && handler != null)
                        {
                            handler.Invoke(task, originInfo);
                        }
                        else
                        {
                             DebugMessage($"No delegate found for task type: {task.TaskType}", MessageType.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                         DebugMessage(ex.ToString(), MessageType.Critical);
                    }
                }
            }
        }

        #region Public Methods
        /// <summary>
        /// Enqueues a network task for outgoing distribution to trusted peers.
        /// </summary>
        public static void EnqueueOutgoingNetworkTask(NetworkTask task)
        {
            outgoingNetworkTasks.Enqueue(task);
        }
        /// <summary>
        /// Enqueues a network task for incoming processing.
        /// </summary>
        public static void EnqueueIncomingNetworkTask(NetworkTask task, NetworkTaskOriginInfo originInfo)
        {
            incomingNetworkTasks.Enqueue((task, originInfo));
        }

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
        
            /// <summary>
            /// Creates a network task to block and remove a peer.
            /// </summary>
            /// <param name="targetIdentifier">The identifier of the peer to block and remove.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.BlockAndRemovePeer"/>.</returns>
            public static NetworkTask CreateBlockAndRemovePeerTask(string targetIdentifier)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.BlockAndRemovePeer,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["TargetIdentifier"] = targetIdentifier;
                return task;
            }

            /// <summary>
            /// Creates a network task to block a specific IP address.
            /// </summary>
            /// <param name="targetIP">The IP address to block.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.BlockIP"/>.</returns>
            public static NetworkTask CreateBlockIPTask(string targetIP)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.BlockIP,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["TargetIP"] = targetIP;
                return task;
            }

            /// <summary>
            /// Creates a network task to send a message to a designated recipient.
            /// </summary>
            /// <param name="recipient">The identifier of the recipient peer.</param>
            /// <param name="message">The message to send.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.SendMessage"/>.</returns>
            public static NetworkTask CreateSendMessageTask(string recipient, string message)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.SendMessage,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["Recipient"] = recipient;
                task.TaskData["Message"] = message;
                return task;
            }

            /// <summary>
            /// Creates a network task to send a ping to a peer.
            /// </summary>
            /// <param name="recipient">The identifier of the peer to ping.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.PingPeer"/>.</returns>
            public static NetworkTask CreatePingTask(string recipient)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.PingPeer,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["Recipient"] = recipient;
                return task;
            }

            /// <summary>
            /// Creates a network task to disconnect a specific peer.
            /// </summary>
            /// <param name="targetIdentifier">The identifier of the peer to disconnect.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.DisconnectPeer"/>.</returns>
            public static NetworkTask CreateDisconnectPeerTask(string targetIdentifier)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.DisconnectPeer,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["TargetIdentifier"] = targetIdentifier;
                return task;
            }

            /// <summary>
            /// Creates a network task to authorize a peer.
            /// </summary>
            /// <param name="targetIdentifier">The identifier of the peer to authorize.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.AuthorizePeer"/>.</returns>
            public static NetworkTask CreateAuthorizePeerTask(string targetIdentifier)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.AuthorizePeer,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["TargetIdentifier"] = targetIdentifier;
                return task;
            }

            /// <summary>
            /// Creates a network task to revoke a peer's authorization.
            /// </summary>
            /// <param name="targetIdentifier">The identifier of the peer whose authorization is to be revoked.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.RevokePeerAuthorization"/>.</returns>
            public static NetworkTask CreateRevokePeerAuthorizationTask(string targetIdentifier)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.RevokePeerAuthorization,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["TargetIdentifier"] = targetIdentifier;
                return task;
            }

            /// <summary>
            /// Creates a network task to request specific data from a peer.
            /// </summary>
            /// <param name="dataKey">The key identifying the data being requested.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.RequestData"/>.</returns>
            public static NetworkTask CreateRequestDataTask(string dataKey)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.RequestData,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["DataKey"] = dataKey;
                return task;
            }

            /// <summary>
            /// Creates a network task to send data to a peer.
            /// </summary>
            /// <param name="recipient">The identifier of the recipient peer.</param>
            /// <param name="data">The data to send (serialized as a string).</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.SendData"/>.</returns>
            public static NetworkTask CreateSendDataTask(string recipient, string data)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.SendData,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["Recipient"] = recipient;
                task.TaskData["Data"] = data;
                return task;
            }

            /// <summary>
            /// Creates a network task to synchronize data between peers.
            /// </summary>
            /// <param name="dataKey">The key identifying the data to synchronize.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.SynchronizeData"/>.</returns>
            public static NetworkTask CreateSynchronizeDataTask(string dataKey)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.SynchronizeData,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["DataKey"] = dataKey;
                return task;
            }

            /// <summary>
            /// Creates a network task to update network or peer settings.
            /// </summary>
            /// <param name="settings">A dictionary containing the settings key/value pairs to update.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.UpdateSettings"/>.</returns>
            public static NetworkTask CreateUpdateSettingsTask(Dictionary<string, string> settings)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.UpdateSettings,
                    TaskData = new Dictionary<string, string>()
                };

                foreach (var kvp in settings)
                {
                    task.TaskData[kvp.Key] = kvp.Value;
                }

                return task;
            }

            /// <summary>
            /// Creates a network task to request verification of a hash record.
            /// </summary>
            /// <param name="hashToVerify">The hash value that needs to be verified.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.RequestVerifyHashRecord"/>.</returns>
            public static NetworkTask CreateRequestVerifyHashRecordTask(string hashToVerify)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.RequestVerifyHashRecord,
                    TaskData = new Dictionary<string, string>(){
                        { "Hash", hashToVerify }
                    }
                };
                return task;
            }

            /// <summary>
            /// Creates a network task to verify a PGP signature.
            /// </summary>
            /// <param name="signature">The clear-signed PGP signature to verify.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.RequestVerifySignature"/>.</returns>
            public static NetworkTask CreateRequestVerifySignatureTask(string signature)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.RequestVerifySignature,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["Signature"] = signature;
                return task;
            }

            /// <summary>
            /// Creates a network task to request the public key of a peer or bootstrap server.
            /// </summary>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.RequestPublicKey"/>.</returns>
            public static NetworkTask CreateRequestPublicKeyTask()
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.RequestPublicKey,
                    TaskData = new Dictionary<string, string>()
                };
                return task;
            }

            /// <summary>
            /// Creates a network task for bootstrap initialization.
            /// </summary>
            /// <param name="publicKey">The public key of the bootstrap server.</param>
            /// <param name="peerListJson">A JSON serialized list of peers included in the bootstrap initialization.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.BootstrapInitialization"/>.</returns>
            public static NetworkTask CreateBootstrapInitializationTask(string publicKey, string peerListJson)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.BootstrapInitialization,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["PublicKey"] = publicKey;
                task.TaskData["PeerList"] = peerListJson;
                return task;
            }

            /// <summary>
            /// Creates a network task to send a heartbeat signal.
            /// </summary>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.Heartbeat"/>.</returns>
            public static NetworkTask CreateHeartbeatTask()
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.Heartbeat,
                    TaskData = new Dictionary<string, string>()
                };
                return task;
            }

            /// <summary>
            /// Creates a network task for sending a heartbeat response.
            /// </summary>
            /// <param name="responseData">Additional response data or information to include.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.HeartbeatResponse"/>.</returns>
            public static NetworkTask CreateHeartbeatResponseTask(string responseData)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.HeartbeatResponse,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["Response"] = responseData;
                return task;
            }

            /// <summary>
            /// Creates a network task to set the local identifier.
            /// </summary>
            /// <param name="identifier">The identifier to assign locally.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.SetLocalIdentifier"/>.</returns>
            public static NetworkTask CreateSetLocalIdentifierTask(string identifier)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.SetLocalIdentifier,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["Identifier"] = identifier;
                return task;
            }

            /// <summary>
            /// Creates a network task to assign a new identifier to a peer.
            /// </summary>
            /// <param name="targetIdentifier">The identifier of the peer whose identifier is to be changed.</param>
            /// <param name="newIdentifier">The new identifier to assign.</param>
            /// <returns>A <see cref="NetworkTask"/> with type <see cref="TaskType.AssignIdentifierToPeer"/>.</returns>
            public static NetworkTask CreateAssignIdentifierToPeerTask(string targetIdentifier, string newIdentifier)
            {
                var task = new NetworkTask
                {
                    TaskType = TaskType.AssignIdentifierToPeer,
                    TaskData = new Dictionary<string, string>()
                };
                task.TaskData["TargetIdentifier"] = targetIdentifier;
                task.TaskData["NewIdentifier"] = newIdentifier;
                return task;
            }

    }


    #endregion

}
