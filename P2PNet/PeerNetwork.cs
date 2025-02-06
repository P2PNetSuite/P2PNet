
global using static ConsoleDebugger.ConsoleDebugger;

using P2PNet.DiscoveryChannels;
using P2PNet.Distribution;
using P2PNet.NetworkPackets;
using P2PNet.Peers;
using SharpPcap;
using SharpPcap.LibPcap;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Channels;

namespace P2PNet
{
    public static class PeerNetwork
        {

        // Some non-public facing settings
        static bool isBroadcaster = false;
        private static Random randomizer = new Random();
        private static bool localAddressesLoaded = false;

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
        public static int BroadcasterPort;

        /// <summary>
        /// Determines if the broadcaster port for LAN discovery will be rotated on a regular interval.
        /// </summary>
        public static bool RunBroadcastPortRotationRoutine
            {
            get { return runningPortRotate; }
            set { runningPortRotate = value; SetBroadcastPortRotate(value); }
            }
        private static bool runningPortRotate = true;
        private static System.Timers.Timer rotationTimer = new System.Timers.Timer();
        private static void SetBroadcastPortRotate(bool status_)
            {
            if(status_ == true)
                {
                rotationTimer.Start();
                rotationTimer.AutoReset = true;
                } else
                {
                rotationTimer.Stop();
                rotationTimer.AutoReset = false;
                }
            }

        /// <summary>
        /// The duration, in minutes, of how often the LAN broadcaster port will rotate.
        /// </summary>
        public static int BroadcastPortRotationDuration { get; set; } = 2;

        /// <summary>
        /// Gets the public IPv4 IP address.
        /// </summary>
        public static IPAddress PublicIPV4Address;

        /// <summary>
        /// Gets the public IPv6 IP address.
        /// </summary>
        public static IPAddress PublicIPV6Address;
        private static bool IPv6AddressFound = false;

        /// <summary>
        /// Gets the system MAC address.
        /// </summary>
        public static PhysicalAddress MAC;

        private static TcpListener listener;

        /// <summary>
        /// Gets or sets whether a designated TCP port will be actively listening for and accepting inbound TCP peer connections.
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
                SetInboundPeerAcceptancePolicy(value);
                }
            }
        private static  bool runningListener = true;
        private static void SetInboundPeerAcceptancePolicy(bool status_)
            {
            if(status_ == true)
                {
                BeginAcceptingInboundPeers();
                } else
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
                CheckIfProperInit();
                if (AcceptInboundPeers == true)
                    {
                    listener.Start();
                    Task.Run(() => AcceptClientsAsync());
                    }
                else
                    {
                    DebugMessage("Attempt was made to start inbound listener while AcceptInboundPeers is false.", MessageType.Warning);
                    }
                } catch (Exception ex)
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
            if(AcceptInboundPeers == true)
                {
                AcceptInboundPeers = false; // redundant but never too careful
                }
            }

        /// <summary>
        /// Gets the listening port for inbound TCP peer connections.
        /// </summary>
        public static int ListeningPort { get; private set; }

        /// <summary>
        /// Get or set whether or not the cleanup timer will run on a regular interval.
        /// Cleanup timer scans and removes peers that have been active for the duration
        /// set by <see cref="PeerChannelCleanupDuration"/>
        /// </summary>
        public static bool RunPeerCleanup_Routine { get; set; } = true;
        private static System.Timers.Timer cleanupTimer = new System.Timers.Timer();

        /// <summary>
        /// The duration, in minutes, of how often a cleanup of peer channels will run.
        /// This scan checks for likely inactive or disconnected peers, and removes them.
        /// </summary>
        public static int PeerChannelCleanupDuration { get; set; } = 2;

        #region Connection Collections
        private static volatile List<IPeer> activePeers_ = new List<IPeer>();

        /// <summary>
        /// Gets or sets the list of known peers.
        /// KnownPeers do not necessarily have established trust to exchange extensive data and information, but do have an open PeerChannel.
        /// </summary>
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
        /// Occurs when a new KnownPeer is added.
        /// Subscribers can use this event to handle all new peers and their peer channels, regardless of point of origin.
        /// </summary>
        /// <example>
        /// <code>
        ///    // Event is raised when a new known peer is discovered, regardless of point of origin
        ///    private static void HandleNewKnownPeer(object sender, PeerNetwork.NewPeerEventArgs e)
        ///    {
        ///        // The peer channel's DataReceived event subscribed to HandleIncomingData function
        ///        e.peerChannel.DataReceived += HandleIncomingData;
        ///    {
        ///    
        ///    private static void HandleIncomingData(object? sender, Peer_Channel_Base.DataReceivedEventArgs e)
        ///    {
        ///        Console.WriteLine(e.Data); // incoming information is printed to console
        ///    {
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
        private static InboundConnectingPeersQueue InboundConnectingPeers = new InboundConnectingPeersQueue();

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
                        // If it is EventBased queue will certainly be empty and unused. Otherwise there's a possible return.
            if (IncomingPeerTrustConfiguration.IncomingPeerPlacement != IncomingPeerTrustConfiguration.IncomingPeerMode.EventBased)
                {
                return InboundConnectingPeers.Dequeue();
                }

            DebugMessage("Cannot dequeue if IncomingPeerPlacement is not QueueBased or QueueAndEventBased", MessageType.Warning);

            return null;
            }

        /// <summary>
        /// All active <see cref="PeerChannel"/> connections are stored here. 
        /// </summary>
        /// <remarks>
        /// Inbound connections opened through <see cref="ListeningPort"/> will automatically be enqueued here. This allows you to implement any additional verification you may want in place before calling <see cref="AddPeer(IPeer, TcpClient)"/> and opening a PeerChannel
        /// </remarks>
        public static volatile List<PeerChannel> ActivePeerChannels = new List<PeerChannel>();
        private static List<LocalChannel> ActiveLocalChannels = new List<LocalChannel>();
        private static List<MulticastChannel> ActiveMulticastChannels = new List<MulticastChannel>();

        private static List<IPAddress> multicast_addresses = new List<IPAddress>();
        #endregion

        private static void InitiateLocalChannels(int designated_broadcast_port)
            {
            if (AcceptInboundPeers == true)
                {
                listener.Start();
                Task.Run(() => AcceptClientsAsync());
                }

            Queue<LocalChannel> badChannels = new Queue<LocalChannel>();
            bool error_occurred = false;
            foreach (LocalChannel channel_ in ActiveLocalChannels)
                {
                try
                    {
                        channel_.OpenLocalChannel();
                    }
                catch
                    {
                    error_occurred = true;
                    DebugMessage($"\tCannot setup local broadcast channel.", MessageType.Warning);
                    }
                finally
                    {
                    if (error_occurred == false)
                        {
                        DebugMessage($"\tSetup local channel on port: {channel_.DESIGNATED_PORT}");
                        }
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

        /// <summary>
        /// Throttles the broadcasting down.
        /// </summary>
        /// <remarks>This greatly reduces the speed at which packets are sent out. This may be useful if you want to lessen the computational load while handling newly found connections.</remarks>
        public static void ThrottleBroadcastingDown()
            {
            foreach(MulticastChannel multicastChannel in ActiveMulticastChannels)
                {
                multicastChannel.DownThrottle();
                }
            foreach(LocalChannel localChannel in ActiveLocalChannels)
                {
                localChannel.DownThrottle();
                }
            }

        static PeerNetwork()
            {
            ListeningPort = randomizer.Next(8051, 9000); // setup a port for listening
            listener = new TcpListener(IPAddress.Any, ListeningPort);

            cleanupTimer.Elapsed += DiscernPeerChannels;
            cleanupTimer.Interval = (PeerChannelCleanupDuration * 60000);

            rotationTimer.Elapsed += RotatePorts;
            rotationTimer.Interval = (BroadcastPortRotationDuration * 60000);
            }

        /// <summary>
        /// Starts timed routines if their run value is true. These routines include:
        /// <list type="bullet">
        /// <item>
        /// <description><see cref="RunBroadcastPortRotationRoutine"/></description>
        /// </item>
        /// <item>
        /// <description><see cref="RunPeerCleanup_Routine"/></description>
        /// </item>
        /// </list>
        /// </summary>
        public static async Task StartRoutines()
            {
            if (RunBroadcastPortRotationRoutine == true)
                {
                rotationTimer.Start();
                }

            if (RunPeerCleanup_Routine == true)
                {
                cleanupTimer.Start();
                }
            }

        static async Task AcceptClientsAsync()
            {
            while (AcceptInboundPeers == true)
                {
                TcpClient client = await listener.AcceptTcpClientAsync();

                IPAddress peerIP = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
                // Check for existing peer

                    if (KnownPeers.Any(p => p.IP.Equals(peerIP)))
                        {
                        DebugMessage("Duplicate connection attempt from existing peer. Ignoring.", MessageType.Warning);
                        client.Dispose();
                        }
                    else if (InboundConnectingPeers.PeerIsQueued(peerIP.ToString()))
                        {
                        DebugMessage("Duplicate connection attempt from existing peer. Ignoring.", MessageType.Warning);
                        }
                    else
                        {
                        // Set inbound as GenericPeer - intentional gap before AddPeer to implement additional verifications as needed
                        GenericPeer newPeer = new GenericPeer(((IPEndPoint)client.Client.RemoteEndPoint).Address, ((IPEndPoint)client.Client.RemoteEndPoint).Port);

                        InboundConnectingPeers.Enqueue(newPeer);
                        Task.Run(() => AddPeer(newPeer, client));
                        }
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
        public static async Task AddPeer(IPeer peer, TcpClient client = null)
            {
            List<IPeer> peers = KnownPeers;
            if ((KnownPeers.Any(p => p.IP.Equals(peer.IP))) || (KnownPeers.Any(p => p.Identifier.Equals(peer.Identifier))))
                {

                DebugMessage("Duplicate connection attempt from existing peer. Ignoring.", MessageType.Warning);

                return;
                }
            else if (peer.IP.ToString() == PublicIPV4Address.ToString() && peer.Port == ListeningPort)
                {
                DebugMessage("Listener broadcast to itself.");
                return;
                }
            else
                {
                if (client == null)
                    {
                    try
                        {

                        IPEndPoint endpoint = new IPEndPoint(peer.IP, peer.Port);
                        peer.Client = new TcpClient(peer.IP.ToString(), peer.Port);
                        if (peer.Client.Connected == false)
                            {
                            peer.Client.Connect(peer.IP, peer.Port);
                            }

                        peer.Stream = peer.Client.GetStream();
                        await channelize();
                        }
                    catch (Exception e)
                        {

                        DebugMessage($"Issue opening trusted peer channel: {peer.IP.ToString()} @ port {peer.Port}\n{e.ToString()}", MessageType.Critical);

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

                        DebugMessage($"Issue accepting incoming peer: {peer.IP.ToString()} @ port {peer.Port}", MessageType.Warning);

                        return;
                        }
                    }

                async Task channelize()
                    {
                    PeerChannel peerChannel = new PeerChannel(peer);
                    ActivePeerChannels.Add(peerChannel);
                    DistributionHandler.AddTrustedPeer(peerChannel); // REMOVE AFTER DEBUGGING & TESTING

                    peers.Add(peerChannel.peer);
                    KnownPeers = peers;
                    Thread peerThread = new Thread(peerChannel.OpenPeerChannel);
                    peerThread.Start();

                    // Raise PeerAdded event
                    Task.Run(() => { OnPeerAdded(peerChannel); });
                    return;
                    }
                }
            }

        /// <summary>
        /// Terminates a peer connection and removes it from <see cref="KnownPeers"/> and <see cref="ActivePeerChannels"/>.
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

                DistributionHandler.AddTrustedPeer(channel);
                
                if (AutoThrottleOnConnect == true)
                    {
                    ThrottleBroadcastingDown();
                    }
                foreach (PeerChannel potentialpeer in ActivePeerChannels)
                    {
                    if (channel.peer.IP.ToString() == potentialpeer.peer.IP.ToString())
                        {
                        // Filter out unnecessary duplicate connections
                        bool removalAttempt = await RemovePeer(potentialpeer);
                        }
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

        #region Bootup

        /// <summary>
        /// Scans all network interface devices and collects essential information needed for peer network.
        /// </summary>
        public static void LoadLocalAddresses()
            {
            localAddressesLoaded = true; // tell the application that we have loaded the local information
            PublicIPV6Address = GetLocalIPv6Address();
            if(PublicIPV6Address != null)
                {
                IPv6AddressFound = true; // this will signal to us later if IPv6 is usable or not

                DebugMessage($"IPv6 IP address: {PublicIPV6Address.ToString()}");

                }
            else
                {

                DebugMessage("IPv6 IP address not found. IPv6 features will not be available.", MessageType.Warning);

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
                    PublicIPV4Address = ip; // grab public IP, typically the last/only one is true
                    MAC = primaryInterface.GetPhysicalAddress();
                    }
                }
            else
                {

                DebugMessage("No primary interface found.");
                Thread.Sleep(1500);

                return; // accidently'd the internet
                }


            foreach (NetworkInterface adapter in networkInterfaces)
                {
                foreach (var dev in devices)
                    {
                    if ((dev is LibPcapLiveDevice libPcapDevice) && (dev.Description.Equals(adapter.Description, StringComparison.OrdinalIgnoreCase)))
                        {

                        IPInterfaceProperties adapterProperties = adapter.GetIPProperties();

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
        public static void BootDiscoveryChannels()
            {
            try
                {
                CheckIfProperInit(); // make sure local sys info loaded proper
                if (RunPeerCleanup_Routine == true)
                    {
                    cleanupTimer.Start();
                    } // startup the cleanup timer 

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
                }
            catch (Exception e)
                {

                DebugMessage($"{e.StackTrace} {e.Message}", MessageType.Warning);

                }
            }

        // Randomly selects a port from the designated port collection to focus on outbound broadcasting
        static void RandomizeBroadcasterPort()
            {
            BroadcasterPort = DesignatedPorts[randomizer.Next(DesignatedPorts.Count)];

            Console.WriteLine("Role: {0}, Port: {1}", isBroadcaster ? "Broadcaster" : "Listener", BroadcasterPort);
            Console.Title = ($"Broadcast port: {BroadcasterPort}");

            }

        #endregion

        #region Routines

        // Cleanup likely-inactive peers
        private static void DiscernPeerChannels(object sender, System.Timers.ElapsedEventArgs e)
            {
            cleanupTimer.Interval = (PeerChannelCleanupDuration * 60000);

            DebugMessage("DISCERNING CHANNELS", ConsoleColor.Magenta);

            Task.Run(async () =>
            {
                try
                    {
                    List<PeerChannel> peersToRemove = new List<PeerChannel>();
                    List<PeerChannel> peersToTrust = new List<PeerChannel>();

                    foreach (PeerChannel channel in ActivePeerChannels)
                        {
                        if (DateTime.Now - channel.LastIncomingDataReceived > TimeSpan.FromMinutes(PeerChannelCleanupDuration)) // No activity for 60 seconds ~ subject to change
                            {
                            peersToRemove.Add(channel);
                            }
                        if (channel.GOODPINGS > 2)
                            {
                            peersToTrust.Add(channel);
                            }
                        }

                    // Remove inactive PeerChannels from ActivePeerChannels
                    foreach (PeerChannel channel in peersToRemove)
                        {
                        bool success = await RemovePeer(channel);
                        if (success == true)
                            {

                            DebugMessage($"Removed peer for inactivity: {channel.peer.IP.ToString()} port {channel.peer.Port}", ConsoleColor.DarkCyan);

                            channel.ClosePeerChannel();
                            }
                        }
                    foreach (PeerChannel channel in peersToTrust)
                        {
                        bool success = await ElevatePeerPermission(channel);
                        if (success == true)
                            {
                            channel.TrustPeer();
                            }

                        DebugMessage($"Trusting peer: {channel.peer.IP.ToString()} port {channel.peer.Port}", ConsoleColor.Cyan);

                        }
                    }
                catch (Exception ex)
                {

                    DebugMessage($"Encountered an error: {ex}", MessageType.Critical);

                    }
            });
            }

        // Rotate LAN broadcast port
        private static void RotatePorts(object sender, System.Timers.ElapsedEventArgs e)
            {
            rotationTimer.Interval = (BroadcastPortRotationDuration * 60000);
            int currentDesgPort = BroadcasterPort;
            while (BroadcasterPort == currentDesgPort) // make sure we get a new port
                {
                BroadcasterPort = DesignatedPorts[randomizer.Next(DesignatedPorts.Count)];
                }

            DebugMessage($"Rotated to new port: {BroadcasterPort}", MessageType.General);

            }

        // Check if local address info init or not
        private static void CheckIfProperInit()
            {
            if(localAddressesLoaded != true)
                {
                throw new Exception("Local system information unavailable. Make sure to initiate with LoadLocalAddresses() before beginning peer network actions.");
                }
            }

        #endregion

        #region Trust Definitions

        /// <summary>
        /// Handles trust and permissions in regards to incoming peer connections.
        /// </summary>
        public static class IncomingPeerTrustConfiguration
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
            private static IncomingPeerMode _howToHandleInboundPeed = IncomingPeerMode.EventBased;

            /// <summary>
            /// Gets or sets whether incoming peer connections will be trusted to establish initial communication by default.
            /// This is the initial 'ping' pure message packet sent back and forth to ensure connection communicability.
            /// </summary>
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
            }

        #endregion
        }
    }