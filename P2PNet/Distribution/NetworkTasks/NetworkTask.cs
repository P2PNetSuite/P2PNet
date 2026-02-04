using P2PNet.Distribution.NetworkTasks;
using P2PNet.NetworkPackets;
using P2PNet.NetworkPackets.NetworkPacketBase.NetworkPacketBase;
using P2PNet.Peers;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Serialization;

namespace P2PNet.Distribution.NetworkTasks
    {
    /// <summary>
    /// Represents a network task that can be executed within the peer-to-peer network.
    /// </summary>
    /// <remarks>
    /// A network task defines an action to be performed, such as blocking a peer, sending a message, or synchronizing data.
    /// Each task is identified by a <see cref="TaskType"/> and can include additional data in the form of key-value pairs.
    /// </remarks>
    public sealed class NetworkTask
    {
        public TaskType TaskType { get; set; }
        public Dictionary<string, string> TaskData { get; set; }

        [JsonConstructor]
        public NetworkTask() { }
        public byte[] ToByte()
        {
            return Encoding.UTF8.GetBytes(Serialize(this));
        }
    }

    /// <summary>
    /// Represents temporary metadata about the origin of a <see cref="NetworkTask"/>.
    /// </summary>
    /// <remarks>
    /// This struct is used exclusively within the <see cref="NetworkTaskHandler.EnqueueIncomingNetworkTask"/> method
    /// to provide additional context about the origin of a <see cref="NetworkTask"/>. The metadata includes the source
    /// identifier and IP address of the task's origin, which can be used to perform trust checks or other validations
    /// as per the task's <see cref="TaskTrustParameter"/> requirements.
    /// <para>
    /// The values in this struct are not persisted and are only intended for temporary use during the processing
    /// of the network task.
    /// </para>
    /// </remarks>
    public readonly struct NetworkTaskOriginInfo
    {
        /// <summary>
        /// Gets the identifier of the source origin that created the <see cref="NetworkTask"/>.
        /// </summary>
        /// <remarks>
        /// This value is typically derived from the <see cref="INetworkPacket.SourceOriginIdentifier"/> property
        /// of the packet that initiated the task.
        /// </remarks>
        public string SourceOriginIdentifier { get; }
        /// <summary>
        /// Gets the IP address of the source origin that created the <see cref="NetworkTask"/>.
        /// </summary>
        /// <remarks>
        /// This value is resolved using the <see cref="PeerNetwork.GetPeerByIdentifier"/> method, which maps the
        /// source origin identifier to a known peer's IP address. If no matching peer is found, this value will
        /// be an empty string.
        /// </remarks>
        public string IP { get; }
        /// <summary>
        /// Gets the address (typically URL) of the source origin.
        /// </summary>
        public string Address { get; }
        /// <summary>
        /// Gets the public key of the sender that created the <see cref="NetworkTask"/>.
        /// </summary>
        public string SenderPublicKey { get; }
        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkTaskOriginInfo"/> struct with the specified source origin identifier and IP address.
        /// </summary>
        /// <param name="sourceOrigin">The identifier of the source origin.</param>
        /// <param name="ip">The IP address of the source origin.</param>
        public NetworkTaskOriginInfo(string sourceOrigin, string ip)
        {
            SourceOriginIdentifier = sourceOrigin;
            IP = ip;
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkTaskOriginInfo"/> struct using an <see cref="INetworkPacket"/>.
        /// </summary>
        /// <param name="packet">The network packet containing the source origin information.</param>
        /// <remarks>
        /// This constructor extracts the <see cref="SourceOriginIdentifier"/> from the packet's
        /// <see cref="INetworkPacket.SourceOriginIdentifier"/> property and attempts to resolve the corresponding
        /// IP address using the <see cref="PeerNetwork.GetPeerByIdentifier"/> method.
        /// </remarks>
        public NetworkTaskOriginInfo(INetworkPacket packet)
        {
            SourceOriginIdentifier = packet.SourceOriginIdentifier;
            var peer = PeerNetwork.GetPeerByIdentifier(packet.SourceOriginIdentifier);
            if(peer != null)
            {
                IP = peer?.IP?.ToString();
            }
            else
            {
                IP = string.Empty;
            }
            Address = string.Empty;
            SenderPublicKey = string.Empty;
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkTaskOriginInfo"/> struct using an <see cref="INetworkPacket"/>
        /// and the sender's public key.
        /// </summary>
        /// <param name="packet">The network packet containing the source origin information.</param>
        /// <param name="publicKey">The public key of the sender.</param>
        /// <remarks>
        /// This constructor extracts the <see cref="SourceOriginIdentifier"/> from the packet's
        /// <see cref="INetworkPacket.SourceOriginIdentifier"/> property, attempts to resolve the corresponding
        /// IP address using the <see cref="PeerNetwork.GetPeerByIdentifier"/> method, and assigns the provided public key.
        /// The <see cref="Address"/> property remains empty.
        /// </remarks>
        public NetworkTaskOriginInfo(INetworkPacket packet, string publicKey)
        {
            SourceOriginIdentifier = packet.SourceOriginIdentifier;
            var peer = PeerNetwork.GetPeerByIdentifier(packet.SourceOriginIdentifier);
            IP = peer != null ? peer.IP?.ToString() ?? string.Empty : string.Empty;
            Address = string.Empty;
            SenderPublicKey = publicKey;
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkTaskOriginInfo"/> struct using an <see cref="IPeer"/>.
        /// </summary>
        /// <param name="peer">The peer instance representing the task's origin.</param>
        /// <remarks>
        /// This constructor sets the source origin identifier, IP address, and address properties using the provided <see cref="IPeer"/>.
        /// The <see cref="SenderPublicKey"/> is set to an empty string.
        /// </remarks>
        public NetworkTaskOriginInfo(IPeer peer)
        {
            SourceOriginIdentifier = peer.Identifier;
            IP = peer.IP?.ToString() ?? string.Empty;
            Address = peer.Address;
            SenderPublicKey = string.Empty;
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkTaskOriginInfo"/> struct using an <see cref="IPeer"/>
        /// and the sender's public key.
        /// </summary>
        /// <param name="peer">The peer instance representing the task's origin.</param>
        /// <param name="publicKey">The public key of the sender.</param>
        /// <remarks>
        /// This constructor sets the source origin identifier, IP address, and address properties
        /// using the provided <see cref="IPeer"/> and assigns the sender's public key.
        /// </remarks>
        public NetworkTaskOriginInfo(IPeer peer, string publicKey)
        {
            SourceOriginIdentifier = peer.Identifier;
            IP = peer.IP?.ToString() ?? string.Empty;
            Address = peer.Address;
            SenderPublicKey = publicKey;
        }
    }

    /// <summary>
    /// Defines the types of tasks that can be executed within the peer-to-peer network.
    /// </summary>
    public enum TaskType
    {
        /// <summary>
        /// Block a peer and removes it from the network.
        /// </summary>
        BlockAndRemovePeer,

        /// <summary>
        /// Block a specific IP address from connecting to the network.
        /// </summary>
        BlockIP,

        /// <summary>
        /// Send a message to a specific peer or group of peers.
        /// </summary>
        SendMessage,

        /// <summary>
        /// Send a ping to a specific peer to check its availability.
        /// </summary>
        PingPeer,

        /// <summary>
        /// Disconnect a specific peer from the network.
        /// </summary>
        DisconnectPeer,

        /// <summary>
        /// Authorize a peer to perform certain actions or access certain resources.
        /// </summary>
        AuthorizePeer,

        /// <summary>
        /// Revoke the authorization of a peer.
        /// </summary>
        RevokePeerAuthorization,

        /// <summary>
        /// Request specific data from a peer.
        /// </summary>
        RequestData,

        /// <summary>
        /// Send specific data to a peer.
        /// </summary>
        SendData,

        /// <summary>
        /// Synchronize data between peers.
        /// </summary>
        SynchronizeData,

        /// <summary>
        /// Update network settings or peer settings.
        /// </summary>
        UpdateSettings,

        /// <summary>
        /// A request to verify the existence of a hash record.
        /// </summary>
        RequestVerifyHashRecord,

        /// <summary>
        /// Verify the PGP signature of a message or command.
        /// </summary>
        RequestVerifySignature,

        /// <summary>
        /// Request the public key of a peer or bootstrap server.
        /// </summary>
        RequestPublicKey,

        /// <summary>
        /// Send the public key and peer list to the peer from the bootstrap server.
        /// </summary>
        BootstrapInitialization,

        /// <summary>
        /// Send a heartbeat signal to a bootstrap server.
        /// </summary>
        /// <remarks>This can be useful with bootstrap servers to track if peers are still live or drop off the network.</remarks>
        Heartbeat,

        /// <summary>
        /// Send a heartbeat response to a client peer.
        /// </summary>
        /// <remarks>This will return any NetworkTasks queued for the client peer.</remarks>
        HeartbeatResponse,

        /// <summary>
        /// Set the local identifier to the specified value.
        /// </summary>
        /// <remarks>This can be useful with the Authority trust policy to assign unique IDs to peers.</remarks>
        SetLocalIdentifier,

        /// <summary>
        /// Set the identifier of a peer to the specified value.
        /// </summary>
        AssignIdentifierToPeer,

        /// <summary>
        /// Request the bootstrap server to facilitate a WebRTC connection between two peers.
        /// </summary>
        RequestWebRTCConnection,

        WebRTCOffer,

        WebRTCAnswer,

        WebRTCIceCandidate,

        /// <summary>
        /// Request to initiate a TURN relay connection through the bootstrap server.
        /// </summary>
        TurnConnectionRequest,

        /// <summary>
        /// Notification that a TURN relay connection has been established.
        /// </summary>
        TurnConnectionEstablished,

        /// <summary>
        /// Notification that a TURN relay connection has been closed.
        /// </summary>
        TurnConnectionClosed,

        /// <summary>
        /// Data payload being relayed through a TURN connection.
        /// </summary>
        TurnRelayData,

        /// <summary>
        /// Server-sent event stream event containing pending tasks for the peer.
        /// </summary>
        StreamEvent,

        /// <summary>
        /// Keep-alive signal for SSE stream connections.
        /// </summary>
        StreamKeepAlive
    }

    /// <summary>
    /// Represents the trust requirement for executing a network task as a parameter.
    /// </summary>
    public enum TaskTrustParameter
    {
        /// <summary>
        /// Task is totally open. No checks required.
        /// </summary>
        Open,
        /// <summary>
        /// Task is allowed if the sender is a Trusted Peer.
        /// </summary>
        TrustedPeer,
        /// <summary>
        /// Task may come from an Authority Bootstrap Server.
        /// </summary>
        AuthorityBootstrapServer,
        /// <summary>
        /// Task must have a valid signed hash.
        /// </summary>
        MustHaveSignedHash,
        /// <summary>
        /// Task should be ignored regardless of source.
        /// </summary>
        IgnoreAll
    }

    /// <summary>
    /// Represents a mapping entry between a task type and its required trust parameters.
    /// </summary>
    public readonly struct TaskTrustMappingEntry
    {
        /// <summary>
        /// Gets the task type.
        /// </summary>
        public TaskType TaskType { get; }

        /// <summary>
        /// Gets the allowed trust parameters for the task type.
        /// </summary>
        public TaskTrustParameter[] Requirements { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskTrustMappingEntry"/> struct.
        /// </summary>
        /// <param name="taskType">The task type.</param>
        /// <param name="requirements">The allowed trust parameters for this task type.</param>
        public TaskTrustMappingEntry(TaskType taskType, params TaskTrustParameter[] requirements)
        {
            TaskType = taskType;
            Requirements = requirements;
        }
    }

    /// <summary>
    /// Configures the trust requirements for each network task type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class uses a dictionary mapping each <see cref="TaskType"/> to an array of allowed <see cref="TaskTrustParameter"/> values.
    /// The configuration can be initialized using a set of mapping entries provided via the <c>params</c> keyword.
    /// </para>
    /// <para>
    /// For instance, you could initialize it like so:
    /// <code language="csharp">
    /// var config = new NetworkTaskTrustConfiguration(
    ///     new TaskTrustMappingEntry(TaskType.SendMessage, TaskTrustParameter.Open),
    ///     new TaskTrustMappingEntry(TaskType.BlockIP, TaskTrustParameter.TrustedPeer, TaskTrustParameter.AuthorityBootstrapServer),
    ///     new TaskTrustMappingEntry(TaskType.UpdateSettings, TaskTrustParameter.MustHaveSignedHash)
    /// );
    /// </code>
    /// </para>
    /// </remarks>
    public class NetworkTaskTrustConfiguration
    {
        /// <summary>
        /// Gets the mapping between TaskType values and their allowed TaskTrustParameter values.
        /// </summary>
        public Dictionary<TaskType, TaskTrustParameter[]> TaskTrustMapping { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkTaskTrustConfiguration"/> class with the specified trust parameters.
        /// </summary>
        /// <remarks>
        /// <param name="entries">
        /// An array of <see cref="TaskTrustMappingEntry"/> values specifying the trust parameters for each task type.
        /// </param>
        /// <para>
        /// For any <see cref="TaskType"/> not explicitly declared in the provided entries, default trust requirements of
        /// <see cref="TaskTrustParameter.TrustedPeer"/> and <see cref="TaskTrustParameter.AuthorityBootstrapServer"/> are assigned.
        /// This ensures that tasks not explicitly configured require elevated trust by default.
        /// </para>
        /// </remarks>
        public NetworkTaskTrustConfiguration(params TaskTrustMappingEntry[] entries)
        {
            TaskTrustMapping = new Dictionary<TaskType, TaskTrustParameter[]>();

            foreach (var entry in entries)
            {
                TaskTrustMapping[entry.TaskType] = entry.Requirements.ToArray();
            }
            foreach(Enum item in Enum.GetValues(typeof(TaskType)))
            {
                if (!TaskTrustMapping.ContainsKey((TaskType)item))
                {
                    // we assume some elevated trust should be required for any task not explicitly defined
                    TaskTrustMapping[(TaskType)item] = new[] { TaskTrustParameter.TrustedPeer, TaskTrustParameter.AuthorityBootstrapServer };
                }
            }

        }

        /// <summary>
        /// Updates the trust parameters for the specified task type.
        /// </summary>
        /// <param name="taskType">The task type to update.</param>
        /// <param name="requirements">The allowed trust requirements for the task type.</param>
        public void UpdateTrustParams(TaskType taskType, params TaskTrustParameter[] requirements)
        {
            TaskTrustMapping[taskType] = requirements;
        }

        /// <summary>
        /// Determines whether the specified trust requirement is allowed for the given task type.
        /// </summary>
        /// <param name="taskType">The task type to check.</param>
        /// <param name="currentRequirement">The current trust parameter of the sender.</param>
        /// <returns><c>true</c> if the current trust requirement is permitted; otherwise, <c>false</c>.</returns>
        public bool IsRequirementAllowed(TaskType taskType, TaskTrustParameter currentRequirement)
        {
            if (TaskTrustMapping.TryGetValue(taskType, out var allowed))
            {
                return allowed.Contains(currentRequirement);
            }
            return false;
        }
    }
}

namespace P2PNet.Distribution
{
    public static partial class NetworkTaskHandler
    {
        // here we pair each TaskType with its corresponding delegate for processing
        // this is for clean management and easy invocation
        // ( this is set in the constructor )
        private static readonly Dictionary<TaskType, NetworkTaskDelegate> delegateLib;

        #region Task Trust Checks

        private static bool CheckTrustedPeer(ref NetworkTask task, NetworkTaskOriginInfo info)
        {
            return PeerNetwork.TrustedPeerChannels != null &&
                   PeerNetwork.TrustedPeerChannels.Any(channel =>
                       channel != null &&
                       channel.peer != null &&
                       !string.IsNullOrEmpty(channel.peer.Identifier) &&
                       channel.peer.Identifier.Equals(info.SourceOriginIdentifier, StringComparison.Ordinal));
        }

        private static bool CheckAuthorityBootstrapServer(ref NetworkTask task, NetworkTaskOriginInfo info)
        {
            return PeerNetwork.ActiveBootstrapChannels.Any(channel => channel.PublicKey == info.SenderPublicKey);
        }

        private static async Task<bool> CheckMustHaveSignedHash(NetworkTask task, NetworkTaskOriginInfo info)
        {
           
            var bootstrapServer = PeerNetwork.ActiveBootstrapChannels.FirstOrDefault(x => x.PublicKey == info.SenderPublicKey);

            if (bootstrapServer == null)
            {
                DebugMessage($"No bootstrap server found for task {task.TaskType} from {info.SourceOriginIdentifier}.", MessageType.Warning);
                return false;
            }

            return await bootstrapServer.IsValidNetworkHash(task);
        }

        private static bool CheckTaskParameters(ref NetworkTask task, ref NetworkTaskOriginInfo info)
        {
            if (!PeerNetwork.TrustPolicies.PeerNetworkTrustPolicy.NetworkTaskTrustSettings.TaskTrustMapping.TryGetValue(task.TaskType, out TaskTrustParameter[] requiredParams))
            {
                DebugMessage($"Task {task.TaskType} from {info.SourceOriginIdentifier} has no trust mapping.", MessageType.Warning);
                return false;
            }

            if (requiredParams.Contains(TaskTrustParameter.IgnoreAll))
            {
                // DebugMessage($"Task {task.TaskType} from {info.SourceOriginIdentifier} is ignored.", MessageType.Warning);
                return false;
            }

            // If the Open flag is set, no checks are needed.
            if (requiredParams.Contains(TaskTrustParameter.Open))
            {
                return true;
            }

            bool requiresOptionalTrust = false;
            if (requiredParams.Contains(TaskTrustParameter.TrustedPeer))
            {
                requiresOptionalTrust = true;
            }
            if (requiredParams.Contains(TaskTrustParameter.AuthorityBootstrapServer) &&
                !requiredParams.Contains(TaskTrustParameter.MustHaveSignedHash))
            {
                requiresOptionalTrust = true;
            }

            if (requiresOptionalTrust)
            {
                if (!(CheckTrustedPeer(ref task, info) || CheckAuthorityBootstrapServer(ref task, info)))
                {
                    DebugMessage($"Task {task.TaskType} from {info.SourceOriginIdentifier} failed trust checks.", MessageType.Warning);
                    return false;
                }
            }

            // Process any additional required trust parameters.
            foreach (var param in requiredParams)
            {
                switch (param)
                {
                    case TaskTrustParameter.MustHaveSignedHash:
                        if (!CheckMustHaveSignedHash(task, info).Result)
                        {
                            DebugMessage($"Task {task.TaskType} from {info.SourceOriginIdentifier} failed MustHaveSignedHash check.", MessageType.Warning);
                            return false;
                        }
                        break;
                    // TrustedPeer and AuthorityBootstrapServer already handled
                    case TaskTrustParameter.TrustedPeer:
                    case TaskTrustParameter.AuthorityBootstrapServer:
                        break;
                    default:
                        break;
                }
            }

            return true;
        }


        private static NetworkTaskDelegate WrapWithChecks(NetworkTaskDelegate originalDelegate)
        {
            // return new delegate that checks trust params
            return (NetworkTask task, NetworkTaskOriginInfo info) =>
            {
                if (!CheckTaskParameters(ref task, ref info))
                {
                    // TODO log or handle
                    return;
                }
                
                originalDelegate.Invoke(task, info);
                return;
            };
        }

        #endregion

        #region Task Delegate
        /// <summary>
        /// Represents a handler for processing a network task.
        /// </summary>
        /// <param name="task">The network task to process.</param>
        public delegate void NetworkTaskDelegate(NetworkTask task, NetworkTaskOriginInfo info);

        /// <summary>
        /// Handler for tasks of type BlockAndRemovePeer.
        /// </summary>
        private static NetworkTaskDelegate BlockAndRemovePeerHandler { get; set; } = DefaultBlockAndRemovePeerHandler;

        /// <summary>
        /// Handler for tasks of type BlockIP.
        /// </summary>
        private static NetworkTaskDelegate BlockIPHandler { get; set; } = DefaultBlockIPHandler;

        /// <summary>
        /// Handler for tasks of type SendMessage.
        /// </summary>
        private static NetworkTaskDelegate SendMessageHandler { get; set; } = DefaultSendMessageHandler;

        /// <summary>
        /// Handler for tasks of type PingPeer.
        /// </summary>
        private static NetworkTaskDelegate PingPeerHandler { get; set; } = DefaultPingPeerHandler;

        /// <summary>
        /// Handler for tasks of type DisconnectPeer.
        /// </summary>
        private static NetworkTaskDelegate DisconnectPeerHandler { get; set; } = DefaultDisconnectPeerHandler;

        /// <summary>
        /// Handler for tasks of type AuthorizePeer.
        /// </summary>
        private static NetworkTaskDelegate AuthorizePeerHandler { get; set; } = DefaultAuthorizePeerHandler;

        /// <summary>
        /// Handler for tasks of type RevokePeerAuthorization.
        /// </summary>
        private static NetworkTaskDelegate RevokePeerAuthorizationHandler { get; set; } = DefaultRevokePeerAuthorizationHandler;

        /// <summary>
        /// Handler for tasks of type RequestData.
        /// </summary>
        private static NetworkTaskDelegate RequestDataHandler { get; set; } = DefaultRequestDataHandler;

        /// <summary>
        /// Handler for tasks of type SendData.
        /// </summary>
        private static NetworkTaskDelegate SendDataHandler { get; set; } = DefaultSendDataHandler;

        /// <summary>
        /// Handler for tasks of type SynchronizeData.
        /// </summary>
        private static NetworkTaskDelegate SynchronizeDataHandler { get; set; } = DefaultSynchronizeDataHandler;

        /// <summary>
        /// Handler for tasks of type UpdateSettings.
        /// </summary>
        private static NetworkTaskDelegate UpdateSettingsHandler { get; set; } = DefaultUpdateSettingsHandler;

        /// <summary>
        /// Handler for tasks of type RequestVerifyHashRecord.
        /// </summary>
        private static NetworkTaskDelegate RequestVerifyHashRecordHandler { get; set; } = DefaultRequestVerifyHashRecordHandler;

        /// <summary>
        /// Handler for tasks of type RequestVerifySignature.
        /// </summary>
        private static NetworkTaskDelegate RequestVerifySignatureHandler { get; set; } = DefaultRequestVerifySignatureHandler;

        /// <summary>
        /// Handler for tasks of type RequestPublicKey.
        /// </summary>
        private static NetworkTaskDelegate RequestPublicKeyHandler { get; set; } = DefaultRequestPublicKeyHandler;

        /// <summary>
        /// Handler for tasks of type BootstrapInitialization.
        /// </summary>
        private static NetworkTaskDelegate BootstrapInitializationHandler { get; set; } = DefaultBootstrapInitializationHandler;

        /// <summary>
        /// Handler for tasks of type Heartbeat.
        /// </summary>
        private static NetworkTaskDelegate HeartbeatHandler = DefaultHeartbeatHandler;

        /// <summary>
        /// Handler for tasks of type HeartbeatResponse.
        /// </summary>
        private static NetworkTaskDelegate HeartbeatResponseHandler { get; set; } = DefaultHeartbeatResponseHandler;

        /// <summary>
        /// Handler for tasks of type SetLocalIdentifier.
        /// </summary>
        private static NetworkTaskDelegate SetLocalIdentifierHandler { get; set; } = DefaultSetLocalIdentifierHandler;

        /// <summary>
        /// Handler for tasks of type AssignIdentifierToPeer.
        /// </summary>
        private static NetworkTaskDelegate AssignIdentifierToPeerHandler { get; set; } = DefaultAssignIdentifierToPeerHandler;
        
        private static NetworkTaskDelegate RequestWebRTCConnection { get; set; } = DefaultRequestWebRTCConnectionHandler;
        #endregion

        #region Default Implementations
        private static void DefaultBlockAndRemovePeerHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default BlockAndRemovePeerHandler executed.");
            PeerNetwork.TrustPolicies.IncomingPeerTrustPolicy.BlockedIdentifiers.Add(task.TaskData["Identifier"]);

            var peer = PeerNetwork.GetPeerByIdentifier(task.TaskData["Identifier"]);
            if(peer != null)
            {
                var pchannel = PeerNetwork.ActivePeerChannels.FirstOrDefault(x => x.peer.Identifier == peer.Identifier);
                var bschannel = PeerNetwork.ActiveBootstrapChannels.FirstOrDefault(x => x.BootstrapServer.Identifier == peer.Identifier);
                if(pchannel != null)
                {
                    pchannel.ClosePeerChannel();
                    PeerNetwork.ActivePeerChannels.Remove(pchannel);
                }
                if (bschannel != null)
                {
                    bschannel.CloseBootstrapChannel();
                    PeerNetwork.ActiveBootstrapChannels.Remove(bschannel);
                }
            }
            else
            {
                DebugMessage($"Peer with identifier {task.TaskData["Identifier"]} not found.", MessageType.Warning);
            }
        }

        private static void DefaultBlockIPHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default BlockIPHandler executed.");
        }

        private static void DefaultSendMessageHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default SendMessageHandler executed.");
        }

        private static void DefaultPingPeerHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default PingPeerHandler executed.");
        }

        private static void DefaultDisconnectPeerHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default DisconnectPeerHandler executed.");
        }

        private static void DefaultAuthorizePeerHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default AuthorizePeerHandler executed.");
        }

        private static void DefaultRevokePeerAuthorizationHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default RevokePeerAuthorizationHandler executed.");
        }

        private static void DefaultRequestDataHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default RequestDataHandler executed.");
        }

        private static void DefaultSendDataHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default SendDataHandler executed.");
        }

        private static void DefaultSynchronizeDataHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default SynchronizeDataHandler executed.");
        }

        private static void DefaultUpdateSettingsHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default UpdateSettingsHandler executed.");
        }

        private static void DefaultRequestVerifyHashRecordHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default RequestVerifyHashRecordHandler executed.");
        }

        private static void DefaultRequestVerifySignatureHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default RequestVerifySignatureHandler executed.");
        }

        private static void DefaultRequestPublicKeyHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default RequestPublicKeyHandler executed.");
        }

        private static async void DefaultBootstrapInitializationHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default BootstrapInitializationHandler executed.");
        }

        private static void DefaultHeartbeatHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default HeartbeatHandler executed.");
        }

        private static void DefaultHeartbeatResponseHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            // each value in Dict<str,str> --should-- be another NetworkTask
            Dictionary<string, string> tasks = task.TaskData;
            DebugMessage($"Bootstrap server responded to heartbeat with {tasks.Count} tasks.", ConsoleColor.Cyan, PeerNetwork.Logging.Bootstrap);

            foreach (var _task in tasks.Values)
            {
                try
                { // leave this wrapped in TryCatch block or otherwise will throw exception
                    var nt = Deserialize<NetworkTask>(_task);
                    if (nt != null)
                    {
                        try
                        {

                            DebugMessage($"Enqued task: {nt.TaskType}", ConsoleColor.DarkGreen, PeerNetwork.Logging.Bootstrap);
                            NetworkTaskHandler.EnqueueIncomingNetworkTask(nt, info);
                        }
                        catch (Exception ex)
                        {
                            // we do nothing here for now
                            // DebugMessage(ex.ToString(), MessageType.Critical, PeerNetwork.Logging.Bootstrap);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // we do nothing here
                }

            }
        }
        private static void DefaultSetLocalIdentifierHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            try
            {
                PeerNetwork.Identifier = task.TaskData["Identifier"];
            }
            catch (Exception ex)
            {
                // DebugMessage($"Error setting local identifier: {ex.Message}", MessageType.Error);
            }
        }

        private static void DefaultAssignIdentifierToPeerHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default AssignIdentifierToPeerHandler executed.");
        }

        private static void DefaultRequestWebRTCConnectionHandler(NetworkTask task, NetworkTaskOriginInfo info)
        {
            Console.WriteLine("Default RequestWebRTCConnectionTask executed.");
        }

        #endregion
    }

}