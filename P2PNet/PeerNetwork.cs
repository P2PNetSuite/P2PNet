global using P2PNet.Exceptions;
global using static ConsoleDebugger.ConsoleDebugger;
using P2PNet.DicoveryChannels.WAN;
using P2PNet.DiscoveryChannels;
using P2PNet.NetworkPackets;
using P2PNet.Peers;
using P2PNet.Routines;
using SharpPcap;
using SharpPcap.LibPcap;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace P2PNet
{
    public static class PeerNetwork
    {

        // Some non-public facing settings
        static bool isBroadcaster = false;
        private static Random randomizer = new Random();
        private static bool localAddressesLoaded = false;
        private static bool runningLANdiscovery = false;

        public static NetworkRoutines<string, IRoutine> P2PNetworkRoutines { get; set; } = new NetworkRoutines<string, IRoutine>();

        /// <summary>
        /// Indicates whether to automatically throttle outbound broadcast rate when a new peer is discovered.
        /// </summary>
        public static bool AutoThrottleOnConnect { get; set; } = true;

        /// <summary>
        /// Gets or sets the list of designated ports for broadcast and discovery.
        /// </summary>
        public static List<int> DesignatedPorts { get; set; } = new List<int> { 8001, 8002, 8003 }; // Your designated ports

        /// <summary>
        /// Gets or sets the broadcaster port designated for outbound LAN discovery.
        /// </summary>
        public static int BroadcasterPort { get; set; } = 0;

        /// <summary>
        /// Gets the local IPv4 IP address.
        /// </summary>
        public static IPAddress LocalIPV4Address { get; private set; } = null;

        /// <summary>
        /// Gets the public IPv4 IP address.
        /// </summary>
        public static IPAddress PublicIPV4Address { get; private set; } = null;

        /// <summary>
        /// Gets the public IPv6 IP address.
        /// </summary>
        public static IPAddress PublicIPV6Address;
        private static bool IPv6AddressFound = false;

        /// <summary>
        /// Gets or sets the identifier for the peer member in the P2P network.
        /// The ability to set or change the identifier is governed by the <see cref="TrustPolicies.PeerNetworkTrustPolicy.LocalIdentifierSetPolicy"/>.
        /// </summary>
        /// <exception cref="UnauthorizedAccessException">
        /// Thrown if an attempt is made to change the identifier when the policy is set to <see cref="TrustPolicies.LocalIdentifierSetPolicyTypes.StrictLocalOnly"/> or <see cref="TrustPolicies.LocalIdentifierSetPolicyTypes.StrictRemoteOnly"/>
        /// and the identifier has already been set.
        /// </exception>
        public static string Identifier
        {
            get { return _identifier; }
            set 
            { 
                if((TrustPolicies.PeerNetworkTrustPolicy.LocalIdentifierSetPolicy != TrustPolicies.LocalIdentifierSetPolicyTypes.StrictLocalOnly) && (TrustPolicies.PeerNetworkTrustPolicy.LocalIdentifierSetPolicy != TrustPolicies.LocalIdentifierSetPolicyTypes.StrictRemoteOnly))
                {
                    // checking if origin (local or remote) match policy should occur upstream from here
                    // these checks only occur here due to security
                    _identifier = value;
                } else
                {
                    if (_identifierSet == false)
                    {
                        _identifier = value;
                        _identifierSet = true;
                    }
                    else
                    {
                        throw new UnauthorizedAccessException("The identifier value is set and locked, therefore cannot be changed.");
                    }
                }
            }
        }
        private static string _identifier = string.Empty;
        private static bool _identifierSet = false;

        /// <summary>
        /// Gets the system MAC address.
        /// </summary>
        public static PhysicalAddress MAC;

        private static TcpListener listener;

        /// <summary>
        /// Gets or sets whether a designated TCP port will be actively listening for and accepting inbound TCP peer connections.
        /// Setting this value will automatically start or stop acceptance of inbound peers.
        /// The default is true, but you may want to toggle this for server instances that serve different network purposes.
        /// </summary>
        public static bool AcceptInboundPeers
        {
            get
            {
                return runningListener;
            }
            set
            {
                runningListener = value;
                SetInboundPeerAcceptance(value);
            }
        }
        private static bool runningListener = true;
        private static void SetInboundPeerAcceptance(bool status_)
        {
            if (status_ == true)
            {
                BeginAcceptingInboundPeers();
            }
            else
            {
                StopAcceptingInboundPeers();
            }
        }

        /// <summary>
        /// Begins accepting inbound peers connections.
        /// </summary>
        public static void BeginAcceptingInboundPeers()
        {
            try
            {               
                if (AcceptInboundPeers == true)
                {                  
                    listener.Start();
                    Task.Run(() => AcceptClientsAsync());
                }
                else
                {
                    DebugMessage("Attempt was made to start inbound listener while AcceptInboundPeers is false.", MessageType.Warning);
                }
            }
            catch (Exception ex)
            {
                DebugMessage($"{ex.StackTrace} {ex.Message}", MessageType.Critical);
            }
        }

        /// <summary>
        /// Stops accepting inbound peer connections.
        /// </summary>
        public static void StopAcceptingInboundPeers()
        {
            listener.Stop();
            if (AcceptInboundPeers == true)
            {
                AcceptInboundPeers = false; // redundant but never too careful
            }
        }

        /// <summary>
        /// Gets the listening port for inbound TCP peer connections.
        /// </summary>
        public static int ListeningPort { get; private set; }


        #region Connection Collections
        private static volatile List<IPeer> activePeers_ = new List<IPeer>();

        /// <summary>
        /// Gets the list of trusted peer channels within the <see cref="PeerNetwork.ActivePeerChannels"/>
        /// </summary>
        public static List<PeerChannel> TrustedPeerChannels
        {
            get { return ActivePeerChannels.Where(pc => pc.IsTrustedPeer).ToList(); }
        }

        /// <summary>
        /// Gets or sets the list of known peers.
        /// </summary>
        /// <remarks>
        /// KnownPeers do not necessarily have established trust to exchange extensive data and information, but do have an open PeerChannel in the <see cref="PeerNetwork.ActivePeerChannels"/>.
        /// This is mostly to store and manage known peers for future reference and potential connection.
        /// </remarks>
        public static List<IPeer> KnownPeers
        {
            get
            {
                return activePeers_;
            }
            set
            {
                activePeers_ = value;
            }
        }

        /// <summary>
        /// Occurs when a new peer is added and passes the received data from the subsequent PeerChannel in an event.
        /// Subscribers can use this event to handle all new peers and their peer channels, regardless of point of origin.
        /// </summary>
        /// <example>
        /// <code>
        ///    // Event is raised when a new known peer is discovered, regardless of point of origin
        ///    private static void HandleNewKnownPeer(object sender, PeerNetwork.NewPeerEventArgs e)
        ///    {
        ///        // The peer channel's DataReceived event subscribed to HandleIncomingData function
        ///        e.peerChannel.DataReceived += HandleIncomingData;
        ///    }
        ///    
        ///    private static void HandleIncomingData(object? sender, PeerChannelBase.DataReceivedEventArgs e)
        ///    {
        ///        Console.WriteLine(e.Data); // incoming information is printed to console
        ///    }
        /// 
        /// </code>
        /// </example>
        public static event EventHandler<NewPeerEventArgs> PeerAdded;

        // Define the delegate for the event
        public delegate void NewKnownPeerEventHandler(object sender, NewPeerEventArgs e);

        // Define the event arguments class
        public class NewPeerEventArgs : EventArgs
        {
            public PeerChannel peerChannel { get; }

            public NewPeerEventArgs(PeerChannel PeerChannel)
            {
                peerChannel = PeerChannel;
            }
        }
        // Protected method to raise the event
        private static void OnPeerAdded(PeerChannel peerChannel)
        {
            PeerAdded?.Invoke(null, new NewPeerEventArgs(peerChannel));
        }

        /// <summary>
        /// Queue of inbound connecting peers that have not been assigned a peer channel.
        /// </summary>
        /// <remarks>
        /// This will only become populated if the <see cref="PeerNetwork.TrustPolicies.IncomingPeerTrustPolicy.IncomingPeerPlacement"/> value includes queue-based placement.
        /// This allows for additional verification and handling of incoming peers before they are assigned a peer channel.
        /// For idle action, consider using <see cref="PeerNetwork.TrustPolicies.IncomingPeerTrustPolicy.IncomingPeerMode.EventBased"/> and ignoring the event to prevent excessive memory usage.
        /// </remarks>
        public static InboundConnectingPeersQueue InboundConnectingPeers = new InboundConnectingPeersQueue();

        /// <summary>
        /// Occurs when a new incoming peer connection attempt is detected.
        /// Subscribers can use this event to handle new connections and pass incoming connections through your verification pipeline and/or <see cref="AddPeer(IPeer, TcpClient)"/>.
        /// </summary>
        public static event EventHandler<IncomingPeerEventArgs> IncomingPeerConnectionAttempt
        {
            add { InboundConnectingPeers.IncomingPeerConnectionAttempt += value; }
            remove { InboundConnectingPeers.IncomingPeerConnectionAttempt -= value; }
        }

        /// <summary>
        /// Gets the number of inbound peers that have been enqueued but not yet processed.
        /// </summary>
        public static int InboundPeerCount => InboundConnectingPeers.Count;

        /// <summary>
        /// Gets the first peer from inbound peer queue.
        /// </summary>
        public static GenericPeer DequeueInboundPeer()
        {
            return InboundConnectingPeers.Dequeue();
        }

        /// <summary>
        /// All active <see cref="PeerChannel"/> connections are stored here. 
        /// </summary>
        public static volatile List<PeerChannel> ActivePeerChannels = new List<PeerChannel>();


        private static List<LocalChannel> ActiveLocalChannels = new List<LocalChannel>();
        private static List<MulticastChannel> ActiveMulticastChannels = new List<MulticastChannel>();
        private static List<BootstrapChannel> ActiveBootstrapChannel = new List<BootstrapChannel>();

        private static List<BootstrapChannel> inactiveBootstrapChannel = new List<BootstrapChannel>();
        private static List<IPAddress> multicast_addresses = new List<IPAddress>();

        #endregion


        /// <summary>
        /// Throttles the broadcasting down.
        /// </summary>
        /// <remarks>This greatly reduces the speed at which packets are sent out. This may be useful if you want to lessen the computational load while handling newly found connections.</remarks>
        public static void ThrottleBroadcastingDown()
        {
            foreach (MulticastChannel multicastChannel in ActiveMulticastChannels)
            {
                multicastChannel.DownThrottle();
            }
            foreach (LocalChannel localChannel in ActiveLocalChannels)
            {
                localChannel.DownThrottle();
            }
        }

        static PeerNetwork()
        {
            ListeningPort = randomizer.Next(8051, 9000); // setup a port for listening
            listener = new TcpListener(IPAddress.Any, ListeningPort);

            P2PNetworkRoutines.InitializeRoutines();
        }

        static async Task AcceptClientsAsync()
        {
            while (AcceptInboundPeers == true)
            {
                TcpClient client = await listener.AcceptTcpClientAsync();

                Task.Run(() => handleInboundConnection(client));
                Thread.Sleep(75);
            }
        }
        static void handleInboundConnection(TcpClient client)
        {
            IPAddress peerIP = ((IPEndPoint)client.Client.RemoteEndPoint).Address;

            // Immediately filter out blocked IPs
            if (PeerNetwork.TrustPolicies.IncomingPeerTrustPolicy.BlockedIPs.Contains(peerIP))
            {
                DebugMessage($"Blocked IP attempted to connect: {peerIP.ToString()}. Ignoring.", MessageType.Warning);
                client.Dispose();
                return;
            }
            // Check for existing peer
            if (KnownPeers.Any(p => p.IP.Equals(peerIP)))
            {
                //    DebugMessage("Duplicate connection attempt from existing peer. Ignoring.", MessageType.Warning);
                client.Dispose();
            }
            else if (InboundConnectingPeers.PeerIsQueued(peerIP.ToString()))
            {
                //    DebugMessage("Duplicate connection attempt from existing peer. Ignoring.", MessageType.Warning);
            }
            else
            {
                // Set inbound as GenericPeer
                GenericPeer newPeer = new GenericPeer(((IPEndPoint)client.Client.RemoteEndPoint).Address, ((IPEndPoint)client.Client.RemoteEndPoint).Port);
                // pass peer reference to be processed by queue or event driven logic (application defined)
                InboundConnectingPeers.Enqueue(newPeer);
                // pass peer reference to be added to KnownPeers and ActivePeerChannels (lib defined)
                Task.Run(() => AddPeer(newPeer, client));
            }
        }

        /// <summary>
        /// Adds a peer to the <see cref="KnownPeers"/> list and establishes a connection if one is not provided.
        /// A new peer channel will be automatically added to <see cref="ActivePeerChannels"/> with standard non-elevated permissions.
        /// </summary>
        /// <param name="peer">The peer to add.</param>
        /// <param name="client">The TCP client associated with the peer. Default is null.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <remarks></remarks>
        private static readonly object _peersLock = new object();

        public static async Task AddPeer(IPeer peer, TcpClient client = null)
        {
            // Lock the known peers check
            lock (_peersLock)
            {
                if ((KnownPeers.Any(p => p.IP.Equals(peer.IP))) ||
                    (KnownPeers.Any(p => p.Identifier.Equals(peer.Identifier))))
                {
                    //    DebugMessage("Duplicate connection attempt from existing peer. Ignoring.", MessageType.Warning);
                    return;
                }
                else if (peer.IP.ToString() == LocalIPV4Address.ToString() && peer.Port == ListeningPort)
                {

                    return;
                }
            }
            if (client == null)
            {
                try
                {
                    IPEndPoint endpoint = new IPEndPoint(peer.IP, peer.Port);
                    peer.Client = new TcpClient(peer.IP.ToString(), peer.Port);
                    if (!peer.Client.Connected)
                    {
                        peer.Client.Connect(peer.IP, peer.Port);
                    }
                    peer.Stream = peer.Client.GetStream();
                    await channelize();
                }
                catch (Exception e)
                {
                    DebugMessage($"Issue opening trusted peer channel: {peer.IP} @ port {peer.Port}\n{e}", MessageType.Critical);
                    return;
                }
            }
            else // we're accepting an incoming client
            {
                try
                {
                    peer.Client = client;
                    peer.Stream = peer.Client.GetStream();
                    await channelize();
                }
                catch (Exception e)
                {
                    DebugMessage($"Issue accepting incoming peer: {peer.IP} @ port {peer.Port}", MessageType.Warning);
                    return;
                }
            }

            async Task channelize()
            {
                PeerChannel peerChannel = new PeerChannel(peer);
                lock (_peersLock)
                {
                    ActivePeerChannels.Add(peerChannel);
                    KnownPeers.Add(peerChannel.peer);
                }

                if (TrustPolicies.IncomingPeerTrustPolicy.AllowEnhancedPacketExchange == true)
                {
                    await ElevatePeerPermission(peerChannel);
                }

                Thread peerThread = new Thread(peerChannel.OpenPeerChannel);
                peerThread.Start();

                // Raise  PeerAdded event
                Task.Run(() => OnPeerAdded(peerChannel));
            }
        }
        /// <summary>
        /// Terminates a peer connection and removes it from <see cref="PeerNetwork.KnownPeers"/> and <see cref="PeerNetwork.ActivePeerChannels"/>.
        /// </summary>
        /// <param name="channel">The peer channel to remove.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a boolean indicating success or failure.</returns>
        public static async Task<bool> RemovePeer(PeerChannel channel)
        {
            bool flawless = true; // This is here for more intricate scenarios later
            try
            {

                channel.ClosePeerChannel();
                ActivePeerChannels.Remove(channel);
            }
            catch (Exception ex)
            {

                DebugMessage($"Error removing peer: {ex.Message}", MessageType.Warning);

                return false;
            }
            return flawless;
        }

        /// <summary>
        /// Elevates a peer's permissions and adjusts broadcasting if necessary.
        /// </summary>
        /// <param name="channel">The peer channel to elevate.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a boolean indicating success or failure.</returns>
        public static async Task<bool> ElevatePeerPermission(PeerChannel channel)
        {
            bool flawless = true;
            try
            {
                channel.TrustPeer();

                if (AutoThrottleOnConnect == true)
                {
                    ThrottleBroadcastingDown();
                }
            }
            catch (Exception ex)
            {
                // Log the exception for debugging 

                DebugMessage($"Error elevating peer status: {ex.Message}", MessageType.Warning);

                return false;
            }
            return flawless;
        }

        /// <summary>
        /// Processes and adds peers from a collection share packet to the known peers list.
        /// </summary>
        /// <param name="packet">The collection share packet containing the list of peers.</param>
        public static void ProcessPeerList(CollectionSharePacket packet)
        {
            int x = 0;
            foreach (IPeer newpeer in packet.peers)
            {
                if (!KnownPeers.Contains(newpeer))
                {
                    KnownPeers.Add(newpeer);
                    x++;
                }
            }
            if (x > 0)
            {
                DebugMessage($"Added {x} peers.");
            }
        }

        #region Bootup LAN

        /// <summary>
        /// Scans all network interface devices and collects essential information needed for peer network.
        /// </summary>
        public static void LoadLocalAddresses()
        {
            localAddressesLoaded = true; // tell the application that we have loaded the local information
            // get public ipv6 address
            PublicIPV6Address = GetLocalIPv6Address();
            if (PublicIPV6Address != null)
            {
                IPv6AddressFound = true; // this will signal to us later if IPv6 is usable or not
                DebugMessage($"IPv6 IP address: {PublicIPV6Address.ToString()}");
            }
            else
            {

                DebugMessage("IPv6 IP address not found. IPv6 features will not be available.", MessageType.Warning);
            }
            // get public ipv4 address
            PublicIPV4Address = GetPublicIPv4Address();
            if (PublicIPV4Address != null)
            {
                DebugMessage($"Public-facing IPv4 address: {PublicIPV4Address}");
            }
            else
            {
                DebugMessage("Failed to fetch public-facing IPv4 address.", MessageType.Warning);
            }

            // Get the first available network interface
            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            var devices = CaptureDeviceList.Instance; // loads active interfaces


            var relevantInterfaces = networkInterfaces
           .Where(adapter =>
               adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
               adapter.SupportsMulticast);

            NetworkInterface primaryInterface = null;
            foreach (NetworkInterface adapter in relevantInterfaces)
            {
                IPInterfaceProperties adapterProperties = adapter.GetIPProperties();
                // Check if the interface has a non-empty default gateway
                if (adapterProperties.GatewayAddresses.Any())
                {
                    primaryInterface = adapter;
                    break; // Found the primary interface
                }
            }

            if (primaryInterface != null)
            {
                IPInterfaceProperties adapterProperties = primaryInterface.GetIPProperties();

                var ipv4Addresses = adapterProperties.UnicastAddresses
                .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                .Select(addr => addr.Address);

                foreach (IPAddress ip in ipv4Addresses)
                {
                    LocalIPV4Address = ip; // grab public IP, typically the last/only one is true
                    MAC = primaryInterface.GetPhysicalAddress();
                }
            }
            else
            {

                DebugMessage("No primary interface found.");
                Thread.Sleep(1500);
                throw new IOException("No primary network interface found. Please check your network settings.");
            }


            foreach (NetworkInterface adapter in networkInterfaces)
            {
                foreach (var dev in devices)
                {
                    if ((dev is LibPcapLiveDevice libPcapDevice) && (dev.Description.Equals(adapter.Description, StringComparison.OrdinalIgnoreCase)))
                    {

                        IPInterfaceProperties adapterProperties = adapter.GetIPProperties();
                        GatewayIPAddressInformationCollection addresses = adapterProperties.GatewayAddresses;

                        DebugMessage(adapter.GetPhysicalAddress().ToString());
                        DebugMessage($"{adapter.Name.ToString()}\t {adapter.NetworkInterfaceType.ToString()}");
                        DebugMessage($"Multicast supported: {adapter.SupportsMulticast}");

                        foreach (MulticastIPAddressInformation address in adapterProperties.MulticastAddresses)
                        {

                            DebugMessage($"Multicast address: {address.Address.ToString()}");

                            multicast_addresses.Add(address.Address);
                        }
                    }
                }
            }
            // Ensure there's no duplicates
            multicast_addresses = multicast_addresses.Distinct().ToList();
        }
        private static IPAddress GetPublicIPv4Address()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string ipString = client.GetStringAsync("https://api.ipify.org").Result;
                    if (IPAddress.TryParse(ipString, out IPAddress publicIP))
                    {
                        return publicIP;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugMessage($"Error fetching public IP address: {ex.Message}", MessageType.Warning);
            }
            return null;
        }


        /// <summary>
        /// Gets the IPv6 address of the host machine.
        /// </summary>
        /// <returns>The non-temporary IPv6 address of the host machine.</returns>
        private static IPAddress GetLocalIPv6Address()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                {
                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                            !ip.Address.IsIPv6LinkLocal &&
                            !ip.Address.IsIPv6Multicast &&
                            !ip.Address.IsIPv6SiteLocal &&
                            !ip.DuplicateAddressDetectionState.HasFlag(DuplicateAddressDetectionState.Deprecated))
                        {
                            return ip.Address;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Begin LAN broadcast and discovery.
        /// </summary>
        public static void StartBroadcastingLAN()
        {
            try
            {
                CheckIfProperInit(); // make sure local sys info loaded proper

                RandomizeBroadcasterPort(); // randomize a broadcast port

                foreach (int port in DesignatedPorts)
                {
                    LocalChannel localChannel = new LocalChannel(port);
                    ActiveLocalChannels.Add(localChannel);
                }
                InitiateLocalChannels(BroadcasterPort);

                foreach (IPAddress multicaster in multicast_addresses)
                {
                    MulticastChannel multicastChannel = new MulticastChannel(multicaster);
                    ActiveMulticastChannels.Add(multicastChannel);
                }
                InitializeMulticastChannels();

                runningLANdiscovery = true; // toggle flag
            }
            catch (Exception e)
            {

                DebugMessage($"{e.StackTrace} {e.Message}", MessageType.Warning);

            }
        }
        /// <summary>
        /// Begin LAN broadcast and discovery.
        /// </summary>
        /// <param name="initialRandomization">By default, the <see cref="PeerNetwork.BroadcasterPort"/> is randomized from available ports. Use this to override or manually set whether random selection occurs. Useful in scenarios when other logic determines the broadcaster port.</param>
        public static void StartBroadcastingLAN(bool initialRandomization = false)
        {
            try
            {
                CheckIfProperInit(); // make sure local sys info loaded proper

                if(initialRandomization == true)
                {
                    RandomizeBroadcasterPort(); // randomize a broadcast port
                }

                foreach (int port in DesignatedPorts)
                {
                    LocalChannel localChannel = new LocalChannel(port);
                    ActiveLocalChannels.Add(localChannel);
                }
                InitiateLocalChannels(BroadcasterPort);

                foreach (IPAddress multicaster in multicast_addresses)
                {
                    MulticastChannel multicastChannel = new MulticastChannel(multicaster);
                    ActiveMulticastChannels.Add(multicastChannel);
                }
                InitializeMulticastChannels();

                runningLANdiscovery = true; // toggle flag
            }
            catch (Exception e)
            {

                DebugMessage($"{e.StackTrace} {e.Message}", MessageType.Warning);

            }
        }

        /// <summary>
        /// Stops all LAN broadcast.
        /// </summary>
        public static void StopBroadcastingLAN()
        {
            if (runningLANdiscovery == true)
            {
                foreach (LocalChannel localChannel in ActiveLocalChannels)
                {
                    localChannel.cancelBroadcaster.Cancel();
                    localChannel.cancelListener.Cancel();
                }
                foreach (MulticastChannel multicastChannel in ActiveMulticastChannels)
                {
                    multicastChannel.cancelBroadcaster.Cancel();
                    multicastChannel.cancelListener.Cancel();
                }
                runningLANdiscovery = false; // toggle flag
            }
        }

        private static void InitiateLocalChannels(int designated_broadcast_port)
        {
            Queue<LocalChannel> badChannels = new Queue<LocalChannel>();
            bool error_occurred = false;
            foreach (LocalChannel channel_ in ActiveLocalChannels)
            {
                try
                {
                    channel_.OpenLocalChannel();
                    DebugMessage($"\tSetup local channel on port: {channel_.DESIGNATED_PORT}");

                }
                catch
                {
                    DebugMessage($"\tCannot setup local broadcast channel.", MessageType.Warning);
                }
            }
            while (badChannels.Count > 0)
            {
                ActiveLocalChannels.Remove(badChannels.Dequeue());
            }
        }
        private static void InitializeMulticastChannels()
        {
            Queue<MulticastChannel> badChannels = new Queue<MulticastChannel>();
            foreach (MulticastChannel multicastChannel in ActiveMulticastChannels)
            {
                bool error_occurred = false;
                try
                {
                    multicastChannel.OpenMulticastChannel();
                }
                catch
                {
                    error_occurred = true;
                    DebugMessage($"\tCannot setup multi-cast channel on address: {multicastChannel.multicast_address.ToString()}", MessageType.Warning);
                    badChannels.Enqueue(multicastChannel);
                }
                finally
                {
                    if (error_occurred == false)
                    {
                        DebugMessage($"\tSetup multi-cast channel on address: {multicastChannel.multicast_address.ToString()}");
                    }
                }
            }
            while (badChannels.Count > 0)
            {
                ActiveMulticastChannels.Remove(badChannels.Dequeue());
            }
        }

        // Randomly selects a port from the designated port collection to focus on outbound broadcasting
        static void RandomizeBroadcasterPort()
        {
            BroadcasterPort = DesignatedPorts[randomizer.Next(DesignatedPorts.Count)];

            Console.WriteLine("Role: {0}, Port: {1}", isBroadcaster ? "Broadcaster" : "Listener", BroadcasterPort);
        }

        #endregion

        #region Bootup Bootstrap

        /// <summary>
        /// Adds the provided <see cref="BootstrapChannel"/> instance to the active bootstrap channels collection.
        /// </summary>
        /// <param name="bootstrapChannel">An instance of <see cref="BootstrapChannel"/> to be added.</param>
        public static void AddBootstrapChannel(BootstrapChannel bootstrapChannel)
        {
            ActiveBootstrapChannel.Add(bootstrapChannel);
        }
        /// <summary>
        /// Creates a new <see cref="BootstrapChannel"/> from the specified connection options and adds it to the active bootstrap channels collection.
        /// </summary>
        /// <param name="bootstrapChannel">
        /// An instance of <see cref="BootstrapChannelConnectionOptions"/> containing the connection settings. 
        /// The endpoint is mandatory while the authority mode and bootstrap peer values are optional.
        /// </param>
        public static void AddBootstrapChannel(BootstrapChannelConnectionOptions bootstrapChannel)
        {
            BootstrapChannel bChannel = new BootstrapChannel(bootstrapChannel);
            ActiveBootstrapChannel.Add(bChannel);
        }
        /// <summary>
        /// Creates a new <see cref="BootstrapChannel"/> using the specified endpoint address and trust policy,
        /// then adds it to the active bootstrap channels collection.
        /// </summary>
        /// <param name="address">The endpoint URL or address of the bootstrap server.</param>
        /// <param name="trustPolicy">
        /// A value of <see cref="BootstrapTrustPolicyType"/> that specifies whether the channel should be created in authority or trustless mode.
        /// </param>
        public static void AddBootstrapChannel(string address, TrustPolicies.BootstrapTrustPolicyType trustPolicy)
        {
            BootstrapChannelConnectionOptions bootstrapChannel = new BootstrapChannelConnectionOptions(
                address,
                trustPolicy == TrustPolicies.BootstrapTrustPolicyType.Authority ? true : false);
            BootstrapChannel bChannel = new BootstrapChannel(bootstrapChannel);
            ActiveBootstrapChannel.Add(bChannel);
        }

        /// <summary>
        /// Begin loading bootstrap connections.
        /// </summary>
        public static void StartBootstrapConnections()
        {
            try
            {
                CheckIfProperInit(); // make sure loaded proper

                // todo

            } catch(Exception e)
            {
                DebugMessage($"{e.StackTrace} {e.Message}", MessageType.Warning);
            }

            foreach (BootstrapChannel bChannel in inactiveBootstrapChannel)
            {
                // todo: add logic to start bootstrap connections
            }
        }

        #endregion

        // Check if local address info init or not
        private static void CheckIfProperInit()
        {
            if (localAddressesLoaded != true)
            {
                throw new Exception("Local system information unavailable. Make sure to initiate with LoadLocalAddresses() before beginning peer network actions.");
            }
        }

        #region Trust Policies
        public static class TrustPolicies
        {
            /// <summary>
            /// Handles trust and permissions in regards to incoming peer connections.
            /// </summary>
            public static class IncomingPeerTrustPolicy
            {
                /// <summary>
                /// Values for <see cref="IncomingPeerPlacement"/>
                /// <list type="bullet">
                /// <item>
                /// <term>QueueBased</term>
                /// <description>The inbound peer will be directed to the inbound peer queue.</description>
                /// </item>
                /// <item>
                /// <term>EventBased</term>
                /// <description>An event is triggered and the peer is passed to the event args.</description>
                /// </item>
                /// <item>
                /// <term>QueueAndEventBased</term>
                /// <description>The peer is directed to the inbound peer queue, and an event is triggered where the peer is passed to the event args.</description>
                /// </item>
                /// </list>
                /// </summary>
                public enum IncomingPeerMode
                {
                    QueueBased,
                    EventBased,
                    QueueAndEventBased
                }
                private static bool _allowDefaultCommunication = true;
                private static bool _allowEnhancedPacketExchange = false;
                private static bool _runDefaultTrustProtocol = true;
                private static IncomingPeerMode _howToHandleInboundPeed = IncomingPeerMode.EventBased;
                private static List<IPAddress> _blockedIPs = new List<IPAddress>();
                private static List<string> _blockedIdentifiers = new List<string>();

                /// <summary>
                /// Gets or sets whether incoming peer connections will be trusted to establish initial communication by default.
                /// </summary>
                /// <remarks>This determines communicability of <see cref="PureMessagePacket"/> and <see cref="DisconnectPacket"/> through the PeerChannel.</remarks>
                public static bool AllowDefaultCommunication
                {
                    get => _allowDefaultCommunication;
                    set => _allowDefaultCommunication = value;
                }

                /// <summary>
                /// Gets or sets whether incoming peer connections will be trusted to exchange all other packet types, like
                /// data transmission packets, before being trusted peers.
                /// </summary>
                public static bool AllowEnhancedPacketExchange
                {
                    get => _allowEnhancedPacketExchange;
                    set => _allowEnhancedPacketExchange = value;
                }

                /// <summary>
                /// Gets or sets the list of blocked IP addresses.
                /// </summary>
                /// <remarks>This will prevent peers with the specified IP addresses from connecting.</remarks>
                public static List<IPAddress> BlockedIPs
                {
                    get => _blockedIPs;
                    set => _blockedIPs = value;
                }
                /// <summary>
                /// Gets or sets the list of blocked peer identifiers.
                /// </summary>
                /// <remarks>This is useful in authority mode or when peer identifiers are managed to be unique to machine/IP address.</remarks>
                public static List<string> BlockedIdentifiers
                {
                    get => _blockedIdentifiers;
                    set => _blockedIdentifiers = value;
                }

                /// <summary>
                /// Gets or sets the logic for handling inbound peers.
                /// <list type="bullet">
                /// <item>
                /// <term>QueueBased</term>
                /// <description>The inbound peer will be directed to the inbound peer queue.</description>
                /// </item>
                /// <item>
                /// <term>EventBased</term>
                /// <description>An event is triggered and the peer is passed to the event args.</description>
                /// </item>
                /// <item>
                /// <term>QueueAndEventBased</term>
                /// <description>The peer is directed to the inbound peer queue, and an event is triggered where the peer is passed to the event args.</description>
                /// </item>
                /// </list>
                /// </summary>
                public static IncomingPeerMode IncomingPeerPlacement
                {
                    get => _howToHandleInboundPeed;
                    set => _howToHandleInboundPeed = value;
                }

                /// <summary>
                /// Gets or sets whether the default trust protocol will be run when a new peer channel is opened.
                /// </summary>
                /// <remarks>The peer channel will invoke <see cref="PeerNetwork.TrustPolicies.IncomingPeerTrustPolicy.DefaultTrustProtocol"/> Action delegate and pass a reference to itself.</remarks>
                public static bool RunDefaultTrustProtocol
                {
                    get => _runDefaultTrustProtocol;
                    set => _runDefaultTrustProtocol = value;
                }
                public static Action<PeerChannel> DefaultTrustProtocol { get; set; } = DefaultPingHandler;
                private static async void DefaultPingHandler(PeerChannel peerChannel)
                {
                    DebugMessage("Default trust protocol invoked.", ConsoleColor.Cyan);
                    int successfulPings = 0;
                    const int requiredPings = 3;

                    EventHandler<PeerChannelBase.DataReceivedEventArgs> dataReceivedHandler = null;
                    EventHandler<PeerChannelBase.DataReceivedEventArgs> postTestDataReceivedHandler = null;

                    // trust established, but peer is still waiting for pings
                    postTestDataReceivedHandler = (sender, e) =>
                    {
                        DebugMessage(e.Data.ToString(), ConsoleColor.Cyan);
                        if (e.Data.Contains("Ping from"))
                        {
                            // we have established trust, but peer is still waiting for pings
                            PureMessagePacket pingMessage = new PureMessagePacket
                            {
                                Message = $"Ping from {PeerNetwork.LocalIPV4Address}"
                            };
                            string outgoing = Serialize(pingMessage);
                            WrapPacket(PacketType.PureMessage, ref outgoing);
                            peerChannel.LoadOutgoingData(outgoing);
                        }
                    };

                    // peer is not yet trusted, we are still waiting for pings
                    dataReceivedHandler = (sender, e) =>
                    {
                        DebugMessage(e.Data.ToString(), ConsoleColor.Cyan);
                        if (e.Data.Contains("Ping from"))
                        {
                            successfulPings++;
                            if ((successfulPings >= requiredPings) && (successfulPings < 6))
                            {
                                peerChannel.TrustPeer();
                                DebugMessage("Peer passed trust test.", ConsoleColor.Green);
                            }
                            else if (successfulPings >= 5)
                            {
                                // swap event handlers
                                peerChannel.DataReceived -= dataReceivedHandler;
                                peerChannel.DataReceived += postTestDataReceivedHandler;
                            }
                        }
                    };

                    peerChannel.DataReceived += dataReceivedHandler;

                    while (peerChannel.IsTrustedPeer == false)
                    {
                        PureMessagePacket pingMessage = new PureMessagePacket
                        {
                            Message = $"Ping from {PeerNetwork.LocalIPV4Address}"
                        };
                        string outgoing = Serialize(pingMessage);
                        WrapPacket(PacketType.PureMessage, ref outgoing);
                        peerChannel.LoadOutgoingData(outgoing);
                        Thread.Sleep(3000);
                    }
                }
            }

            /// <summary>
            /// Handles trust and permissions in regards to bootstrap connections.
            /// </summary>
            public static class BootstrapTrustPolicy
            {
                private static bool _allowBootstrapAuthorityConnection = false;
                private static bool _allowBootstrapTrustlessConnection = true;
                private static bool _mustBeAuthority = false;
                private static bool _firstSingleLockingAuthority = false;
                private static BootstrapChannel FirstLockingAuthority { get; set; } = null; // only used in FirstSingleLockingAuthority mode
                private static bool _firstLockingAuthroitySet = false;

                /// <summary>
                /// Gets or sets whether bootstrap authority connections are allowed.
                /// When enabled, the client can connect to bootstrap servers able to issue command tasks that are signed with authority certificates.
                /// This signed command tasks will be executed by the client and are used to control the network.
                /// </summary>
                public static bool AllowBootstrapAuthorityConnection
                {
                    get => _allowBootstrapAuthorityConnection;
                    set
                    {
                        if ((_firstSingleLockingAuthority == true) && (value == false))
                        {
                            // must enforce consistent policy rules
                            throw new TrustPolicyConflictException("FirstSingleLockingAuthority is true, therefore AllowBootstrapAuthorityConnection must be true.");
                        }
                        else
                        {
                            _allowBootstrapAuthorityConnection = value;
                        }
                    }
                }

                /// <summary>
                /// Gets or sets whether bootstrap trustless connections are allowed.
                /// When enabled, the client can connect to bootstrap servers able to serve a static endpoint for giving new peers the peer list.
                /// This can be disabled to enforce bootstrap authority.
                /// </summary>
                /// <remarks>Trustless boostrap connection cannot relay commands or information of any sorts, they may only serve to relay identifying peer information.</remarks>
                public static bool AllowBootstrapTrustlessConnection
                {
                    get => _allowBootstrapTrustlessConnection;
                    set
                    {
                        if ((_firstSingleLockingAuthority == true) && (value == true))
                        {
                            throw new TrustPolicyConflictException("FirstSingleLockingAuthority is true, therefore AllowBootstrapTrustlessConnection must be false.");
                        }
                        else
                        {
                            _allowBootstrapTrustlessConnection = value;
                        }
                    }
                }

                /// <summary>
                /// If true, the first authority connection will be the only authority connection.
                /// No other authority connections will be allowed.
                /// Setting this value to true will also set <see cref="MustBeAuthority"/> and <see cref="AllowBootstrapAuthorityConnection"/> to true.
                /// </summary>
                public static bool FirstSingleLockingAuthority
                {
                    get => _firstSingleLockingAuthority;
                    set
                    {
                        _firstSingleLockingAuthority = value;
                        if (value == true)
                        {
                            _mustBeAuthority = true;
                            _allowBootstrapAuthorityConnection = true;
                        }
                    }
                }

                /// <summary>
                /// Gets whether the first authority connection has been set or not.
                /// </summary>
                public static bool FirstSingleLockingAuthoritySet
                {
                    get => _firstLockingAuthroitySet;
                }

                /// <summary>
                /// Sets the first authority connection to be established via the bootstrap connection.
                /// </summary>
                /// <param name="authority">The bootstrap channel that will take first single locking authority.</param>
                public static void SetFirstLockingAuthority(BootstrapChannel authority)
                {
                    if (_firstLockingAuthroitySet == false)
                    {
                        FirstLockingAuthority = authority;
                        _firstLockingAuthroitySet = true;
                    }
                }

                /// <summary>
                /// Gets or sets whether bootstrap servers must establish an authority connection.
                /// </summary>
                /// <remarks>Setting this to true will automatically set <see cref="AllowBootstrapTrustlessConnection"/> to false.</remarks>
                public static bool MustBeAuthority
                {
                    get => _mustBeAuthority;
                    set
                    {
                        _mustBeAuthority = value;
                        if (value == true)
                        {
                            _allowBootstrapTrustlessConnection = false;
                        }
                    }
                }


            }

            public static class PeerNetworkTrustPolicy
            {
                private static LocalIdentifierSetPolicyTypes _localIdentifierInitPolicy = LocalIdentifierSetPolicyTypes.LocalAndRemote;

                /// <summary>
                /// Gets or sets the policy for initializing and managing the identifier of a peer member in the P2P network.
                /// This policy determines whether the identifier can be set locally, remotely, or both, and whether it can be changed after initial assignment.
                /// </summary>
                /// <exception cref="UnauthorizedAccessException">
                /// Thrown if an attempt is made to change the policy to or from <see cref="LocalIdentifierSetPolicyTypes.StrictLocalOnly"/> or <see cref="LocalIdentifierSetPolicyTypes.StrictRemoteOnly"/>
                /// after the identifier has already been set.
                /// </exception>
                public static LocalIdentifierSetPolicyTypes LocalIdentifierSetPolicy
                {
                    get => _localIdentifierInitPolicy;
                    set
                    {
                        if ((_localIdentifierInitPolicy == LocalIdentifierSetPolicyTypes.StrictLocalOnly || _localIdentifierInitPolicy == LocalIdentifierSetPolicyTypes.StrictRemoteOnly) && (_identifierSet == true))
                        {
                            // ensure that the identifier cannot be changed after initial assignment
                            throw new UnauthorizedAccessException("The identifier policy is locked and cannot be changed after initial assignment.");
                        }
                        else
                        {
                            _localIdentifierInitPolicy = value;
                        }
                    }
                }

            }

            public enum BootstrapTrustPolicyType
            {
                Trustless,
                Authority
            }

            /// <summary>
            /// Specifies the policy for initializing and managing the identifier of a peer member in the P2P network.
            /// This policy determines whether the identifier can be set locally, remotely, or both, and whether it can be changed after initial assignment.
            /// </summary>
            public enum LocalIdentifierSetPolicyTypes
            {
                /// <summary>
                /// The identifier is set only upon initialization and cannot be changed thereafter.
                /// This ensures that the identifier remains consistent and is not influenced by any external commands or local changes.
                /// Note that this will lock in the policy and prevent any changes to the identifier after initial assignment.
                /// </summary>
                StrictLocalOnly,

                /// <summary>
                /// The identifier can be set and changed locally by the client.
                /// This allows the client to manage its own identifier through out application lifecycle, but does not permit remote commands to alter it.
                /// </summary>
                Local,

                /// <summary>
                /// The identifier can be set and changed both locally and remotely.
                /// The client can receive a NetworkTask from a trusted source (e.g., a bootstrap server in authority mode) with instructions to change the identifier.
                /// This provides flexibility for both local and remote management of the identifier.
                /// </summary>
                LocalAndRemote,

                /// <summary>
                /// The identifier can only be set based on a NetworkTask from a remote authority source, such as a bootstrap server.
                /// This ensures that the identifier is controlled by a trusted external entity and cannot be changed locally by the client.
                /// </summary>
                RemoteOnly,

                /// <summary>
                /// The identifier is strictly initialized and managed by a remote authority source and cannot be changed locally.
                /// This provides the highest level of control by ensuring that the identifier is only influenced by trusted remote commands.
                /// Note that this will lock in the policy and prevent any changes to the identifier after initial assignment.
                /// </summary>
                StrictRemoteOnly
            }
        }
        #endregion

        #region Management Policies


        #endregion
    }
}