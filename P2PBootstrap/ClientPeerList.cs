using System.Collections;
using System.Timers;

namespace P2PBootstrap
{
    public class ClientPeerList : IEnumerable<ClientPeer>
    {
        private readonly Dictionary<string, ClientPeer> _peers = new Dictionary<string, ClientPeer>();
        private static System.Timers.Timer _idleChecker;
        public ClientPeerList()
        {
            _idleChecker = new System.Timers.Timer(60000); // 1 min for now
            _idleChecker.Elapsed += IdleCheck;
            _idleChecker.AutoReset = true;
            _idleChecker.Enabled = true;
        }

        private void IdleCheck(object? sender, ElapsedEventArgs e)
        {
            DateTime now = DateTime.Now;
            // get identifiers for peers that have been idle for 30 minutes or more.
            var idlePeerIds = _peers.Values
                .Where(peer => now - peer.LastIncomingDataReceived >= TimeSpan.FromMinutes(30))
                .Select(peer => peer.Identifier)
                .ToList();

            foreach (var id in idlePeerIds)
            {
                _peers.Remove(id);
            }
        }

        /// <summary>
        /// Gets the number of client peers.
        /// </summary>
        public int Count => _peers.Count;

        /// <summary>
        /// Indexer to get a client peer by its identifier.
        /// Throws a KeyNotFoundException if the identifier is not found.
        /// </summary>
        /// <param name="identifier">The unique identifier for a client peer.</param>
        /// <returns>The matching client peer.</returns>
        public ClientPeer this[string identifier]
        {
            get
            {
                if (_peers.TryGetValue(identifier, out var peer))
                {
                    return peer;
                }
                throw new KeyNotFoundException($"Peer with identifier '{identifier}' not found.");
            }
        }

        /// <summary>
        /// Attempts to add a new client peer to the list.
        /// The peer is added only if its Identifier does not already exist in the list.
        /// </summary>
        /// <param name="peer">The client peer to add.</param>
        /// <returns>
        /// True if the peer was added; false if a peer with the same Identifier already exists.
        /// </returns>
        public bool Add(ClientPeer peer)
        {
            if (peer == null)
                throw new ArgumentNullException(nameof(peer));
        //    if (string.IsNullOrWhiteSpace(peer.Identifier))
        //        throw new ArgumentException("ClientPeer.Identifier cannot be null or whitespace.", nameof(peer));

            if (_peers.ContainsKey(peer.Identifier))
            {
                // Prevent duplicate identifiers.
                return false;
            }

            _peers.Add(peer.Identifier, peer);
            return true;
        }

        /// <summary>
        /// Removes a client peer with the specified identifier.
        /// </summary>
        /// <param name="identifier">The unique identifier of the client peer to remove.</param>
        /// <returns>True if the peer was removed; otherwise, false.</returns>
        public bool Remove(string identifier)
        {
            return _peers.Remove(identifier);
        }

        /// <summary>
        /// Tries to get the client peer with the specified identifier.
        /// </summary>
        /// <param name="identifier">The identifier of the desired peer.</param>
        /// <param name="peer">When this method returns, contains the desired client peer if found; otherwise, null.</param>
        /// <returns>True if the peer was found; otherwise, false.</returns>
        public bool TryGetValue(string identifier, out ClientPeer peer)
        {
            return _peers.TryGetValue(identifier, out peer);
        }

        /// <summary>
        /// Clears all client peers from the list.
        /// </summary>
        public void Clear()
        {
            _peers.Clear();
        }

        /// <summary>
        /// Returns an enumerator that iterates through the client peers.
        /// </summary>
        /// <returns>An enumerator of ClientPeer.</returns>
        public IEnumerator<ClientPeer> GetEnumerator()
        {
            return _peers.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
