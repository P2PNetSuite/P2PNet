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

namespace P2PBootstrap
{
    public class Program
    {
        public static string PublicKeyToString => Encoding.UTF8.GetString(GlobalConfig.ActiveKeys.Public.KeyData);
        public static void Main(string[] args)
        {
            LoggingConfiguration.LoggerStyle = LogStyle.PlainTextFormat;
            LoggingConfiguration.LoggerActive = true;

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

            app.MapPut("/api/Bootstrap/peers", async Task<IResult> (HttpContext context) =>
            {
                try
                {
                    // read the incoming PUT
                    using var reader = new StreamReader(context.Request.Body);
                    var bodyJson = await reader.ReadToEndAsync();

                    // deserialize the input
                    var incomingPacket = Deserialize<DataTransmissionPacket>(bodyJson);

                    // TODO improve logic for handling incoming peer verification
                    // ie Identifier values
                    if(incomingPacket != null)
                    {
                        string IDpacketJSON = Encoding.UTF8.GetString(incomingPacket.Data);
                        IdentifierPacket identifierPacket = JsonSerializer.Deserialize<IdentifierPacket>(IDpacketJSON);
                        IPeer newPeer = new GenericPeer(IPAddress.Parse(identifierPacket.IP), identifierPacket.Data);
                        KnownPeers.Add(newPeer); // add the new peer to the known peers list
                        // we DO NOT use PeerNetwork.AddPeer(...) otherwise a PeerChannel will be created
                    }

                    if (GlobalConfig.TrustPolicy() == TrustPolicies.BootstrapTrustPolicyType.Trustless)
                    {
                        // reply with a CollectionSharePacket
                        var share = new CollectionSharePacket(100, KnownPeers);
                        var responseJson = Serialize(share);
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

                        var outPacket = new DataTransmissionPacket()
                        {
                            DataType = DataPayloadFormat.Task,
                            Data = networkTask.ToByte()
                        };

                        var responseJson = Serialize(outPacket);
                        return Results.Content(responseJson, "application/json");
                    }
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message);
                }
            });

            app.MapPut("/api/Bootstrap/verifyhash", async Task<IResult> (HttpContext context) =>
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

            if(GlobalConfig.ServePublicIP() == true)
            {                
                app.MapGet("/api/Bootstrap/publicip", async (HttpContext context) =>
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

            Task.Run(() => { Parser.Initialize(); });
            Task.Run(() => { EncryptionService.Initialize(); });
            Task.Run(() => { InitializeDatabase(); });

            app.Run();
        }

        private class InputModel
        {
            public string Input { get; set; }
        }

        public static void Test()
        {
            Thread.Sleep(5000);
            // test
            NetworkTask nt = new NetworkTask()
            {
                TaskType = TaskType.BootstrapInitialization,
                TaskData = new Dictionary<string, string>()
                {
                    { "PublicKey", PublicKeyToString },
                    { "Peers", Serialize(new CollectionSharePacket(100, KnownPeers)) }
                }
            };

            SignOffOnNetworkTask(ref nt);
            foreach (KeyValuePair<string, string> kvp in nt.TaskData)
            {
                DebugMessage($"Key: {kvp.Key}, Value: {kvp.Value}", MessageType.Debug);
            }
            MD5 hashing = MD5.Create();
            string _hashone = Convert.ToBase64String(hashing.ComputeHash(nt.ToByte()));
            DebugMessage("Hash out before entry removal: " + _hashone, MessageType.Debug);

            nt.TaskData.Remove("Signature"); // remove signature for verification
            
            string _hash = Convert.ToBase64String(hashing.ComputeHash(nt.ToByte()));
            DebugMessage("Hash out after entry removal: " + _hash, MessageType.Debug);
        }
    }
}
