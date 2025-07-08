using static P2PNet.PeerNetwork;
using P2PNet.NetworkPackets;
using static P2PNet.Distribution.DistributionHandler;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace P2PNet.Peers
{
    /// <summary>
    /// Represents a communication channel with a peer in the P2P network.
    /// </summary>
    public class PeerChannel : PeerChannelBase
    {
        /// <summary>
        /// Gets the DateTime value of when the last piece of data or information was received from this peer.
        /// </summary>
        public DateTime LastIncomingDataReceived { get; internal set; } = DateTime.Now;

        private int RETRIES = 0; // for retrying connections
        private const int MAX_RETRIES = 3;

        /// <summary>
        /// Initializes a new instance of the <see cref="PeerChannel"/> class with the specified peer.
        /// </summary>
        /// <param name="peer_">The peer to associate with this channel.</param>
        public PeerChannel(IPeer peer_)
        {
            peer = peer_;
            // If not set, default to TCP protocol using peer.Client
            if (peer.Protocol == null && peer.Client != null)
            {
                peer.Protocol = new TcpNetProtocol(peer.Client);
            }
        }

        /// <summary>
        /// Opens the communication channel with the peer and starts handling incoming and outgoing data.
        /// </summary>
        public async void OpenPeerChannel()
        {
            cancelSender = new CancellationTokenSource();
            cancelReceiver = new CancellationTokenSource();

            DebugMessage($"Successfully opened channel with new peer: {peer.IP.ToString()}:{peer.Client.Client.RemoteEndPoint.ToString()} port {peer.Port}:{peer.Client.Client.RemoteEndPoint.ToString()}", ConsoleColor.Cyan);

            peer.Client.ReceiveTimeout = 60000;
            peer.Client.SendTimeout = 20000;

            StartPacketHandling();
            receiveTask = Task.Run(() => ReadIncoming(cancelReceiver.Token));
            sendTask = Task.Run(() => SendOutgoing(cancelSender.Token));

            if (TrustPolicies.IncomingPeerTrustPolicy.RunDefaultTrustProtocol == true)
            {
                TrustPolicies.IncomingPeerTrustPolicy.DefaultTrustProtocol.Invoke(this);
            }
        }

        /// <summary>
        /// Closes the communication channel with the peer and terminates all associated tasks.
        /// </summary>
        public void ClosePeerChannel()
        {
            if ((cancelSender != null) && (cancelReceiver != null))
            {
                DebugMessage($"\t-- Ending peer connection with {peer.IP.ToString()} port {peer.Port}", ConsoleColor.DarkBlue);

                BreakAndRemovePeer();
            }
        }

        /// <summary>
        /// The default task for sending outgoing data and information to the network stream.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token provided to the send outgoing task.</param>
        protected async void SendOutgoing(CancellationToken cancellationToken)
        {
            try
            {
                var protocol = peer.Protocol;
                while (!cancellationToken.IsCancellationRequested)
                {
                    while (OutgoingDataQueue.TryDequeue(out string message))
                    {
                        try
                        {
                            byte[] buffer = Encoding.UTF8.GetBytes(message);

                            DebugMessage($"Sending: {message}", ConsoleColor.Cyan);

                            await protocol.SendAsync(buffer, 0, buffer.Length);
                        }
                        catch (Exception e)
                        {
                            DebugMessage(e.ToString(), MessageType.Critical);
                        }
                        finally
                        {
                            Thread.Sleep(100);
                        }
                    }
                }
            }
            catch (ObjectDisposedException ex)
            {
                HandleException(ex, RETRIES, MAX_RETRIES);
            }
            catch (IOException ex) when (ex.InnerException is SocketException socket_ex)
            {
                HandleException(ex, RETRIES, MAX_RETRIES);
            }
            catch (InvalidOperationException ex)
            {
                HandleException(ex, RETRIES, MAX_RETRIES);
            }
            catch (Exception ex)
            {
                DebugMessage($"Thread: {Environment.CurrentManagedThreadId.ToString()}\nPeer channel connection issue (sender): {peer.IP.ToString()} port {peer.Port}" + Environment.NewLine + ex.Message, MessageType.Critical);
                BreakAndRemovePeer(); // Stop after generic exceptions
            }
            finally
            {
                Thread.Sleep(3000);
            }
        }

        /// <summary>
        /// The default task for reading incoming network stream data.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token provided to the read incoming task.</param>
        public async void ReadIncoming(CancellationToken cancellationToken)
        {
            int ReceiverErrors = 0;
            try
            {
                var protocol = peer.Protocol;
                string receivedData = ""; // Top-level string

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        byte[] buffer = new byte[4096];
                        int bytesRead = 0;

                        bytesRead = await protocol.ReceiveAsync(buffer, 0, buffer.Length);

                        if (bytesRead > 0)
                        {
                            string newData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            receivedData = receivedData + newData;
                            LastIncomingDataReceived = DateTime.Now;

                            while (IsValidMessageFormat(receivedData))
                            {
                                PacketTypeRelay receivedPacket = ExtractWholeMessage(receivedData);
                                if (!string.IsNullOrEmpty(receivedPacket.Data))
                                {
                                    packetQueue.Enqueue(receivedPacket); // Add to the packet queue to be processed
                                    OnDataReceived(newData); // event test
                                }
                                receivedData = ""; // wipe receivedData ~ ~ ~
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        DebugMessage($"Encountered an error. Error code: {(int)e.HResult} {e.Message}\n{e.Data}", MessageType.Critical);
                        HandleException(e, RETRIES, MAX_RETRIES);
                    }
                    finally
                    {
                        // No stream.Flush() for protocol abstraction
                    }
                    Thread.Sleep(250);
                }
                TerminateCurrentReceiver();
            }
            catch
            {
                // Handle exception
            }
        }

        private void HandleException(Exception ex, int retries, int maxRetries)
        {
            RETRIES++;

            DebugMessage($"Exception in PeerChannel ({peer.IP.ToString()} port {peer.Port}) : {ex.Message} - Attempt {RETRIES}/{MAX_RETRIES}", MessageType.Critical);

            if (RETRIES >= MAX_RETRIES)
            {
                BreakAndRemovePeer();
            }
            else
            {
                Thread.Sleep(4000); // Retry pause of 4 seconds
            }
        }

        private void BreakAndRemovePeer()
        {
            TerminateChannel();
        }
    }
}