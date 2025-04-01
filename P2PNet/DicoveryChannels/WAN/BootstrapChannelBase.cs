using P2PNet.Distribution;
using P2PNet.Distribution.NetworkTasks;
using P2PNet.NetworkPackets;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace P2PNet.DicoveryChannels.WAN
{
    public abstract class BootstrapChannelBase
    {
        public bool IsAuthorityMode { get; set; } = false;
        public BootstrapPeer BootstrapServer { get; set; }
        internal Uri BootstrapServerEndpoint => BootstrapServer.Endpoint;
        internal PGPKeyInfo publicKey { get; set; } = null; // store the public key from the server
        public string PublicKey => publicKey?.ToString(); // expose the public key as a string for easy access


        private static Timer _heartbeatTimer;


        // ----- public delegates -----
        public Action<string> InitialBootstrapHandler =>
            IsAuthorityMode ? AuthorityModeInitialBootstrapHandle : TrustlessModeInitialBootstrapHandle;
        public Func<NetworkTask, Task<bool>> IsValidNetworkHash => CheckNetworkTaskHash;
        // ----------------------------

        // ------ private delegate -----
        private Action<string> AuthorityModeInitialBootstrapHandle { get; set; }
        private Action<string> TrustlessModeInitialBootstrapHandle { get; set; }
        private Func<NetworkTask, Task<bool>> CheckNetworkTaskHash { get; set; }
        private Action SendHeartbeatToServer { get; set; }
        // ----------------------------

        private void SendOutgoingHeartbeat(object? sender, System.Timers.ElapsedEventArgs e)
        {
            SendHeartbeatToServer.Invoke();
        }

        protected BootstrapChannelBase()
        {
            // setup delegates
            AuthorityModeInitialBootstrapHandle = AuthorityModeInitialBootstrap;
            TrustlessModeInitialBootstrapHandle = TrustlessModeInitialBootstrap;
            CheckNetworkTaskHash = ValidateNetworkTaskHash;
            SendHeartbeatToServer = SendOutgoingHeartbeatToServer;

            // setup hearbeat timer
            _heartbeatTimer = new System.Timers.Timer(50000); // half second
            _heartbeatTimer.Elapsed += SendOutgoingHeartbeat;
            _heartbeatTimer.AutoReset = true;
            _heartbeatTimer.Enabled = true;
        }
        #region Delegate Methods
        protected virtual void AuthorityModeInitialBootstrap(string packet)
        {
            // expecting a DataTransmissionPacket with NetworkTask.
            NetworkTask networkTask = Deserialize<NetworkTask>(packet);
            // store server's public key.
            StorePublicKey(networkTask.TaskData["PublicKey"]);
            // process the peer list.
            CollectionSharePacket sharePacket = Deserialize<CollectionSharePacket>(networkTask.TaskData["Peers"]);
            ProcessPeerList(sharePacket);
        }
        protected virtual void TrustlessModeInitialBootstrap(string packet)
        {
            // trustless mode
            CollectionSharePacket sharePacket = Deserialize<CollectionSharePacket>(packet);
            ProcessPeerList(sharePacket);
        }

        protected async Task<bool> ValidateNetworkTaskHash(NetworkTask task)
        {
            if (task.TaskData.ContainsKey("Signature"))
            {
                NetworkTask ntRemovedSignature = task;
                ntRemovedSignature.TaskData.Remove("Signature");

                MD5 hashing = MD5.Create();
                string _hashone = Convert.ToBase64String(hashing.ComputeHash(ntRemovedSignature.ToByte()));

                // todo : check if the signature is valid + pass the hash to VerifyHash endpoint
            }
            else
            {
                DebugMessage("No signature found in NetworkTask.");
            }

            return await EncryptionAndSecurityHandler.VerifySignature(task.TaskData["Signature"], publicKey?.KeyData?.ToString());
        }
        protected virtual async void SendOutgoingHeartbeatToServer()
        {
            DataTransmissionPacket dataTransmissionPacket = CreateHeartbeatPacket();
            string outgoingPacket = Serialize<DataTransmissionPacket>(dataTransmissionPacket);
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = await client.PutAsync(DistributionProtocol.GetEndpointURI(CommonBootstrapEndpoints.Heartbeat, BootstrapServerEndpoint), new StringContent(outgoingPacket, Encoding.UTF8, "application/json"));

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Bootstrap heartbeat failed: {response.StatusCode}");
                }

                string responseContent = await response.Content.ReadAsStringAsync();
                var responsePacket = Deserialize<DataTransmissionPacket>(responseContent);

                if (responsePacket != null)
                {
                    // TODO - the bootstrap server will return all the queued network tasks to be processed
                }
                else
                {
                    throw new Exception("Bootstrap response was not in the expected format.");
                }
            }
        }
        #endregion

        #region General Helper Methods
        protected DataTransmissionPacket CreateHeartbeatPacket()
        {
            NetworkTask hbTask = new NetworkTask()
            {
                TaskType = TaskType.Heartbeat,
                TaskData = new Dictionary<string, string>()
            };
            // send a heartbeat to the server
            DataTransmissionPacket heartbeatPacket = new DataTransmissionPacket(hbTask.ToByte(), DataPayloadFormat.MiscData);
            return heartbeatPacket;
        }

        protected DataTransmissionPacket CreateInitialBootstrapPacket()
        {
            IdentifierPacket idPacket = new IdentifierPacket("discovery", PeerNetwork.ListeningPort, PeerNetwork.PublicIPV6Address == null ? PeerNetwork.PublicIPV4Address : PeerNetwork.PublicIPV6Address);
            string idPacketJson = Serialize<IdentifierPacket>(idPacket);
            byte[] idPacketBytes = Encoding.UTF8.GetBytes(idPacketJson);
            DebugMessage($"Sending initial request to bootstrap server.", PeerNetwork.Logging.Bootstrap);
            // wrap the IdentifierPacker in a DataTransmissionPacket
            DataTransmissionPacket initialPacket = new DataTransmissionPacket(idPacketBytes, DataPayloadFormat.MiscData);
            return initialPacket;
        }

        protected void StorePublicKey(string publicKey)
        {
            this.publicKey = new PGPKeyInfo("PublicKey", Encoding.UTF8.GetBytes(publicKey));
        }

        protected void ProcessPeerList(CollectionSharePacket peerList)
        {
            PeerNetwork.ProcessPeerList(peerList);
        }
        #endregion
    }
}
