using P2PNet.Peers.CommProtocols;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace P2PNet.Peers
{
    /// <summary>
    /// Provides a TCP-based network protocol implementation for direct peer-to-peer connections.
    /// </summary>
    public class TcpNetProtocol : INetProtocol
    {
        private readonly TcpClient _client;

        /// <summary>
        /// Gets the protocol type identifier.
        /// </summary>
        public NetProtocolType ProtocolType => NetProtocolType.Tcp;

        /// <summary>
        /// Gets a value indicating whether this protocol requires a direct IP address connection.
        /// TCP connections are direct and require IP connectivity.
        /// </summary>
        public bool IsDirectConnection => true;

        /// <summary>
        /// Initializes a new instance of the <see cref="TcpNetProtocol"/> class.
        /// </summary>
        /// <param name="client">The TCP client for this connection.</param>
        public TcpNetProtocol(TcpClient client)
        {
            _client = client;
        }

        /// <summary>
        /// Gets the underlying network stream for data transfer.
        /// </summary>
        public Stream GetStream() => _client.GetStream();

        /// <summary>
        /// Sends data asynchronously through the TCP connection.
        /// </summary>
        public async Task SendAsync(byte[] data, int offset, int count)
        {
            await _client.GetStream().WriteAsync(data, offset, count);
        }

        /// <summary>
        /// Receives data asynchronously through the TCP connection.
        /// </summary>
        public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count)
        {
            return await _client.GetStream().ReadAsync(buffer, offset, count);
        }

        /// <summary>
        /// Gets the remote endpoint of the TCP connection.
        /// </summary>
        public EndPoint RemoteEndPoint => _client.Client.RemoteEndPoint;

        /// <summary>
        /// Gets a value indicating whether the TCP connection is currently active.
        /// </summary>
        public bool IsConnected => _client.Connected;

        /// <summary>
        /// Closes the TCP connection.
        /// </summary>
        public void Close() => _client.Close();

        /// <summary>
        /// Disposes of the TCP protocol resources.
        /// </summary>
        public void Dispose() => _client.Dispose();
    }
}
