using Org.BouncyCastle.Asn1.Ocsp;
using P2PNet.Distribution;
using P2PNet.Distribution.NetworkTasks;
using P2PNet.NetworkPackets;
using P2PNet.Peers;
using P2PNet.Peers.CommProtocols;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace P2PNet.DicoveryChannels.WAN
{
    public abstract class BootstrapChannelBase
    {
        public bool IsAuthorityMode { get; set; } = false;
        public BootstrapPeer BootstrapServer { get; set; }
        internal Uri BootstrapServerEndpoint => BootstrapServer.Endpoint;
        internal PGPKeyInfo publicKey { get; set; } // store the public key from the server

        /// <summary>
        /// Indicates whether the channel is currently active and has recently communicated.
        /// False indicates prolonged lack of communication (ie timeout) or failed initialization.
        /// </summary>
        public bool IsActive { get; set; } = false; // indicates if the channel is active or not

        /// <summary>
        /// Gets or sets a value indicating whether the channel supports WebRTC for peer-to-peer communication.
        /// </summary>
        /// <remarks>
        /// When enabled, this property indicates that the channel can utilize WebRTC protocols, which provide features such as real-time communication,
        /// to establish direct and relay-assisted connections between peers.
        /// The default value is <c>false</c>.
        /// </remarks>
        public bool SupportsWebRTC { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the channel supports TURN (Traversal Using Relays around NAT) as a fallback connectivity option.
        /// </summary>
        /// <remarks>
        /// TURN is used as a relay mechanism when direct peer-to-peer connections are not possible due to strict NAT or firewall settings.
        /// When this property is set to <c>true</c>, it indicates peers may use TURN servers to relay traffic from this bootstrap channel.
        /// The default value is <c>false</c>.
        /// </remarks>
        public bool SupportsTURN { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the channel supports NAT hole punching.
        /// </summary>
        /// <remarks>
        /// NAT hole punching is a connectivity technique that enables direct communication between peers located behind NATs by creating temporary mappings
        /// in the NAT device's routing table. Enabling this option signals that the channel can attempt to establish direct connections using this method.
        /// The default value is <c>false</c>.
        /// </remarks>
        public bool SupportsNATHolepunching { get; set; } = false;


        public string PublicKey => Encoding.UTF8.GetString(publicKey.KeyData); // expose the public key as a string for easy access

        /// <summary>
        /// Gets or sets the maximum number of consecutive connection failures before the event stream is terminated.
        /// </summary>
        /// <remarks>
        /// If the number of stream connection failures reaches this threshold, the <see cref="BootstrapChannelBase.HandleStreamConnectionFailure"> delegate is invoked.
        /// The default value is 5.
        /// </remarks>
        public int MaxFailureTimeout { get; set; } = 5;

        /// <summary>
        /// Cancellation token source for the bootstrap channel stream connection.
        /// </summary>
        protected CancellationTokenSource _streamCts;

        /// <summary>
        /// HTTP client used for the SSE bootstrap channel stream connection.
        /// </summary>
        protected HttpClient _streamClient;

        /// <summary>
        /// Indicates whether the bootstrap channel stream is currently running.
        /// </summary>
        public bool BootstrapChannelStreamRunning { get; protected set; } = false;

        /// <summary>
        /// Starts the SSE stream connection to receive network tasks from the bootstrap server.
        /// </summary>
        /// <remarks>
        /// This method establishes a persistent SSE connection with the bootstrap server for real-time communication.
        /// Any exceptions during stream initialization are caught and logged.
        /// </remarks>
        public void StartBootstrapChannelStream()
        {
            if (BootstrapChannelStreamRunning)
            {
                return; // already running
            }

            try
            {
                _streamCts = new CancellationTokenSource();
                Task.Run(() => RunBootstrapStreamLoop(_streamCts.Token));
                BootstrapChannelStreamRunning = true;
            }
            catch (Exception ex)
            {
                DebugMessage($"Failed to start bootstrap channel stream: {ex.Message}", MessageType.Warning, PeerNetwork.Logging.Bootstrap);
            }
        }

        /// <summary>
        /// Stops the SSE stream connection.
        /// </summary>
        /// <remarks>
        /// This method terminates the persistent SSE connection to the bootstrap server.
        /// Any exceptions during stream termination are caught and suppressed.
        /// </remarks>
        public void StopBootstrapChannelStream()
        {
            try
            {
                _streamCts?.Cancel();
                _streamClient?.Dispose();
                BootstrapChannelStreamRunning = false;
            }
            catch
            {
                // Exceptions are suppressed for graceful shutdown.
            }
        }

        private int failureCount { get; set; } = 0;

        #region TURN Connection Helpers
        /// <summary>
        /// HTTP client used for TURN connection requests.
        /// </summary>
        protected HttpClient _turnHttpClient;

        /// <summary>
        /// Dictionary tracking all active TURN connections owned by this bootstrap channel.
        /// Key is the connection ID, value is the TurnNetProtocol instance.
        /// </summary>
        private readonly ConcurrentDictionary<string, TurnNetProtocol> _activeTurnConnections = new();

        /// <summary>
        /// Gets the HTTP client used for TURN relay operations.
        /// </summary>
        /// <returns>The HTTP client configured for TURN operations.</returns>
        public HttpClient GetTurnHttpClient()
        {
            if (_turnHttpClient == null)
                _turnHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            return _turnHttpClient;
        }

        /// <summary>
        /// Registers a TURN connection in the active connections dictionary.
        /// </summary>
        /// <param name="connectionId">The unique identifier for the TURN connection.</param>
        /// <param name="protocol">The TURN protocol instance to track.</param>
        internal void RegisterTurnConnection(string connectionId, TurnNetProtocol protocol)
        {
            _activeTurnConnections.TryAdd(connectionId, protocol);
        }

        /// <summary>
        /// Unregisters a TURN connection from the active connections dictionary.
        /// Called when a TURN connection is closed.
        /// </summary>
        /// <param name="connectionId">The unique identifier of the TURN connection to unregister.</param>
        internal void UnregisterTurnConnection(string connectionId)
        {
            _activeTurnConnections.TryRemove(connectionId, out _);
        }

        /// <summary>
        /// Gets the number of active TURN connections owned by this bootstrap channel.
        /// </summary>
        public int ActiveTurnConnectionCount => _activeTurnConnections.Count;

        /// <summary>
        /// Requests a TURN relay connection to the specified peer through the bootstrap server.
        /// </summary>
        /// <param name="targetPeerIdentifier">The identifier of the peer to connect to via TURN relay.</param>
        /// <returns>
        /// A task that represents the asynchronous operation. 
        /// The task result contains a <see cref="Peers.TurnPeer"/> if the connection was established, or null if it failed.
        /// </returns>
        /// <remarks>
        /// This method sends a TURN connection request to the bootstrap server, which will notify the target peer
        /// and establish a relay channel. Both peers can then communicate through the TURN relay.
        /// </remarks>
        public async Task<Peers.TurnPeer> RequestTurnConnectionAsync(string targetPeerIdentifier)
        {
            if (!SupportsTURN)
            {
                DebugMessage("TURN is not supported by this bootstrap server.", MessageType.Warning, PeerNetwork.Logging.Bootstrap);
                return null;
            }

            if (string.IsNullOrWhiteSpace(targetPeerIdentifier))
            {
                DebugMessage("Target peer identifier cannot be null or empty.", MessageType.Warning, PeerNetwork.Logging.Bootstrap);
                return null;
            }

            try
            {
                if (_turnHttpClient == null)
                    _turnHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

                var turnConnectUri = DistributionProtocol.GetEndpointURI(CommonBootstrapEndpoints.TurnConnect, BootstrapServerEndpoint);
                var requestUri = $"{turnConnectUri}?initiatorId={Uri.EscapeDataString(PeerNetwork.Identifier)}&targetId={Uri.EscapeDataString(targetPeerIdentifier)}";

                using var response = await _turnHttpClient.PostAsync(requestUri, null);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    DebugMessage($"TURN connection request failed: {response.StatusCode} - {errorContent}", MessageType.Warning, PeerNetwork.Logging.Bootstrap);
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                
                // Parse the connection ID from response
                var connectionId = responseContent.Trim().Trim('"');
                if (string.IsNullOrEmpty(connectionId))
                {
                    DebugMessage("TURN connection response did not contain a valid connection ID.", MessageType.Warning, PeerNetwork.Logging.Bootstrap);
                    return null;
                }

                // Create the TURN protocol with a reference back to this channel
                var turnProtocol = new TurnNetProtocol(this, targetPeerIdentifier, connectionId);
                
                // Register the connection for lifecycle management
                RegisterTurnConnection(connectionId, turnProtocol);
                
                // Start the continuous stream loops
                turnProtocol.StartStreamLoops();

                // Create the TURN peer with a reference to this channel
                var turnPeer = new Peers.TurnPeer(this, targetPeerIdentifier, turnProtocol);

                DebugMessage($"TURN connection established with peer {targetPeerIdentifier} (Connection ID: {connectionId})", ConsoleColor.Green, PeerNetwork.Logging.Bootstrap);

                return turnPeer;
            }
            catch (Exception ex)
            {
                DebugMessage($"Failed to request TURN connection: {ex.Message}", MessageType.Warning, PeerNetwork.Logging.Bootstrap);
                return null;
            }
        }

        /// <summary>
        /// Requests a TURN relay connection and automatically adds the peer to the network.
        /// </summary>
        /// <param name="targetPeerIdentifier">The identifier of the peer to connect to via TURN relay.</param>
        /// <returns>
        /// A task that represents the asynchronous operation.
        /// The task result is true if the connection was established and peer was added, false otherwise.
        /// </returns>
        public async Task<bool> ConnectViaTurnAsync(string targetPeerIdentifier)
        {
            var turnPeer = await RequestTurnConnectionAsync(targetPeerIdentifier);
            if (turnPeer == null)
            {
                return false;
            }

            await PeerNetwork.AddTurnPeer(turnPeer);
            return true;
        }

        /// <summary>
        /// Closes an active TURN connection with the specified peer.
        /// </summary>
        /// <param name="connectionId">The unique identifier of the TURN connection to close.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task CloseTurnConnectionAsync(string connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return;
            }

            try
            {
                if (_turnHttpClient == null)
                    _turnHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

                var turnCloseUri = DistributionProtocol.GetEndpointURI(CommonBootstrapEndpoints.TurnClose, BootstrapServerEndpoint);
                var requestUri = $"{turnCloseUri}?connectionId={Uri.EscapeDataString(connectionId)}&peerId={Uri.EscapeDataString(PeerNetwork.Identifier)}";

                await _turnHttpClient.DeleteAsync(requestUri);
                DebugMessage($"TURN connection {connectionId} closed.", ConsoleColor.DarkGray, PeerNetwork.Logging.Bootstrap);
            }
            catch (Exception ex)
            {
                DebugMessage($"Failed to close TURN connection: {ex.Message}", MessageType.Warning, PeerNetwork.Logging.Bootstrap);
            }
        }
        #endregion


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
        /// Gets or sets the delegate that handles incoming network tasks from the bootstrap channel stream.
        /// </summary>
        /// <remarks>
        /// This delegate is invoked for each network task received from the bootstrap server's stream.
        /// The default implementation queues tasks for processing via the <see cref="NetworkTaskHandler"/>.
        /// You can override this delegate to customize how incoming tasks are handled.
        /// </remarks>
        public Action<NetworkTask> HandleIncomingStreamTask { get; set; }

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
        /// Gets or sets the delegate that is invoked to handle the situation when the bootstrap channel stream connection fails.
        /// </summary>
        /// <remarks>
        /// This delegate is called when the stream detects consecutive connection failures.
        /// The default implementation terminates the stream if the failure count exceeds <see cref="MaxFailureTimeout"/>.
        /// You can override this delegate to implement custom logic for handling failed stream connections,
        /// such as logging additional details, attempting reconnection, or notifying a user interface.
        /// </remarks>
        public Action HandleStreamConnectionFailure { get; set; }

        #endregion
        // ----------------------------

        // ------ private delegates -----
        #region Private Default Delegate Implementations
        protected Action<string> DefaultInitialBootstrapHandler
        {
            get { if(IsAuthorityMode) { return AuthorityModeInitialBootstrapHandle; } else { return TrustlessModeInitialBootstrapHandle; } }
            set { InitialBootstrapHandler = value; }
        }
        protected Action<string> AuthorityModeInitialBootstrapHandle => AuthorityModeInitialBootstrap;
        protected Action<string> TrustlessModeInitialBootstrapHandle => TrustlessModeInitialBootstrap;
        private Func<NetworkTask, Task<bool>> CheckNetworkTaskHashHandle => ValidateNetworkTaskHash;
        private Func<string, Task<bool>> CheckForErrorResponseHandle => IsErrorResponse;
        private Action<string> HandleErrorResponseHandle => ErrorResponse;
        private Action<NetworkTask> DefaultIncomingStreamTaskHandler => ProcessIncomingChannelTask;
        private Action StreamConnectionFailureHandler => HandleChannelConnectionFailed;
        #endregion
        // ----------------------------

        protected BootstrapChannelBase()
        {
            // Set default delegate implementations if not already overridden
            InitialBootstrapHandler = DefaultInitialBootstrapHandler;
            HandleErrorResponse = HandleErrorResponseHandle;
            HandleIncomingStreamTask = DefaultIncomingStreamTaskHandler;
            IsValidNetworkHash = CheckNetworkTaskHashHandle;
            PacketReturnedErrorResponse = CheckForErrorResponseHandle;
            HandleStreamConnectionFailure = StreamConnectionFailureHandler;
        }

        #region Default Delegate Methods
        private void AuthorityModeInitialBootstrap(string packet)
        {
            // expecting a DataTransmissionPacket with NetworkTask.
            NetworkTask networkTask = Deserialize<NetworkTask>(packet);
            // store server's public key.
            StorePublicKey(networkTask.TaskData["PublicKey"]);
            // process the peer list.
            CollectionSharePacket sharePacket = Deserialize<CollectionSharePacket>(networkTask.TaskData["Peers"]);
            ProcessPeerList(sharePacket);
            // check if extra srvcs are available
            if (networkTask.TaskData.ContainsKey("WebRTC"))
            {
                this.SupportsWebRTC = bool.Parse(networkTask.TaskData["WebRTC"]);
            }
            if (networkTask.TaskData.ContainsKey("TURN"))
            {
                this.SupportsTURN = bool.Parse(networkTask.TaskData["TURN"]);
            }
            if (networkTask.TaskData.ContainsKey("NATHolepunch"))
            {
                this.SupportsNATHolepunching = bool.Parse(networkTask.TaskData["NATHolepunch"]);
            }
            // start the bootstrap channel stream to receive tasks from the bootstrap server
            StartBootstrapChannelStream();
            // set the channel to active
            IsActive = true;
        }

        private void TrustlessModeInitialBootstrap(string packet)
        {
            // trustless mode
            CollectionSharePacket sharePacket = Deserialize<CollectionSharePacket>(packet);
            ProcessPeerList(sharePacket);
            // start bootstrap channel stream for trustless mode as well
            StartBootstrapChannelStream();
            // set the channel to active
            IsActive = true;
        }

        private async Task<bool> ValidateNetworkTaskHash(NetworkTask task)
        {
            if (task.TaskData.ContainsKey("Signature"))
            {
                // remove the signature for hash computation
                task.TaskData.Remove("Signature");
                // compute the hash of the task without the signature
                string computedHash = EncryptionAndSecurityHandler.GetMD5Hash(Serialize(task)).Result;

                // create a new network task to request hash verification.
                NetworkTask verifyTask = NetworkTaskHandler.CreateRequestVerifyHashRecordTask(computedHash);

                // Wrap the verification task in a data transmission packet.
                DataTransmissionPacket verifyPacket = new DataTransmissionPacket(verifyTask);
                string jsonPayload = Serialize(verifyPacket);

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
                            return true;
                        }
                        else
                        {
                            HandleErrorResponse($"Hash verification failed. Server returned: {messagePacket.Message}");
                            return false;
                        }
                    }
                    else
                    {
                        HandleErrorResponse("Failed to call verify hash endpoint.");
                        return false;
                    }
                }
            }
            else
            {
                HandleErrorResponse("No signature found in NetworkTask.");
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

        /// <summary>
        /// Main bootstrap channel stream loop that connects to the bootstrap server's SSE endpoint and processes incoming tasks.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to stop the stream.</param>
        private async Task RunBootstrapStreamLoop(CancellationToken cancellationToken)
        {
            string streamUrl = DistributionProtocol.GetEndpointURI(CommonBootstrapEndpoints.EventStream, BootstrapServerEndpoint).ToString()
                + $"?peerId={Uri.EscapeDataString(PeerNetwork.Identifier)}";

            while (!cancellationToken.IsCancellationRequested && failureCount < MaxFailureTimeout)
            {
                try
                {
                    _streamClient = new HttpClient();
                    _streamClient.Timeout = TimeSpan.FromMinutes(30); // long timeout for SSE

                    using var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
                    using var response = await _streamClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var reader = new StreamReader(stream);

                    failureCount = 0; // reset on successful connection
                    DebugMessage($"Connected to bootstrap channel stream at {BootstrapServerEndpoint}", ConsoleColor.Cyan, PeerNetwork.Logging.Bootstrap);

                    while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        // SSE format: data: <json>
                        if (line.StartsWith("data: "))
                        {
                            string taskJson = line.Substring(6);
                            try
                            {
                                NetworkTask task = Deserialize<NetworkTask>(taskJson);
                                if (task != null)
                                {
                                    // skip keep-alive tasks from processing
                                    if (task.TaskType == TaskType.StreamKeepAlive)
                                    {
                                        DebugMessage($"Received keep-alive from {BootstrapServerEndpoint}", ConsoleColor.DarkGray, PeerNetwork.Logging.Bootstrap);
                                        continue;
                                    }

                                    HandleIncomingStreamTask?.Invoke(task);
                                }
                            }
                            catch (Exception parseEx)
                            {
                                DebugMessage($"Failed to parse SSE event: {parseEx.Message}", MessageType.Warning, PeerNetwork.Logging.Bootstrap);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    DebugMessage($"Bootstrap channel stream connection failed ({failureCount}/{MaxFailureTimeout}): {ex.Message}", MessageType.Warning, PeerNetwork.Logging.Bootstrap);

                    if (failureCount >= MaxFailureTimeout)
                    {
                        HandleStreamConnectionFailure?.Invoke();
                        break;
                    }

                    // wait before reconnecting
                    await Task.Delay(TimeSpan.FromSeconds(5 * failureCount), cancellationToken);
                }
                finally
                {
                    _streamClient?.Dispose();
                    _streamClient = null;
                }
            }

            BootstrapChannelStreamRunning = false;
            DebugMessage($"Bootstrap channel stream loop ended for {BootstrapServerEndpoint}", MessageType.General, PeerNetwork.Logging.Bootstrap);
        }

        /// <summary>
        /// Default handler for incoming stream tasks. Queues tasks for processing via the NetworkTaskHandler.
        /// </summary>
        /// <param name="task">The incoming network task.</param>
        private void ProcessIncomingChannelTask(NetworkTask task)
        {
            NetworkTaskHandler.EnqueueIncomingNetworkTask(task, new NetworkTaskOriginInfo(BootstrapServer, PublicKey));
            DebugMessage($"Received task {task.TaskType} from {BootstrapServerEndpoint}", ConsoleColor.Cyan, PeerNetwork.Logging.Bootstrap);
        }

        /// <summary>
        /// Default handler for bootstrap channel stream connection failures.
        /// </summary>
        private void HandleChannelConnectionFailed()
        {
            BootstrapChannelStreamRunning = false;
            IsActive = false;
            HandleErrorResponse($"Bootstrap server {BootstrapServerEndpoint} failed to respond to {MaxFailureTimeout} consecutive connection attempts. Event stream terminated.");
        }
        #endregion

        #region General Helper Methods
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

        protected void StorePublicKey(string publicKeyCrosscheck)
        {
            // Trim surrounding whitespace
            string sanitizedKey = publicKeyCrosscheck.Trim();
            publicKey = new PGPKeyInfo("PublicKey", Encoding.UTF8.GetBytes(publicKeyCrosscheck));
        }

        protected void ProcessPeerList(CollectionSharePacket peerList)
        {
            PeerNetwork.ProcessPeerList(peerList);
        }

        public virtual void CloseBootstrapChannel()
        {
            StopBootstrapChannelStream();
            CloseAllTurnConnections();
            IsActive = false;
        }

        /// <summary>
        /// Closes all active TURN connections owned by this bootstrap channel.
        /// Called automatically when the bootstrap channel is closed.
        /// </summary>
        private void CloseAllTurnConnections()
        {
            foreach (var kvp in _activeTurnConnections)
            {
                try
                {
                    kvp.Value.Dispose();
                }
                catch
                {
                    // Best effort cleanup
                }
            }
            _activeTurnConnections.Clear();
            _turnHttpClient?.Dispose();
            _turnHttpClient = null;
        }
        #endregion
    }
}
