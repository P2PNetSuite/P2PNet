global using static P2PNet.PeerNetwork;
global using static P2PNet.Distribution.DistributionProtocol;
global using static ConsoleDebugger.ConsoleDebugger;
global using static P2PBootstrap.GlobalConfig;
global using static P2PBootstrap.Database.DatabaseService;
global using static P2PBootstrap.Encryption.EncryptionService;
global using P2PNet.Distribution;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using P2PNet;
using P2PNet.NetworkPackets;
using P2PNet.Peers;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using P2PBootstrap.CLI;
using System.IO;
using System.Text.Json;
using P2PBootstrap.Database;
using Microsoft.Extensions.FileProviders;
using ConsoleDebugger;
using P2PBootstrap.Encryption;
using P2PNet.Distribution.NetworkTasks;
using System.Text;
using System.Security.Cryptography;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.CompilerServices;

namespace P2PBootstrap
{
    public class Program
    {
        public static ClientPeerList ClientPeers = new ClientPeerList();
        public static string PublicKeyToString => Encoding.UTF8.GetString(GlobalConfig.ActiveKeys.Public.KeyData);
        public static void Main(string[] args)
        {
            LoggingConfiguration.LoggerStyle = LogStyle.PlainTextFormat;
            LoggingConfiguration.LoggerActive = true;
            PeerNetwork.Logging.OutputLogMessages = true;

            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(ConfigFile, optional: false, reloadOnChange: true);

            AppSettings = config.Build();

            // check if application is running in container or not
            GlobalConfig.CheckContainerEnvironment();

            Identifier = ConfigIdentifier(); // set identifier

            var builder = WebApplication.CreateBuilder(args);
            builder.Logging.AddFilter("Microsoft", LogLevel.None);
            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            // Enable default files and static files
            app.UseDefaultFiles(); // Serves index.html by default
            app.UseStaticFiles();

            var DBdirectory = Path.Combine(Directory.GetCurrentDirectory(), GlobalConfig.DbFileName());
            if (!Directory.Exists(DBdirectory))
            {
                Directory.CreateDirectory(DBdirectory);
            }

            app.UseRouting();

            app.MapPut(DistributionProtocol.BootstrapServerAPIendpoints[CommonBootstrapEndpoints.Bootstrap], async Task<IResult> (HttpContext context) =>
            {
                DebugMessage("New inbound peer detected.", MessageType.Debug);
                // read the incoming PUT
                using var reader = new StreamReader(context.Request.Body);
                    var bodyJson = await reader.ReadToEndAsync();
                    // deserialize the input
                    var incomingPacket = Deserialize<DataTransmissionPacket>(bodyJson);

                    // TODO improve logic for handling incoming peer verification
                    // ie Identifier values
                    if (incomingPacket != null)
                    {
                        string IDpacketJSON = Encoding.UTF8.GetString(UnwrapData(Deserialize<DataTransmissionPacket>(bodyJson)));
                        IdentifierPacket identifierPacket = Deserialize<IdentifierPacket>(IDpacketJSON);
                        IPeer newPeer = new GenericPeer(IPAddress.Parse(identifierPacket.IP), identifierPacket.SourceOriginIdentifier,  identifierPacket.Data);
                        KnownPeers.Add(newPeer); // add the new peer to the known peers list
                        ClientPeers.Add(new ClientPeer(newPeer)); // add the new peer to the client peers list
                        // we DO NOT use PeerNetwork.AddPeer(...) otherwise a PeerChannel will be made active
                    }

                    if (GlobalConfig.TrustPolicy() == TrustPolicies.BootstrapTrustPolicyType.Trustless)
                    {
                            // reply with a CollectionSharePacket
                            var share = new CollectionSharePacket(100, KnownPeers);
                            string peershareJson = Serialize(share);
                            byte[] peerslistBytes = Encoding.UTF8.GetBytes(peershareJson);
                            DataTransmissionPacket dptResponse = new DataTransmissionPacket()
                            {
                                DataType = DataPayloadFormat.MiscData,
                                Data = peerslistBytes
                            };
                            string responseJson = dptResponse.ToJsonString();
                        return Results.Content(responseJson, "application/json");
                    }
                    else
                    {

                        // reply with a DataTransmissionPacket holding public key and peer list
                        var networkTask = new NetworkTask()
                        {
                            TaskType = TaskType.BootstrapInitialization,
                            TaskData = new Dictionary<string, string>()
                                {
                                    { "PublicKey", PublicKeyToString },
                                    { "Peers", Serialize(new CollectionSharePacket(100, KnownPeers)) },
                                    { "WebRTC", GlobalConfig.OptionalServices.WebRTC().ToString() },
                                    { "NATHolepunch", GlobalConfig.OptionalServices.UDPNATHolepunch().ToString() },
                                    { "TURN", GlobalConfig.OptionalServices.TURN().ToString() }
                                }
                        };

                        var outPacket = new DataTransmissionPacket(networkTask);

                        var responseJson = outPacket.ToJsonString();

                    return Results.Content(responseJson, "application/json");
                    }
                
            });

            app.MapPut(DistributionProtocol.BootstrapServerAPIendpoints[CommonBootstrapEndpoints.VerifyHash], async Task<IResult> (HttpContext context) =>
            {
                if (GlobalConfig.TrustPolicy() != TrustPolicies.BootstrapTrustPolicyType.Trustless)
                {
                    // read the PUT 
                    using var reader = new StreamReader(context.Request.Body);
                    string bodyJson = await reader.ReadToEndAsync();

                    // Deserialize the incoming DataTransmissionPacket.
                    var incomingPacket = Deserialize<DataTransmissionPacket>(bodyJson);

                    // null check
                    if (incomingPacket == null || incomingPacket.Data == null)
                    {
                        return Results.Text(Serialize<PureMessagePacket>(new PureMessagePacket("Invalid DataTransmissionPacket received.")), "application/json", statusCode: 400);
                    }

                    // extract the NetworkTask from the DataTransmissionPacket Data field.
                    string ntJson = Encoding.UTF8.GetString(UnwrapData(incomingPacket));
                    NetworkTask task = Deserialize<NetworkTask>(ntJson);

                    // verify the task type.
                    if (task.TaskType != TaskType.RequestVerifyHashRecord)
                    {
                        return Results.Text(Serialize<PureMessagePacket>(new PureMessagePacket("Invalid network task type for this endpoint.")), "application/json", statusCode: 400);
                    }

                    // check for the 'Hash' key.
                    if (!task.TaskData.ContainsKey("Hash"))
                    {
                        return Results.Text(Serialize<PureMessagePacket>(new PureMessagePacket("Missing 'Hash' key in TaskData.")), "application/json", statusCode: 400);
                    }

                    string hashValue = task.TaskData["Hash"];
                    bool exists = DatabaseService.VerifyHashRecord(hashValue);

                    // prepare a PureMessagePacket indicating whether the hash was found.
                    var replyPacket = new PureMessagePacket
                    {
                        Message = (exists ? $"True:{hashValue}" : $"False:{hashValue}")
                    };

                    // return the serialized PureMessagePacket as application/json.
                    return Results.Content(Serialize<PureMessagePacket>(replyPacket), "application/json");
                }
                else
                {
                    // trustless policy, just return a message indicating this
                    return Results.Content(Serialize<PureMessagePacket>(new PureMessagePacket("Trustless policy in effect, no hash verification performed.")), "application/json");
                }
            });

            app.MapGet(DistributionProtocol.BootstrapServerAPIendpoints[CommonBootstrapEndpoints.EventStream], async (HttpContext context, CancellationToken cancellationToken) =>
            {
                string peerId = context.Request.Query["peerId"];
                if (string.IsNullOrWhiteSpace(peerId))
                {
                    return Results.Text("Missing 'peerId' query parameter.", "text/plain", statusCode: 400);
                }

                // verify the peer exists
                if (!ClientPeers.TryGetValue(peerId, out ClientPeer clientPeer))
                {
                    return Results.Text($"Peer with identifier '{peerId}' not found.", "text/plain", statusCode: 404);
                }

                // update the client's last incoming time
                clientPeer.UpdateTimeIn();

                // set up SSE response headers
                context.Response.Headers.Append("Content-Type", "text/event-stream");
                context.Response.Headers.Append("Cache-Control", "no-cache");
                context.Response.Headers.Append("Connection", "keep-alive");
                context.Response.Headers.Append("X-Accel-Buffering", "no");

                await foreach (var sseEvent in GetPeerServerSentEvents(peerId, clientPeer, cancellationToken))
                {
                    await context.Response.WriteAsync(sseEvent, cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }

                return Results.Text(string.Empty, "text/plain", statusCode: 200);
            });

            if (GlobalConfig.OptionalEndpoints.ServePublicIP() == true)
            {
                app.MapGet(DistributionProtocol.BootstrapServerAPIendpoints[CommonBootstrapEndpoints.GetPublicIP], async (HttpContext context) =>
                {
                    var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
                    string clientIp = string.Empty;

                    if (!string.IsNullOrEmpty(forwardedFor))
                    {
                        clientIp = forwardedFor.Split(',').First().Trim();
                    }
                    else
                    {
                        clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                    }
                    return Results.Text(clientIp, "text/plain");
                });
            }

            if(GlobalConfig.OptionalServices.TURN() == true)
            {
                // WebRTC signaling endpoint - routes WebRTC offers/answers/ICE candidates between peers
                app.MapPut(DistributionProtocol.BootstrapServerAPIendpoints[CommonBootstrapEndpoints.Signal], async Task<IResult> (HttpContext context) =>
                {
                    using var reader = new StreamReader(context.Request.Body);
                    string bodyJson = await reader.ReadToEndAsync();

                    var incomingPacket = Deserialize<DataTransmissionPacket>(bodyJson);
                    if (incomingPacket == null || incomingPacket.Data == null)
                    {
                        return Results.Text("Invalid DataTransmissionPacket received.", "text/plain", statusCode: 400);
                    }

                    string ntJson = Encoding.UTF8.GetString(UnwrapData(incomingPacket));
                    NetworkTask task = Deserialize<NetworkTask>(ntJson);

                    // Only allow WebRTC signaling types
                    if (task == null ||
                        (task.TaskType != TaskType.WebRTCOffer &&
                         task.TaskType != TaskType.WebRTCAnswer &&
                         task.TaskType != TaskType.WebRTCIceCandidate))
                    {
                        return Results.Text("Invalid or unsupported network task type for this endpoint.", "text/plain", statusCode: 400);
                    }

                    // Route to recipient
                    if (!task.TaskData.TryGetValue("Recipient", out var recipientId) || string.IsNullOrWhiteSpace(recipientId))
                    {
                        return Results.Text("Missing 'Recipient' in TaskData.", "text/plain", statusCode: 400);
                    }

                    if (!ClientPeers.TryGetValue(recipientId, out ClientPeer recipientPeer))
                    {
                        return Results.Text($"Recipient peer '{recipientId}' not found.", "text/plain", statusCode: 404);
                    }

                    // Route via the TURN service event channel
                    TURNService.EnqueueTaskForPeer(recipientId, task);

                    return Results.Text("OK", "text/plain");
                });

                // TURN connection initiation endpoint
                app.MapPut(DistributionProtocol.BootstrapServerAPIendpoints[CommonBootstrapEndpoints.TurnConnect], async Task<IResult> (HttpContext context) =>
                {
                    using var reader = new StreamReader(context.Request.Body);
                    string bodyJson = await reader.ReadToEndAsync();

                    var incomingPacket = Deserialize<DataTransmissionPacket>(bodyJson);
                    if (incomingPacket == null || incomingPacket.Data == null)
                    {
                        return Results.Text("Invalid DataTransmissionPacket received.", "text/plain", statusCode: 400);
                    }

                    string ntJson = Encoding.UTF8.GetString(UnwrapData(incomingPacket));
                    NetworkTask connectTask = Deserialize<NetworkTask>(ntJson);

                    if (connectTask == null || connectTask.TaskType != TaskType.TurnConnectionRequest)
                    {
                        return Results.Text("Expected TurnConnectionRequest task type.", "text/plain", statusCode: 400);
                    }

                    if (!connectTask.TaskData.TryGetValue("InitiatorId", out var initiatorId) ||
                        !connectTask.TaskData.TryGetValue("TargetId", out var targetId))
                    {
                        return Results.Text("Missing 'InitiatorId' or 'TargetId' in TaskData.", "text/plain", statusCode: 400);
                    }

                    // verify both peers exist
                    if (!ClientPeers.TryGetValue(initiatorId, out _))
                    {
                        return Results.Text($"Initiator peer '{initiatorId}' not found.", "text/plain", statusCode: 404);
                    }
                    if (!ClientPeers.TryGetValue(targetId, out _))
                    {
                        return Results.Text($"Target peer '{targetId}' not found.", "text/plain", statusCode: 404);
                    }

                    var connection = TURNService.InitiateTurnConnection(initiatorId, targetId);
                    if (connection == null)
                    {
                        return Results.Text("Failed to establish TURN connection.", "text/plain", statusCode: 500);
                    }

                    // return connection key to initiator
                    var responseTask = new NetworkTask
                    {
                        TaskType = TaskType.TurnConnectionEstablished,
                        TaskData = new Dictionary<string, string>
                        {
                            { "ConnectionKey", $"{initiatorId}|{targetId}".Split('|').OrderBy(x => x).Aggregate((a, b) => $"{a}|{b}") },
                            { "TargetId", targetId }
                        }
                    };
                    var responsePacket = new DataTransmissionPacket(responseTask);
                    return Results.Text(responsePacket.ToJsonString(), "application/json");
                });

                // TURN data relay endpoint
                app.MapPut(DistributionProtocol.BootstrapServerAPIendpoints[CommonBootstrapEndpoints.TurnRelay], async Task<IResult> (HttpContext context) =>
                {
                    using var reader = new StreamReader(context.Request.Body);
                    string bodyJson = await reader.ReadToEndAsync();

                    var incomingPacket = Deserialize<DataTransmissionPacket>(bodyJson);
                    if (incomingPacket == null || incomingPacket.Data == null)
                    {
                        return Results.Text("Invalid DataTransmissionPacket received.", "text/plain", statusCode: 400);
                    }

                    string ntJson = Encoding.UTF8.GetString(UnwrapData(incomingPacket));
                    NetworkTask relayTask = Deserialize<NetworkTask>(ntJson);

                    if (relayTask == null || relayTask.TaskType != TaskType.TurnRelayData)
                    {
                        return Results.Text("Expected TurnRelayData task type.", "text/plain", statusCode: 400);
                    }

                    if (!relayTask.TaskData.TryGetValue("ConnectionKey", out var connectionKey) ||
                        !relayTask.TaskData.TryGetValue("SenderId", out var senderId) ||
                        !relayTask.TaskData.TryGetValue("Data", out var data))
                    {
                        return Results.Text("Missing required fields in TaskData.", "text/plain", statusCode: 400);
                    }

                    bool relayed = TURNService.RelayData(connectionKey, senderId, data);
                    if (!relayed)
                    {
                        return Results.Text("Failed to relay data. Connection may not exist.", "text/plain", statusCode: 404);
                    }

                    return Results.Text("OK", "text/plain");
                });

                // TURN stream endpoint - SSE for receiving relayed data
                app.MapGet(DistributionProtocol.BootstrapServerAPIendpoints[CommonBootstrapEndpoints.TurnStream], async (HttpContext context, CancellationToken cancellationToken) =>
                {
                    string connectionKey = context.Request.Query["connectionKey"];
                    string receiverId = context.Request.Query["receiverId"];

                    if (string.IsNullOrWhiteSpace(connectionKey) || string.IsNullOrWhiteSpace(receiverId))
                    {
                        return Results.Text("Missing 'connectionKey' or 'receiverId' query parameters.", "text/plain", statusCode: 400);
                    }

                    var connection = TURNService.GetConnection(connectionKey);
                    if (connection == null)
                    {
                        return Results.Text("TURN connection not found.", "text/plain", statusCode: 404);
                    }

                    // verify receiver is part of this connection
                    if (receiverId != connection.InitiatorId && receiverId != connection.TargetId)
                    {
                        return Results.Text("Unauthorized.", "text/plain", statusCode: 401);
                    }

                    // set up SSE response headers
                    context.Response.Headers.Append("Content-Type", "text/event-stream");
                    context.Response.Headers.Append("Cache-Control", "no-cache");
                    context.Response.Headers.Append("Connection", "keep-alive");
                    context.Response.Headers.Append("X-Accel-Buffering", "no");

                    await foreach (var data in TURNService.ReadRelayedDataAsync(connectionKey, receiverId, cancellationToken))
                    {
                        // format as SSE event with data payload
                        string sseEvent = $"data: {data}\n\n";
                        await context.Response.WriteAsync(sseEvent, cancellationToken);
                        await context.Response.Body.FlushAsync(cancellationToken);
                    }

                    return Results.Text("OK", "text/plain");
                });

                // TURN connection close endpoint
                app.MapPut(DistributionProtocol.BootstrapServerAPIendpoints[CommonBootstrapEndpoints.TurnClose], async Task<IResult> (HttpContext context) =>
                {
                    using var reader = new StreamReader(context.Request.Body);
                    string bodyJson = await reader.ReadToEndAsync();

                    var incomingPacket = Deserialize<DataTransmissionPacket>(bodyJson);
                    if (incomingPacket == null || incomingPacket.Data == null)
                    {
                        return Results.Text("Invalid DataTransmissionPacket received.", "text/plain", statusCode: 400);
                    }

                    string ntJson = Encoding.UTF8.GetString(UnwrapData(incomingPacket));
                    NetworkTask closeTask = Deserialize<NetworkTask>(ntJson);

                    if (closeTask == null || closeTask.TaskType != TaskType.TurnConnectionClosed)
                    {
                        return Results.Text("Expected TurnConnectionClosed task type.", "text/plain", statusCode: 400);
                    }

                    if (!closeTask.TaskData.TryGetValue("ConnectionKey", out var connectionKey))
                    {
                        return Results.Text("Missing 'ConnectionKey' in TaskData.", "text/plain", statusCode: 400);
                    }

                    bool closed = TURNService.CloseTurnConnection(connectionKey);
                    if (!closed)
                    {
                        return Results.Text("Failed to close connection. Connection may not exist.", "text/plain", statusCode: 404);
                    }

                    return Results.Text("OK", "text/plain");
                });
            }

            // TODO secure this against remote access
            #region Internal API Endpoints -- NOT FOR PUBLIC CONSUMPTION
            app.MapGet("/api/parser/output", () =>
            {
                if (Parser.OutputQueue.Count > 0)
                {
                    return Results.Text(Parser.OutputQueue.Dequeue(), "text/plain");
                }
                return Results.NoContent();
            });
            app.MapPut("/api/parser/input", async (HttpContext context) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var input = await reader.ReadToEndAsync();
                Parser.InputQueue.Enqueue(input);
                return Results.Text(string.Empty, "text/plain", statusCode: 200);
            });
            // endpoint for managing peers
            app.MapPut("/api/managepeer", async (HttpContext context) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                string body = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body))
                {
                    return Results.Text("Empty request body.", "text/plain", statusCode: 400);
                }

                // Parse the JSON body
                var jsonDoc = System.Text.Json.JsonDocument.Parse(body);
                if (!jsonDoc.RootElement.TryGetProperty("peerAddress", out var peerAddressElement) ||
                    !jsonDoc.RootElement.TryGetProperty("action", out var actionElement))
                {
                    return Results.Text("Missing required properties.", "text/plain", statusCode: 400);
                }

                string peerAddress = peerAddressElement.GetString() ?? string.Empty;
                string actionStr = actionElement.GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(peerAddress) || string.IsNullOrEmpty(actionStr))
                {
                    return Results.Text("Invalid properties.", "text/plain", statusCode: 400);
                }

                // Map the action string to the TaskType enum using a dictionary.
                var taskTypeMap = new Dictionary<string, TaskType>(StringComparer.OrdinalIgnoreCase)
                {
                    { "disconnect", TaskType.DisconnectPeer },
                    { "block", TaskType.BlockAndRemovePeer }
                };

                if (!taskTypeMap.TryGetValue(actionStr, out TaskType taskType))
                {
                    return Results.Text($"Action '{actionStr}' is not supported.", "text/plain", statusCode: 400);
                }

                // Enqueue a network task for each client peer using the TURN service event channel
                foreach (ClientPeer clientPeer in ClientPeers)
                {
                    NetworkTask task = new NetworkTask()
                    {
                        TaskType = taskType,
                        TaskData = new Dictionary<string, string>()
                        {
                            { "TargetPeer", peerAddress }
                        }
                    };
                    TURNService.EnqueueTaskForPeer(clientPeer.Identifier, task);
                }
                return Results.Text($"Peer management task enqueued for action '{actionStr}' on peer '{peerAddress}'.", "text/plain");
            });
            // GET endpoint to return the current peers for the dashboard
            app.MapGet("/api/peers", () =>
            {
                List<GenericPeer> peers = new List<GenericPeer>();
                // Iterate through ClientPeers and project each peer into a simple object.
                foreach (ClientPeer peer in ClientPeers)
                {
                    GenericPeer _peer = new GenericPeer(peer.IP, peer.Identifier, peer.Port);
                    _peer.Address = peer.IP.ToString();
                    peers.Add(_peer);
                }
                // Use DistributionProtocol.Serialize for AOT compliance
                return Results.Text(Serialize<CollectionSharePacket>(new CollectionSharePacket(0, peers.Cast<IPeer>().ToList())), "application/json", statusCode: 200);
            });
            #endregion

            Task.Run(() => { Parser.Initialize(); });
            Task.Run(() => { EncryptionService.Initialize(); });
            Task.Run(() => { InitializeDatabase(); });

            app.Run();
        }

        /// <summary>
        /// Generates server-sent events for a connected peer, streaming any pending network tasks.
        /// </summary>
        /// <param name="peerId">The identifier of the peer.</param>
        /// <param name="clientPeer">The client peer instance.</param>
        /// <param name="cancellationToken">Cancellation token for the stream.</param>
        /// <returns>An async enumerable of SSE-formatted strings.</returns>
        private static async IAsyncEnumerable<string> GetPeerServerSentEvents(
            string peerId,
            ClientPeer clientPeer,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            int keepAliveCounter = 0;
            const int keepAliveInterval = 30; // send keep-alive every 30 iterations (~15 seconds at 500ms delay)

            while (!cancellationToken.IsCancellationRequested)
            {
                // first drain any tasks from the legacy outgoing queue
                while (clientPeer.OutgoingTasks.Count > 0)
                {
                    NetworkTask task = clientPeer.OutgoingTasks.Dequeue();
                    SignOffOnNetworkTask(ref task);
                    string taskJson = Serialize(task);
                    yield return $"data: {taskJson}\n\n";
                    clientPeer.UpdateTimeIn();
                }

                // then check the TURN service event channel for this peer
                var channel = TURNService.GetOrCreatePeerChannel(peerId);
                while (channel.Reader.TryRead(out var task))
                {
                    SignOffOnNetworkTask(ref task);
                    string taskJson = Serialize(task);
                    yield return $"data: {taskJson}\n\n";
                    clientPeer.UpdateTimeIn();
                }

                // send keep-alive periodically to maintain connection
                keepAliveCounter++;
                if (keepAliveCounter >= keepAliveInterval)
                {
                    var keepAlive = new NetworkTask
                    {
                        TaskType = TaskType.StreamKeepAlive,
                        TaskData = new Dictionary<string, string>
                        {
                            { "Timestamp", DateTime.UtcNow.ToString("o") }
                        }
                    };
                    yield return $"data: {Serialize(keepAlive)}\n\n";
                    keepAliveCounter = 0;
                }

                await Task.Delay(500, cancellationToken);
            }
        }

    }
}