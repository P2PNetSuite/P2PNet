using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace P2PNet.Peers.CommProtocols
{
    /// <summary>
    /// Defines the contract for network protocol implementations used for peer communication.
    /// </summary>
    public interface INetProtocol : IDisposable
    {
        /// <summary>
        /// Gets the type of network protocol.
        /// </summary>
        public NetProtocolType ProtocolType { get; }

        /// <summary>
        /// Gets the underlying stream for data transfer.
        /// </summary>
        Stream GetStream();

        /// <summary>
        /// Sends data asynchronously through the protocol.
        /// </summary>
        /// <param name="data">The byte array containing data to send.</param>
        /// <param name="offset">The offset in the data array at which to begin sending.</param>
        /// <param name="count">The number of bytes to send.</param>
        Task SendAsync(byte[] data, int offset, int count);

        /// <summary>
        /// Receives data asynchronously through the protocol.
        /// </summary>
        /// <param name="buffer">The buffer to store received data.</param>
        /// <param name="offset">The offset in the buffer at which to begin storing data.</param>
        /// <param name="count">The maximum number of bytes to receive.</param>
        /// <returns>The number of bytes received.</returns>
        Task<int> ReceiveAsync(byte[] buffer, int offset, int count);

        /// <summary>
        /// Gets the remote endpoint of the connection.
        /// </summary>
        EndPoint RemoteEndPoint { get; }

        /// <summary>
        /// Closes the connection.
        /// </summary>
        void Close();

        /// <summary>
        /// Gets a value indicating whether the connection is currently active.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets a value indicating whether this protocol requires a direct IP address connection.
        /// Returns true for protocols like TCP and UDP that connect directly to an IP address and port.
        /// Returns false for relay-based protocols like TURN that communicate through an intermediary bootstrap server.
        /// </summary>
        bool IsDirectConnection { get; }
    }

    /// <summary>
    /// Defines the supported network protocol types.
    /// </summary>
    public enum NetProtocolType
    {
        /// <summary>
        /// TCP (Transmission Control Protocol) - direct peer-to-peer communication via TCP connections.
        /// </summary>
        Tcp,

        /// <summary>
        /// UDP (User Datagram Protocol) - direct peer-to-peer communication via UDP packets.
        /// </summary>
        Udp,

        /// <summary>
        /// WebRTC - peer-to-peer communication using WebRTC technology.
        /// </summary>
        WebRTC,

        /// <summary>
        /// TURN (Traversal Using Relays around NAT) - relay-based communication through a server.
        /// </summary>
        Turn
    }
}
