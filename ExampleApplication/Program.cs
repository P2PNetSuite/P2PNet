using P2PNet;
using P2PNet.Peers;
using P2PNet.NetworkPackets;
using static P2PNet.PeerNetwork;
using P2PNet.Routines;
using System.Diagnostics;

namespace ExampleApplication
{
    internal class Program
    {
        static List<int> targerPorts;
        static bool broadcastPortProvided = false;
        static void Main(string[] args)
        {

            if (args.Length > 0)
            {
                targerPorts = args.ToArray().Select(x => int.Parse(x)).ToList();
                PeerNetwork.DesignatedPorts = targerPorts;
                PeerNetwork.BroadcasterPort = targerPorts[0]; // we'll chain these together
                broadcastPortProvided = true;
            } else if (Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process).Contains("BROADCASTPORT"))
            {
                if (int.TryParse(Environment.GetEnvironmentVariable("BROADCASTPORT"), out int port))
                {
                    Console.WriteLine($"Using environment variable for broadcast port: {port}");
                    PeerNetwork.BroadcasterPort = port;
                    broadcastPortProvided = true;
                }
            }

            PeerNetwork.LoadLocalAddresses();

            PeerNetwork.TrustPolicies.IncomingPeerTrustPolicy.IncomingPeerPlacement = TrustPolicies.IncomingPeerTrustPolicy.IncomingPeerMode.EventBased;
            PeerNetwork.TrustPolicies.IncomingPeerTrustPolicy.RunDefaultTrustProtocol = true;
            PeerNetwork.TrustPolicies.IncomingPeerTrustPolicy.AllowDefaultCommunication = true;

            PeerNetwork.PeerAdded += NewPeerAdded;
            PeerNetwork.IncomingPeerConnectionAttempt += NewPeerChannel;

            PeerNetwork.BeginAcceptingInboundPeers();
            if (broadcastPortProvided == true)
            {
                PeerNetwork.StartBroadcastingLAN(false);
            }
            else
            {
                PeerNetwork.StartBroadcastingLAN(true);
            }

            Console.WriteLine("Hello, p2p world!");
            Console.WriteLine($"Listening port: {ListeningPort}, local IP: {LocalIPV4Address}");
            Console.WriteLine($"Broadcasting on port: {PeerNetwork.BroadcasterPort}");
            Task.Run(() => { WaitForPeers(); });
            while (true)
            {
                Console.ReadLine();
            }
        }

        private static void WaitForPeers() 
        {
            while (true)
            {
                if(TrustedPeerChannels.Count >= 1)
                {
                    PeerNetwork.StopBroadcastingLAN();
                    Console.WriteLine($"Active peer count: {ActivePeerChannels.Count}");
                    Console.WriteLine($"Active peer addresses: {string.Join(", ", ActivePeerChannels.Select(x => x.peer.Address.ToString()))}");

                    foreach (var peer in ActivePeerChannels)
                    {
                        peer.LoadOutgoingData(new PureMessagePacket("Thanks for being my peer!"));
                    }
                    break;
                }
                Thread.Sleep(500);
            }
        }

        private static void NewPeerChannel(object? sender, IncomingPeerEventArgs e)
        {
            Console.WriteLine($"New peer address: {e.Peer.Address.ToString()}");
        }

        private static void NewPeerAdded(object? sender, NewPeerEventArgs e)
        {
            e.peerChannel.LoadOutgoingData(new PureMessagePacket("Hello fellow peer!"));
        }


    }
}
