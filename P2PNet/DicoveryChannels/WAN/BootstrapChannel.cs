using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using P2PNet.Distribution;
using P2PNet.Distribution.NetworkTasks;
using P2PNet.NetworkPackets;
using P2PNet.Peers;

namespace P2PNet.DicoveryChannels.WAN
{
        /// <summary>
        /// Communicates with a bootstrap server to share known peers and establish identity in network.
        /// </summary>
        /// <remarks>Bootstrap server will direct connecting peers with a CollectionSharePacket and other means of conveying network information.</remarks>
        public class BootstrapChannel
        {
            private string BootstrapServerEndpoint { get; set; }
            public BootstrapPeer BootstrapServer { get; set; }
            public bool IsAuthorityMode { get; set; }
            private PGPKeyInfo publicKey { get; set; } = null; // store the public key from the server
            public string PublicKey => publicKey?.ToString(); // expose the public key as a string for easy access

        /// <summary>
        /// Initializes a new instance of the <see cref="BootstrapChannel"/> class using the specified endpoint and authority mode.
        /// </summary>
        /// <param name="endpoint">The endpoint URL of the bootstrap server.</param>
        /// <param name="isAuthorityMode">A boolean indicating if authority mode should be enabled.</param>
        public BootstrapChannel(string endpoint, bool isAuthorityMode)
        {
            BootstrapServerEndpoint = endpoint;
            IsAuthorityMode = isAuthorityMode;
            BootstrapServer = new BootstrapPeer(endpoint);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BootstrapChannel"/> class using the specified connection options.
        /// If the options include a pre-specified <see cref="BootstrapPeer"/>, it is used; otherwise, a new <see cref="BootstrapPeer"/> is created from the endpoint.
        /// </summary>
        /// <param name="options">An instance of <see cref="BootstrapChannelConnectionOptions"/> detailing the connection settings.</param>
        public BootstrapChannel(BootstrapChannelConnectionOptions options)
        {
            BootstrapServerEndpoint = options.Endpoint;
            IsAuthorityMode = options.IsAuthorityMode;
            BootstrapServer = options.BootstrapPeer ?? new BootstrapPeer(options.Endpoint);
        }

        /// <summary>
        /// Initiates the bootstrap handshake by sending an initial DataTransmissionPacket that embeds the peer’s identifying information.
        /// Then, it awaits the response from the bootstrap server.
        /// In trustless mode, the response is expected to be a CollectionSharePacket.
        /// In authority mode, the response is a DataTransmissionPacket containing a NetworkTask with the public key and peer list.
        /// </summary>
        /// <returns>A task representing the asynchronous handshake operation.</returns>
        public async Task OpenBootstrapChannel()
        {
            // immediately check if first and locking is set or not
            if(PeerNetwork.TrustPolicies.BootstrapTrustPolicy.FirstSingleLockingAuthoritySet == true)
            {
                // if first and locking is set then we throw exception
                throw new InitialAuthorityLockedException("First and locking authority is already set. Cannot open additional bootstrap channel.");
            }

            var idPacket = new IdentifierPacket("discovery", PeerNetwork.ListeningPort, PeerNetwork.PublicIPV6Address == null ? PeerNetwork.PublicIPV4Address : PeerNetwork.PublicIPV6Address);
            string idPacketJson = Serialize(idPacket);

            // wrap the IdentifierPacker in a DataTransmissionPacket
            DataTransmissionPacket initialPacket = new DataTransmissionPacket(Encoding.UTF8.GetBytes(idPacketJson), DataPayloadFormat.MiscData);

            using (HttpClient client = new HttpClient())
            {
                var content = new StringContent(Serialize(initialPacket), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PutAsync(BootstrapServerEndpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Bootstrap request failed with status code: {response.StatusCode}");
                }

                string responseContent = await response.Content.ReadAsStringAsync();
                var responsePacket = Deserialize<DataTransmissionPacket>(responseContent);

                if (responsePacket != null)
                {
                    if (IsAuthorityMode)
                    {
                        // expecting a DataTransmissionPacket with NetworkTask.
                        NetworkTask networkTask = Deserialize<NetworkTask>(Encoding.UTF8.GetString(responsePacket.Data));
                        // store server's public key.
                        StorePublicKey(networkTask.TaskData["PublicKey"]);
                        // process the peer list.
                        CollectionSharePacket sharePacket = Deserialize<CollectionSharePacket>(networkTask.TaskData["Peers"]);
                        ProcessPeerList(sharePacket);
                        // check and set first locking authority
                        if ((PeerNetwork.TrustPolicies.BootstrapTrustPolicy.FirstSingleLockingAuthority == true) && (PeerNetwork.TrustPolicies.BootstrapTrustPolicy.FirstSingleLockingAuthoritySet == false))
                        {
                            PeerNetwork.TrustPolicies.BootstrapTrustPolicy.SetFirstLockingAuthority(this);
                        }
                    }
                    else
                    {
                        // trustless mode
                        CollectionSharePacket sharePacket = Deserialize<CollectionSharePacket>(responseContent);
                        ProcessPeerList(sharePacket);
                    }
                }
            }
        }

            private void StorePublicKey(string publicKey)
            {
            this.publicKey = new PGPKeyInfo("PublicKey", Encoding.UTF8.GetBytes(publicKey));
            }


            private void ProcessPeerList(CollectionSharePacket peerList)
            {
                PeerNetwork.ProcessPeerList(peerList);
            }
        }


        /// <summary>
        /// Represents the configuration options for establishing a connection to a bootstrap server.
        /// The bootstrap server is used for peer discovery and identity establishment within the network.
        /// </summary>
        public class BootstrapChannelConnectionOptions
        {
            /// <summary>
            /// Gets or sets the endpoint URL of the bootstrap server.
            /// This value is required.
            /// </summary>
            public string Endpoint { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the connection should run in authority mode.
            /// In authority mode, additional tasks such as fetching the public key are triggered.
            /// </summary>
            public bool IsAuthorityMode { get; set; }

            /// <summary>
            /// Gets or sets the <see cref="BootstrapPeer"/> that represents the bootstrap server.
            /// This property is optional and can be provided for direct server communication.
            /// </summary>
            public BootstrapPeer BootstrapPeer { get; set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="BootstrapChannelConnectionOptions"/> class using the specified endpoint.
            /// This constructor assumes non-authority mode and no explicit bootstrap peer.
            /// </summary>
            /// <param name="endpoint">The endpoint URL of the bootstrap server.</param>
            public BootstrapChannelConnectionOptions(string endpoint)
                : this(endpoint, false, null)
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="BootstrapChannelConnectionOptions"/> class using the specified endpoint and authority mode.
            /// </summary>
            /// <param name="endpoint">The endpoint URL of the bootstrap server.</param>
            /// <param name="isAuthorityMode">A boolean indicating if authority mode should be enabled.</param>
            public BootstrapChannelConnectionOptions(string endpoint, bool isAuthorityMode)
                : this(endpoint, isAuthorityMode, null)
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="BootstrapChannelConnectionOptions"/> class using the specified endpoint and bootstrap peer.
            /// Authority mode will be set to false.
            /// </summary>
            /// <param name="endpoint">The endpoint URL of the bootstrap server.</param>
            /// <param name="bootstrapPeer">An instance of <see cref="BootstrapPeer"/> representing the bootstrap server.</param>
            public BootstrapChannelConnectionOptions(string endpoint, BootstrapPeer bootstrapPeer)
                : this(endpoint, false, bootstrapPeer)
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="BootstrapChannelConnectionOptions"/> class using all specified values.
            /// </summary>
            /// <param name="endpoint">The endpoint URL of the bootstrap server.</param>
            /// <param name="isAuthorityMode">A boolean indicating if authority mode should be enabled.</param>
            /// <param name="bootstrapPeer">An instance of <see cref="BootstrapPeer"/> representing the bootstrap server.</param>
            public BootstrapChannelConnectionOptions(string endpoint, bool isAuthorityMode, BootstrapPeer bootstrapPeer)
            {
                Endpoint = endpoint;
                IsAuthorityMode = isAuthorityMode;
                BootstrapPeer = bootstrapPeer;
            }
        }
    

}
