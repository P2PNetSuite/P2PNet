using P2PNet.Peers.CommProtocols;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace P2PNet.Peers
{
    public class TcpNetProtocol : INetProtocol
    {
        private readonly TcpClient _client;
        public NetProtocolType ProtocolType => NetProtocolType.Tcp;

        public TcpNetProtocol(TcpClient client)
        {
            _client = client;
        }

        public Stream GetStream() => _client.GetStream();

        public async Task SendAsync(byte[] data, int offset, int count)
        {
            await _client.GetStream().WriteAsync(data, offset, count);
        }

        public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count)
        {
            return await _client.GetStream().ReadAsync(buffer, offset, count);
        }

        public EndPoint RemoteEndPoint => _client.Client.RemoteEndPoint;

        public bool IsConnected => _client.Connected;

        public void Close() => _client.Close();

        public void Dispose() => _client.Dispose();
    }
}
