global using static P2PNet.PeerNetwork;
global using static P2PNet.Distribution.DistributionProtocol;
global using static ConsoleDebugger.ConsoleDebugger;
global using P2PNet.Distribution;

using Microsoft.MixedReality.WebRTC;
using System.Collections.Concurrent;
using P2PNet.Peers;

namespace P2PBootstrap.WebRTC
{
    public static class WebRTCHandler
    {
        private static readonly ConcurrentDictionary<string, PeerConnection> PeerConnections = new();
        private static readonly ConcurrentDictionary<string, string> PendingRequests = new();

        /// <summary>
        /// Initializes a new WebRTC PeerConnection for the given SourceOriginIdentifier.
        /// </summary>
        public static async Task<PeerConnection> StartPeerConnectionAsync(string sourceOriginIdentifier)
        {
            var config = new PeerConnectionConfiguration
            {
                IceServers = new List<IceServer>
                {
                    // TODO" STUN/TURN server here 
                }
            };

            var peerConnection = new PeerConnection();
            await peerConnection.InitializeAsync(config);

            PeerConnections[sourceOriginIdentifier] = peerConnection;
            return peerConnection;
        }

        /// <summary>
        /// Initializes a new WebRTC PeerConnection for the given IPeer.
        /// </summary>
        public static async Task<PeerConnection> StartPeerConnectionAsync(IPeer peer)
        {
            if (peer == null || string.IsNullOrEmpty(peer.Identifier))
                throw new ArgumentException("Peer or Identifier is null.");

            return await StartPeerConnectionAsync(peer.Identifier);
        }

        /// <summary>
        /// Gets an existing PeerConnection by SourceOriginIdentifier, or null if not found.
        /// </summary>
        public static PeerConnection? GetPeerConnection(string sourceOriginIdentifier)
        {
            PeerConnections.TryGetValue(sourceOriginIdentifier, out var conn);
            return conn;
        }

        /// <summary>
        /// Gets an existing PeerConnection by IPeer, or null if not found.
        /// </summary>
        public static PeerConnection? GetPeerConnection(IPeer peer)
        {
            if (peer == null || string.IsNullOrEmpty(peer.Identifier))
                return null;
            return GetPeerConnection(peer.Identifier);
        }

        /// <summary>
        /// Removes and disposes a PeerConnection by SourceOriginIdentifier.
        /// </summary>
        public static void RemovePeerConnection(string sourceOriginIdentifier)
        {
            if (PeerConnections.TryRemove(sourceOriginIdentifier, out var conn))
            {
                conn.Close();
                conn.Dispose();
            }
        }

        /// <summary>
        /// Removes and disposes a PeerConnection by IPeer.
        /// </summary>
        public static void RemovePeerConnection(IPeer peer)
        {
            if (peer == null || string.IsNullOrEmpty(peer.Identifier))
                return;
            RemovePeerConnection(peer.Identifier);
        }

        /// <summary>
        /// Called when a peer wants to connect to another peer via WebRTC.
        /// </summary>
        /// <param name="requestorId">The SourceOriginIdentifier of the requesting peer.</param>
        /// <param name="targetId">The SourceOriginIdentifier of the target peer.</param>
        /// <returns>
        /// Returns the matched peer's ID if a match is found, otherwise null.
        /// </returns>
        public static string? RequestPeerMatch(string requestorId, string targetId)
        {
            if (PendingRequests.TryGetValue(requestorId, out var waitingForMe) && waitingForMe == targetId)
            {
                PendingRequests.TryRemove(requestorId, out _);
                PendingRequests.TryRemove(targetId, out _);
                return targetId;
            }
            else
            {
                PendingRequests[targetId] = requestorId;
                return null;
            }
        }

        /// <summary>
        /// Checks if a peer is known (exists in PeerNetwork.KnownPeers).
        /// </summary>
        public static bool IsPeerKnown(string sourceOriginIdentifier)
        {
            return KnownPeers.Any(p => p.Identifier == sourceOriginIdentifier);
        }

        /// <summary>
        /// Checks if a peer is known (exists in PeerNetwork.KnownPeers).
        /// </summary>
        public static bool IsPeerKnown(IPeer peer)
        {
            if (peer == null || string.IsNullOrEmpty(peer.Identifier))
                return false;
            return IsPeerKnown(peer.Identifier);
        }
    }
}
