using P2PNet.Distribution.NetworkTasks;
using P2PNet.NetworkPackets;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace P2PBootstrap
{
    /// <summary>
    /// Provides TURN (Traversal Using Relays around NAT) relay service functionality for the bootstrap server.
    /// This service allows peers to communicate through the server when direct peer-to-peer connections are not possible.
    /// </summary>
    public static class TURNService
    {
        /// <summary>
        /// Represents an active TURN connection between two peers.
        /// </summary>
        public class TURNConnection
        {
            /// <summary>
            /// Gets or sets the identifier of the initiating peer.
            /// </summary>
            public string InitiatorId { get; set; }

            /// <summary>
            /// Gets or sets the identifier of the target peer.
            /// </summary>
            public string TargetId { get; set; }

            /// <summary>
            /// Gets or sets the channel for data flowing from initiator to target.
            /// </summary>
            public Channel<string> InitiatorToTargetChannel { get; set; }

            /// <summary>
            /// Gets or sets the channel for data flowing from target to initiator.
            /// </summary>
            public Channel<string> TargetToInitiatorChannel { get; set; }

            /// <summary>
            /// Gets or sets the time this connection was established.
            /// </summary>
            public DateTime EstablishedAt { get; set; } = DateTime.UtcNow;

            /// <summary>
            /// Gets or sets the last activity time on this connection.
            /// </summary>
            public DateTime LastActivity { get; set; } = DateTime.UtcNow;

            /// <summary>
            /// Gets or sets the cancellation token source for this connection.
            /// </summary>
            public CancellationTokenSource CancellationSource { get; set; } = new CancellationTokenSource();

            /// <summary>
            /// Updates the last activity timestamp to the current time.
            /// </summary>
            public void Touch()
            {
                LastActivity = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Ledger of all active TURN connections indexed by a composite key of the two peer identifiers.
        /// </summary>
        private static readonly ConcurrentDictionary<string, TURNConnection> _activeTurnConnections = new();

        /// <summary>
        /// Channels for pending outbound data per peer identifier for stream-based communication.
        /// </summary>
        private static readonly ConcurrentDictionary<string, Channel<NetworkTask>> _peerEventChannels = new();

        /// <summary>
        /// Creates a composite key for a TURN connection from two peer identifiers.
        /// </summary>
        private static string CreateConnectionKey(string peerId1, string peerId2)
        {
            // Sort to ensure consistent key regardless of order
            var sorted = new[] { peerId1, peerId2 }.OrderBy(x => x).ToArray();
            return $"{sorted[0]}|{sorted[1]}";
        }

        /// <summary>
        /// Gets or creates the event channel for a specific peer.
        /// </summary>
        /// <param name="peerId">The identifier of the peer.</param>
        /// <returns>The channel for the peer's events.</returns>
        public static Channel<NetworkTask> GetOrCreatePeerChannel(string peerId)
        {
            return _peerEventChannels.GetOrAdd(peerId, _ => Channel.CreateUnbounded<NetworkTask>(
                new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }));
        }

        /// <summary>
        /// Removes the event channel for a specific peer.
        /// </summary>
        /// <param name="peerId">The identifier of the peer.</param>
        public static void RemovePeerChannel(string peerId)
        {
            if (_peerEventChannels.TryRemove(peerId, out var channel))
            {
                channel.Writer.TryComplete();
            }
        }

        /// <summary>
        /// Enqueues a network task to be sent to a specific peer via their event stream.
        /// </summary>
        /// <param name="peerId">The identifier of the target peer.</param>
        /// <param name="task">The network task to send.</param>
        /// <returns>True if the task was enqueued successfully; otherwise, false.</returns>
        public static bool EnqueueTaskForPeer(string peerId, NetworkTask task)
        {
            var channel = GetOrCreatePeerChannel(peerId);
            return channel.Writer.TryWrite(task);
        }

        /// <summary>
        /// Reads events from a peer's channel as an async enumerable for SSE streaming.
        /// </summary>
        /// <param name="peerId">The identifier of the peer.</param>
        /// <param name="cancellationToken">Cancellation token for the stream.</param>
        /// <returns>An async enumerable of network tasks for the peer.</returns>
        public static async IAsyncEnumerable<NetworkTask> ReadPeerEventsAsync(
            string peerId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var channel = GetOrCreatePeerChannel(peerId);

            await foreach (var task in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return task;
            }
        }

        /// <summary>
        /// Initiates a TURN connection request between two peers.
        /// </summary>
        /// <param name="initiatorId">The identifier of the initiating peer.</param>
        /// <param name="targetId">The identifier of the target peer.</param>
        /// <returns>The established TURN connection, or null if the connection could not be established.</returns>
        public static TURNConnection InitiateTurnConnection(string initiatorId, string targetId)
        {
            string connectionKey = CreateConnectionKey(initiatorId, targetId);

            // Check if connection already exists
            if (_activeTurnConnections.TryGetValue(connectionKey, out var existingConnection))
            {
                existingConnection.Touch();
                return existingConnection;
            }

            // Create bidirectional channels for the TURN relay
            var connection = new TURNConnection
            {
                InitiatorId = initiatorId,
                TargetId = targetId,
                InitiatorToTargetChannel = Channel.CreateUnbounded<string>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }),
                TargetToInitiatorChannel = Channel.CreateUnbounded<string>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false })
            };

            if (_activeTurnConnections.TryAdd(connectionKey, connection))
            {
                // Notify target peer of incoming TURN connection request
                var notifyTask = new NetworkTask
                {
                    TaskType = TaskType.TurnConnectionRequest,
                    TaskData = new Dictionary<string, string>
                    {
                        { "InitiatorId", initiatorId },
                        { "ConnectionKey", connectionKey }
                    }
                };
                EnqueueTaskForPeer(targetId, notifyTask);

                return connection;
            }

            return null;
        }

        /// <summary>
        /// Gets an existing TURN connection by connection key.
        /// </summary>
        /// <param name="connectionKey">The connection key.</param>
        /// <returns>The TURN connection if found; otherwise, null.</returns>
        public static TURNConnection GetConnection(string connectionKey)
        {
            _activeTurnConnections.TryGetValue(connectionKey, out var connection);
            return connection;
        }

        /// <summary>
        /// Gets an existing TURN connection by the two peer identifiers.
        /// </summary>
        /// <param name="peerId1">The first peer identifier.</param>
        /// <param name="peerId2">The second peer identifier.</param>
        /// <returns>The TURN connection if found; otherwise, null.</returns>
        public static TURNConnection GetConnection(string peerId1, string peerId2)
        {
            string connectionKey = CreateConnectionKey(peerId1, peerId2);
            return GetConnection(connectionKey);
        }

        /// <summary>
        /// Relays data through a TURN connection.
        /// </summary>
        /// <param name="connectionKey">The connection key.</param>
        /// <param name="senderId">The identifier of the sending peer.</param>
        /// <param name="data">The data to relay.</param>
        /// <returns>True if the data was relayed successfully; otherwise, false.</returns>
        public static bool RelayData(string connectionKey, string senderId, string data)
        {
            if (!_activeTurnConnections.TryGetValue(connectionKey, out var connection))
            {
                return false;
            }

            connection.Touch();

            // Determine direction and write to appropriate channel
            if (senderId == connection.InitiatorId)
            {
                return connection.InitiatorToTargetChannel.Writer.TryWrite(data);
            }
            else if (senderId == connection.TargetId)
            {
                return connection.TargetToInitiatorChannel.Writer.TryWrite(data);
            }

            return false;
        }

        /// <summary>
        /// Reads relayed data for a specific peer from a TURN connection as an async enumerable.
        /// </summary>
        /// <param name="connectionKey">The connection key.</param>
        /// <param name="receiverId">The identifier of the receiving peer.</param>
        /// <param name="cancellationToken">Cancellation token for the stream.</param>
        /// <returns>An async enumerable of relayed data.</returns>
        public static async IAsyncEnumerable<string> ReadRelayedDataAsync(
            string connectionKey,
            string receiverId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!_activeTurnConnections.TryGetValue(connectionKey, out var connection))
            {
                yield break;
            }

            // Determine which channel to read from based on receiver
            ChannelReader<string> reader;
            if (receiverId == connection.TargetId)
            {
                reader = connection.InitiatorToTargetChannel.Reader;
            }
            else if (receiverId == connection.InitiatorId)
            {
                reader = connection.TargetToInitiatorChannel.Reader;
            }
            else
            {
                yield break;
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, connection.CancellationSource.Token);

            await foreach (var data in reader.ReadAllAsync(linkedCts.Token))
            {
                connection.Touch();
                yield return data;
            }
        }

        /// <summary>
        /// Closes a TURN connection and cleans up resources.
        /// </summary>
        /// <param name="connectionKey">The connection key.</param>
        /// <returns>True if the connection was closed; otherwise, false.</returns>
        public static bool CloseTurnConnection(string connectionKey)
        {
            if (_activeTurnConnections.TryRemove(connectionKey, out var connection))
            {
                connection.CancellationSource.Cancel();
                connection.InitiatorToTargetChannel.Writer.TryComplete();
                connection.TargetToInitiatorChannel.Writer.TryComplete();

                // Notify both peers that the connection is closed
                var closeTask = new NetworkTask
                {
                    TaskType = TaskType.TurnConnectionClosed,
                    TaskData = new Dictionary<string, string>
                    {
                        { "ConnectionKey", connectionKey }
                    }
                };
                EnqueueTaskForPeer(connection.InitiatorId, closeTask);
                EnqueueTaskForPeer(connection.TargetId, closeTask);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Closes a TURN connection by the two peer identifiers.
        /// </summary>
        /// <param name="peerId1">The first peer identifier.</param>
        /// <param name="peerId2">The second peer identifier.</param>
        /// <returns>True if the connection was closed; otherwise, false.</returns>
        public static bool CloseTurnConnection(string peerId1, string peerId2)
        {
            string connectionKey = CreateConnectionKey(peerId1, peerId2);
            return CloseTurnConnection(connectionKey);
        }

        /// <summary>
        /// Gets the count of active TURN connections.
        /// </summary>
        public static int ActiveConnectionCount => _activeTurnConnections.Count;

        /// <summary>
        /// Cleans up stale TURN connections that have been inactive for longer than the specified timeout.
        /// </summary>
        /// <param name="timeout">The timeout duration.</param>
        /// <returns>The number of connections cleaned up.</returns>
        public static int CleanupStaleConnections(TimeSpan timeout)
        {
            int cleaned = 0;
            var cutoff = DateTime.UtcNow - timeout;

            foreach (var kvp in _activeTurnConnections)
            {
                if (kvp.Value.LastActivity < cutoff)
                {
                    if (CloseTurnConnection(kvp.Key))
                    {
                        cleaned++;
                    }
                }
            }

            return cleaned;
        }
    }
}
