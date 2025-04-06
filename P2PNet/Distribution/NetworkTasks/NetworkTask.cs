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
    }

}