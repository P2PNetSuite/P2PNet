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
                                    { "Peers", Serialize(new CollectionSharePacket(100, KnownPeers)) }
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
                        return Results.Problem(Serialize<PureMessagePacket>(new PureMessagePacket("Invalid DataTransmissionPacket received.")), statusCode: 400);
                    }

                    // extract the NetworkTask from the DataTransmissionPacket Data field.
                    string ntJson = Encoding.UTF8.GetString(incomingPacket.Data);
                    NetworkTask task = Deserialize<NetworkTask>(ntJson);

                    // verify the task type.
                    if (task.TaskType != TaskType.RequestVerifyHashRecord)
                    {
                        return Results.Problem(Serialize<PureMessagePacket>(new PureMessagePacket("Invalid network task type for this endpoint.")), statusCode: 400);
                    }

                    // check for the 'Hash' key.
                    if (!task.TaskData.ContainsKey("Hash"))
                    {
                        return Results.Problem(Serialize<PureMessagePacket>(new PureMessagePacket("Missing 'Hash' key in TaskData.")), statusCode: 400);
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

            app.MapPut(DistributionProtocol.BootstrapServerAPIendpoints[CommonBootstrapEndpoints.Heartbeat], async Task<IResult> (HttpContext context) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                string bodyJson = await reader.ReadToEndAsync();
                var incomingPacket = Deserialize<DataTransmissionPacket>(bodyJson);
                if (incomingPacket == null || incomingPacket.Data == null)
                {
                    return Results.Content(Serialize<PureMessagePacket>(new PureMessagePacket("Invalid heartbeat packet received.")), "application/json");
                }

                // find the respective ClientPeer using the SourceOriginIdentifier
                if (!ClientPeers.TryGetValue(incomingPacket.SourceOriginIdentifier, out ClientPeer clientPeer))
                {
                    return Results.Content(Serialize<PureMessagePacket>(new PureMessagePacket($"ClientPeer with SourceOriginIdentifier '{incomingPacket.SourceOriginIdentifier}' not found.")), "application/json");
                }

                string ntJson = Encoding.UTF8.GetString(UnwrapData(incomingPacket));
                NetworkTask heartbeatTask = Deserialize<NetworkTask>(ntJson);
                if (heartbeatTask == null || heartbeatTask.TaskType != TaskType.Heartbeat)
                {
                    return Results.Content(Serialize<PureMessagePacket>(new PureMessagePacket("Invalid heartbeat network task received.")), "application/json");
                }

                // update the client's last incoming time
                clientPeer.UpdateTimeIn();

                // outgoing tasks from the client
                Dictionary<string, string> collectedTasks = new Dictionary<string, string>();
                int taskCounter = 0;
                while (clientPeer.OutgoingTasks.Count > 0)
                {
                    NetworkTask task = clientPeer.OutgoingTasks.Dequeue();
                    SignOffOnNetworkTask(ref task);
                    string taskSerialized = Serialize(task);
                    collectedTasks.Add($"Task_{taskCounter++}", taskSerialized);
                }

                // new heartbeatresponse network task containing the collected tasks
                NetworkTask heartbeatResponseTask = new NetworkTask()
                {
                    TaskType = TaskType.HeartbeatResponse,
                    TaskData = collectedTasks
                };
                SignOffOnNetworkTask(ref heartbeatResponseTask);

                // wrap the heartbeat response network task, inside a DataTransmissionPacket
                DataTransmissionPacket responsePacket = new DataTransmissionPacket(heartbeatResponseTask);
                string responseJson = responsePacket.ToJsonString();
                return Results.Content(responseJson, "application/json");
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
                return Results.Ok();
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

                // Enqueue a network task for each client peer with the provided information
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
                    clientPeer.OutgoingTasks.Enqueue(task);
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
                // Use explicit JSON options to avoid source-generation metadata issues
                var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                return Results.Json(peers, options);
            });
            #endregion

            Task.Run(() => { Parser.Initialize(); });
            Task.Run(() => { EncryptionService.Initialize(); });
            Task.Run(() => { InitializeDatabase(); });

            app.Run();
        }


    }
}