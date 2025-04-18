using Org.BouncyCastle.Asn1.Ocsp;
using P2PNet.Distribution;
using P2PNet.Distribution.NetworkTasks;
using P2PNet.NetworkPackets;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        /// <summary>
        /// Gets or sets the maximum number of consecutive failed outbound heartbeats before the heartbeat routine is stopped.
        /// </summary>
        /// <remarks>
        /// If the number of heartbeat failures reaches this threshold, the <see cref="BootstrapChannelBase.HandleFailedHeartbeat"> delegate is invoked.
        /// The default implementation of the delegate terminated the timer routine.
        /// The default value is 5.
        /// </remarks>
        public int MaxFailureTimeout { get; set; } = 5;

        /// <summary>
        /// Starts the heartbeat routine by starting the underlying timer.
        /// </summary>
        /// <remarks>
        /// This method initiates the periodic sending of heartbeat messages to the bootstrap server.
        /// Any exceptions during the timer start are caught and suppressed.
        /// </remarks>
        public void StartHeartbeatRoutine()
        {
            try
            {
                _heartbeatTimer.Start();
            }
            catch
            {
                // Exceptions are suppressed to prevent crashing the application.
            }
        }

        /// <summary>
        /// Stops the heartbeat routine by stopping the underlying timer.
        /// </summary>
        /// <remarks>
        /// This method stops the periodic sending of heartbeat messages to the bootstrap server.
        /// Any exceptions during the timer stop are caught and suppressed.
        /// </remarks>
        public void StopHeartbeatRoutine()
        {
            try
            {
                _heartbeatTimer.Stop();
            }
            catch
            {
                // Exceptions are suppressed for graceful shutdown.
            }
        }

        /// <summary>
        /// Gets a value indicating whether the heartbeat routine is currently running.
        /// </summary>
        /// <remarks>
        /// This property reflects the status of the underlying timer. 
        /// A value of <c>true</c> indicates that heartbeat messages are actively being sent.
        /// </remarks>
        public bool HeartbeatRoutineRunning => _heartbeatTimer.Enabled;



        protected static Timer _heartbeatTimer;
        private int failureCount { get; set; } = 0;


        // ----- public delegates -----
        #region Public Delegates
        /// <summary>
        /// Gets or sets the delegate that handles the initial bootstrap handshake.
        /// </summary>
        /// <remarks>
        /// When not explicitly set, the default implementation is chosen based on the channel mode:
        /// if <see cref="BootstrapChannelBase.IsAuthorityMode"/> is true, then the <see cref="AuthorityModeInitialBootstrap(string)"/>
        /// is used; otherwise, the <see cref="TrustlessModeInitialBootstrap(string)"/> is used.
        /// You can override this delegate by setting a new value.
        /// </remarks>
        public Action<string> InitialBootstrapHandler { get; set; }

        /// <summary>
        /// Gets or sets the delegate that is responsible for sending out or processing an outgoing heartbeat.
        /// </summary>
        /// <remarks>
        /// The default behavior for this delegate is to invoke the method that sends an outbound heartbeat to the bootstrap server.
        /// This delegate is invoked on each timer interval during the heartbeat routine.
        /// You can override this delegate to customize the heartbeat transmission behavior or to inject additional logic before sending.
        /// </remarks>
        public Action HeartbeatOutHandler { get; set; }

        /// <summary>
        /// Gets or sets the delegate that handles error response messages from the bootstrap server.
        /// </summary>
        /// <remarks>
        /// The default implementation logs the error response using <see cref="ErrorResponse(string)"/>.
        /// You can override this to perform additional actions when an error response is received.
        /// </remarks>
        public Action<string> HandleErrorResponse { get; set; }

        /// <summary>
        /// Gets or sets the delegate that validates the network task's hash.
        /// </summary>
        /// <remarks>
        /// The default implementation is <see cref="ValidateNetworkTaskHash(NetworkTask)"/> which validates the hash
        /// and then verifies the signature (using the stored public key). You may override this to implement
        /// custom hash validation logic.
        /// </remarks>
        public Func<NetworkTask, Task<bool>> IsValidNetworkHash { get; set; }

        /// <summary>
        /// Gets or sets the delegate that indicates whether an incoming packet returned an error response from a server.
        /// </summary>
        /// <remarks>
        /// The default implementation is <see cref="IsErrorResponse(string)"/>, which attempts to deserialize the packet
        /// as a <see cref="PureMessagePacket"/>. If the deserialization succeeds, it is considered an error.
        /// You may override this delegate to provide additional error-determination logic.
        /// </remarks>
        public Func<string, Task<bool>> PacketReturnedErrorResponse { get; set; }

        /// <summary>
        /// Gets or sets the delegate that is invoked to handle the situation when an outbound heartbeat fails.
        /// </summary>
        /// <remarks>
        /// This delegate is called when the heartbeat routine detects consecutive failures (as tracked by the failure count).
        /// The default implementation stops the timer if the failure count exceeds <see cref="MaxFailureTimeout"/>.
        /// You can override this delegate to implement custom logic for handling failed heartbeat attempts,
        /// such as logging additional details, attempting reconnection, or notifying a user interface.
        /// </remarks>
        public Action HandleFailedHeartbeat { get; set; }

        #endregion
        // ----------------------------

        // ------ private delegates -----
        #region Private Default Delegate Implementations
        private Action<string> DefaultInitialBootstrapHandler =>
             IsAuthorityMode ? AuthorityModeInitialBootstrapHandle : TrustlessModeInitialBootstrapHandle;
        private Action<string> AuthorityModeInitialBootstrapHandle => AuthorityModeInitialBootstrap;
        private Action<string> TrustlessModeInitialBootstrapHandle => TrustlessModeInitialBootstrap;
        private Func<NetworkTask, Task<bool>> CheckNetworkTaskHashHandle => ValidateNetworkTaskHash;
        private Func<string, Task<bool>> CheckForErrorResponseHandle => IsErrorResponse;
        private Action<string> HandleErrorResponseHandle => ErrorResponse;
        private Action SendHeartbeatToServer => SendOutgoingHeartbeatToServer;
        private Action FailedHeartbeatHandler => FailedOutgoingHeartbeat;
        #endregion
        // ----------------------------

        private void SendOutgoingHeartbeat(object? sender, System.Timers.ElapsedEventArgs e)
        {
            SendHeartbeatToServer.Invoke();
        }

        protected BootstrapChannelBase()
        {
            // Set default delegate implementations if not already overridden
            InitialBootstrapHandler = DefaultInitialBootstrapHandler;
            HandleErrorResponse = HandleErrorResponseHandle;
            HeartbeatOutHandler = SendHeartbeatToServer;
            IsValidNetworkHash = CheckNetworkTaskHashHandle;
            PacketReturnedErrorResponse = CheckForErrorResponseHandle;
            HandleFailedHeartbeat = FailedHeartbeatHandler;

            // setup hearbeat timer
            _heartbeatTimer = new System.Timers.Timer(8000); // half second
            _heartbeatTimer.Elapsed += SendOutgoingHeartbeat;
            _heartbeatTimer.AutoReset = true;
            _heartbeatTimer.Enabled = false;
        }
        #region Delegate Methods
        private void AuthorityModeInitialBootstrap(string packet)
        {
            // expecting a DataTransmissionPacket with NetworkTask.
            NetworkTask networkTask = Deserialize<NetworkTask>(packet);
            // store server's public key.
            StorePublicKey(networkTask.TaskData["PublicKey"]);
            // process the peer list.
            CollectionSharePacket sharePacket = Deserialize<CollectionSharePacket>(networkTask.TaskData["Peers"]);
            ProcessPeerList(sharePacket);
        }
        private void TrustlessModeInitialBootstrap(string packet)
        {
            // trustless mode
            CollectionSharePacket sharePacket = Deserialize<CollectionSharePacket>(packet);
            ProcessPeerList(sharePacket);
        }

        private async Task<bool> ValidateNetworkTaskHash(NetworkTask task)
        {
            if (task.TaskData.ContainsKey("Signature"))
            {
                // Store and remove the signature for hash computation.
                string signature = task.TaskData["Signature"];
                task.TaskData.Remove("Signature");

                MD5 hashing = MD5.Create();
                // Compute the hash of the task without the signature.
                string computedHash = Convert.ToBase64String(hashing.ComputeHash(task.ToByte()));

                // Create a new network task to request hash verification.
                NetworkTask verifyTask = new NetworkTask()
                {
                    TaskType = TaskType.RequestVerifyHashRecord,
                    TaskData = new Dictionary<string, string>()
                    {
                        { "Hash", computedHash }
                    }
                };

                // Wrap the verification task in a data transmission packet.
                DataTransmissionPacket verifyPacket = new DataTransmissionPacket(verifyTask);
                string jsonPayload = Serialize(verifyPacket);

                // Construct the verify-hash endpoint URI.
                // Adjust the GetEndpointURI parameters as needed for your application.
                Uri verifyHashUri = DistributionProtocol.GetEndpointURI(CommonBootstrapEndpoints.VerifyHash, BootstrapServer.Endpoint);

                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage response = await client.PutAsync(verifyHashUri, new StringContent(jsonPayload, Encoding.UTF8, "application/json"));
                    if (response.IsSuccessStatusCode)
                    {
                        string responseContent = await response.Content.ReadAsStringAsync();
                        // Assuming the response is a serialized PureMessagePacket
                        PureMessagePacket messagePacket = Deserialize<PureMessagePacket>(responseContent);
                        // Expecting a message of the form "True:<hash>" if the hash is valid.
                        if (messagePacket.Message.StartsWith("True"))
                        {
                            // Now verify the signature using the stored public key.
                            return await EncryptionAndSecurityHandler.VerifySignature(signature, publicKey?.KeyData?.ToString());
                        }
                        else
                        {
                            DebugMessage($"Hash verification failed. Server returned: {messagePacket.Message}", MessageType.Debug);
                            return false;
                        }
                    }
                    else
                    {
                        DebugMessage("Failed to call verify hash endpoint.", MessageType.Debug);
                        return false;
                    }
                }
            }
            else
            {
                DebugMessage("No signature found in NetworkTask.", MessageType.Debug);
                return false;
            }
        }

        protected async Task<bool> IsErrorResponse(string packet)
        {
            try
            {
                // Use a JsonDocument to inspect the payload.
                using (JsonDocument doc = JsonDocument.Parse(packet))
                {
                    // If the root element contains a property named "Message",
                    // then we assume this is a PureMessagePacket (indicating an error response)
                    if (doc.RootElement.TryGetProperty("Message", out JsonElement messageProp))
                    {
                        string msg = messageProp.GetString();
                        return !string.IsNullOrEmpty(msg) && msg != "Pinging";
                    }
                    else
                    {
                        // If no "Message" property exists, then it's not an error response.
                        return false;
                    }
                }
            }
            catch (Exception)
            {
                // If any exception is thrown, consider it not an error response.
                return false;
            }
        }
        private void ErrorResponse(string response)
        {
            DebugMessage(response, MessageType.Warning, PeerNetwork.Logging.Bootstrap);
        }

        private async void SendOutgoingHeartbeatToServer()
        {
            // create a heartbeat packet, then send it to the server
            DataTransmissionPacket dataTransmissionPacket = CreateHeartbeatPacket();
            string outgoingPacket = dataTransmissionPacket.ToJsonString();
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage response = await client.PutAsync(DistributionProtocol.GetEndpointURI(CommonBootstrapEndpoints.Heartbeat, BootstrapServerEndpoint), new StringContent(outgoingPacket, Encoding.UTF8, "application/json"));

                    // get server response (ideally a DTP with NetworkTask(s))
                    string responseContent = await response.Content.ReadAsStringAsync();
                    var responsePacket = Deserialize<DataTransmissionPacket>(responseContent);

                    if (responsePacket != null)
                    {
                        // unwrap data, convert to string
                        byte[] data = UnwrapData(responsePacket);
                        string response_ = Encoding.UTF8.GetString(data);

                        // turn NetworkTask's data to Dict<str,str>
                        NetworkTask inboundTask = Deserialize<NetworkTask>(response_);
                        // each value in Dict<str,str> --should-- be another NetworkTask
                        Dictionary<string, string> tasks = inboundTask.TaskData;
                        DebugMessage($"Bootstrap server {BootstrapServerEndpoint.ToString()} responded to heartbeat with {tasks.Count} tasks.", ConsoleColor.Cyan, PeerNetwork.Logging.Bootstrap);
                        foreach (var task in tasks.Values)
                        {
                            string unescapedJson = Regex.Unescape(task); // necessary because of PGP caveats
                            string safeSignature = unescapedJson.Replace("\r", "\\r").Replace("\n", "\\n"); // PGP caveats ect ect
                            try
                            { // leave this wrapped in TryCatch block or otherwise will throw exception
                                var nt = Deserialize<NetworkTask>(safeSignature);
                                if (nt != null)
                                {
                                    DebugMessage($"Enqued task: {unescapedJson}", ConsoleColor.DarkGreen, PeerNetwork.Logging.Bootstrap);
                                    NetworkTaskHandler.incomingNetworkTasks.Enqueue(nt);
                                }
                            }
                            catch (Exception ex)
                            {
                                // we do nothing here
                            }

                        }
                    }
                    else
                    {
                        HandleErrorResponse("Bootstrap response was not in the expected format.");
                    }
                }
            }
            catch (Exception ex)
            {
                FailedOutgoingHeartbeat();
            }
        }

        private void FailedOutgoingHeartbeat()
        {
            failureCount++;
            if (failureCount >= MaxFailureTimeout)
            {
                _heartbeatTimer.Stop();
                _heartbeatTimer.Enabled = false;
                DebugMessage($"Bootstrap server {BootstrapServerEndpoint.ToString()} failed to respond to {MaxFailureTimeout} consecutive requests. Ending heartbeat routine.");
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
            DataTransmissionPacket heartbeatPacket = new DataTransmissionPacket(hbTask);
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
