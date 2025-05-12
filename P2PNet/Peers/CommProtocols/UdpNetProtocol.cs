using P2PNet.Peers.CommProtocols;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace P2PNet.Peers.CommProtocols
{
    public class UdpNetProtocol : INetProtocol
    {
        private readonly UdpClient _client;
        private readonly IPEndPoint _remoteEndPoint;

        public UdpNetProtocol(UdpClient client, IPEndPoint remoteEndPoint)
        {
            _client = client;
            _remoteEndPoint = remoteEndPoint;
        }

        public Stream GetStream()
        {
            return new UdpStream(_client, _remoteEndPoint);
        }

        public async Task SendAsync(byte[] data, int offset, int count)
        {
            await _client.SendAsync(data, count, _remoteEndPoint);
        }

        public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count)
        {
            var result = await _client.ReceiveAsync();
            Array.Copy(result.Buffer, 0, buffer, offset, Math.Min(result.Buffer.Length, count));
            return Math.Min(result.Buffer.Length, count);
        }

        public EndPoint RemoteEndPoint => _remoteEndPoint;

        public bool IsConnected => true; // UDP is connectionless

        public void Close() => _client.Close();

        public void Dispose() => _client.Dispose();

        // wraps UDP send + receive as a Stream
        private class UdpStream : Stream
        {
            private readonly UdpClient _client;
            private readonly IPEndPoint _remoteEndPoint;

            public UdpStream(UdpClient client, IPEndPoint remoteEndPoint)
            {
                _client = client;
                _remoteEndPoint = remoteEndPoint;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var result = _client.Receive(ref Unsafe.AsRef(_remoteEndPoint));
                Array.Copy(result, 0, buffer, offset, Math.Min(result.Length, count));
                return Math.Min(result.Length, count);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _client.Send(buffer, count, _remoteEndPoint);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
            {
                return ReceiveAsync(buffer, offset, count);
            }

            private async Task<int> ReceiveAsync(byte[] buffer, int offset, int count)
            {
                var result = await _client.ReceiveAsync();
                Array.Copy(result.Buffer, 0, buffer, offset, Math.Min(result.Buffer.Length, count));
                return Math.Min(result.Buffer.Length, count);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
            {
                return _client.SendAsync(buffer, count, _remoteEndPoint);
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
