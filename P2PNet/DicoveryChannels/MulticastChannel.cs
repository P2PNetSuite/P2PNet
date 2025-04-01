using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using P2PNet.DicoveryChannels;

namespace P2PNet.DiscoveryChannels
{
    /// <summary>
    /// Conducts LAN multicasting to broaden network.
    /// </summary>
    internal class MulticastChannel : LANDiscoveryChannelBase
    {
        private readonly int[] MULTICAST_PORTS = { 3000, 3010, 3020, 3030 }; // TODO :: Allow the range of local multicast ports to be modified
        public IPAddress multicast_address;

        public MulticastChannel(IPAddress in_multicast_address) 
        {
            multicast_address = in_multicast_address;
        }

        /// <summary>
        /// Starts up the multicasting.
        /// </summary>
        public async void OpenMulticastChannel()
            {
            cancelBroadcaster = new CancellationTokenSource(); 
            cancelListener = new CancellationTokenSource();

            Task.Run(() => StartBroadcaster(cancelBroadcaster.Token));
            Task.Run(() => StartListener(cancelListener.Token));
            }

        public override async Task StartBroadcaster(CancellationToken cancellationToken)
        {
            int timevariation = CreateTimeVariation(100, 500);
            UdpClient broadcaster = new UdpClient();
            broadcaster.EnableBroadcast = true;
            broadcaster.MulticastLoopback = true;
            broadcasterendpoint = new IPEndPoint(multicast_address, 0);
            while (!cancellationToken.IsCancellationRequested)
            {
                    byte[] message = UniqueIdentifier(); // Turn unique identifier packet to byte[]

                    DebugMessage($"Multicast channel broadcast:  Endpoint-{broadcasterendpoint.Address.ToString()} MCAddress-{multicast_address.Address.ToString()}", PeerNetwork.Logging.LAN);

                    broadcaster.Send(message, message.Length, new IPEndPoint(multicast_address, 0));
                    Thread.Sleep(BroadcastRateControl.GetCurrentInterval());
            }
        }

        public override async Task StartListener(CancellationToken cancellationToken)
        {
            UdpClient listener = new UdpClient();
            listener.JoinMulticastGroup(multicast_address);
            listener.MulticastLoopback = true; listener.EnableBroadcast = true;
            
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, MULTICAST_PORTS[0]);
            listenerendpoint = endpoint;
            listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Client.Bind(endpoint);

            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] receivedData = listener.Receive(ref endpoint);
                string packet = Encoding.UTF8.GetString(receivedData);

                DebugMessage("Packet received from multicast channel!", PeerNetwork.Logging.LAN);

                HandlePacket(packet);   
            }
        }
    }
}
