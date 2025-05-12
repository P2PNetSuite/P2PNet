using P2PNet.Peers.CommProtocols;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.MixedReality.WebRTC;

namespace P2PNet.Peers.CommProtocols
{
    public class WebRtcNetProtocol : INetProtocol
    {
        private readonly PeerConnection _peerConnection;
        private readonly DataChannel _dataChannel;
        private readonly MemoryStream _receiveBuffer = new MemoryStream();
        private readonly SemaphoreSlim _receiveSignal = new SemaphoreSlim(0);

        public WebRtcNetProtocol(string sourceOriginIdentifier)
        {
            _peerConnection = new PeerConnection();
            _peerConnection.InitializeAsync().GetAwaiter().GetResult();


            _dataChannel = _peerConnection.DataChannels.Count > 0
                ? _peerConnection.DataChannels[0]
                : _peerConnection.AddDataChannelAsync("default", true, true).GetAwaiter().GetResult();

            _dataChannel.MessageReceived += DataChannel_MessageReceived;
        }

        private void DataChannel_MessageReceived(byte[] message)
        {
            lock (_receiveBuffer)
            {
                _receiveBuffer.Position = _receiveBuffer.Length;
                _receiveBuffer.Write(message, 0, message.Length);
                _receiveSignal.Release();
            }
        }

        public Stream GetStream()
        {
            return new WebRtcStream(_dataChannel);
        }

        public Task SendAsync(byte[] data, int offset, int count)
        {
            if (!_dataChannel.State.HasFlag(DataChannel.ChannelState.Open))
                throw new IOException("WebRTC DataChannel is not open.");

            var sendBuffer = new byte[count];
            Array.Copy(data, offset, sendBuffer, 0, count);
            _dataChannel.SendMessage(sendBuffer);
            return Task.CompletedTask;
        }

        public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count)
        {
            await _receiveSignal.WaitAsync();

            lock (_receiveBuffer)
            {
                _receiveBuffer.Position = 0;
                int bytesToRead = (int)Math.Min(count, _receiveBuffer.Length);
                int bytesRead = _receiveBuffer.Read(buffer, offset, bytesToRead);


                var remaining = _receiveBuffer.Length - _receiveBuffer.Position;
                if (remaining > 0)
                {
                    var temp = new byte[remaining];
                    _receiveBuffer.Read(temp, 0, (int)remaining);
                    _receiveBuffer.SetLength(0);
                    _receiveBuffer.Write(temp, 0, (int)remaining);
                }
                else
                {
                    _receiveBuffer.SetLength(0);
                }
                _receiveBuffer.Position = 0;
                return bytesRead;
            }
        }

        public EndPoint RemoteEndPoint => null; // N/A for WebRTC

        public bool IsConnected => _peerConnection.IsConnected;

        public void Close()
        {
            _dataChannel.MessageReceived -= DataChannel_MessageReceived;
            _dataChannel.PeerConnection.RemoveDataChannel(_dataChannel);
            _peerConnection.Close();
        }

        public void Dispose()
        {
            Close();
            _receiveBuffer.Dispose();
            _receiveSignal.Dispose();
        }

        private class WebRtcStream : Stream
        {
            private readonly DataChannel _dataChannel;
            private readonly MemoryStream _receiveBuffer = new MemoryStream();
            private readonly SemaphoreSlim _receiveSignal = new SemaphoreSlim(0);

            public WebRtcStream(DataChannel dataChannel)
            {
                _dataChannel = dataChannel;
                _dataChannel.MessageReceived += OnMessageReceived;
            }

            private void OnMessageReceived(byte[] message)
            {
                lock (_receiveBuffer)
                {
                    _receiveBuffer.Position = _receiveBuffer.Length;
                    _receiveBuffer.Write(message, 0, message.Length);
                    _receiveSignal.Release();
                }
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
                _receiveSignal.Wait();
                lock (_receiveBuffer)
                {
                    _receiveBuffer.Position = 0;
                    int bytesToRead = (int)Math.Min(count, _receiveBuffer.Length);
                    int bytesRead = _receiveBuffer.Read(buffer, offset, bytesToRead);

                    // Remove read bytes from buffer
                    var remaining = _receiveBuffer.Length - _receiveBuffer.Position;
                    if (remaining > 0)
                    {
                        var temp = new byte[remaining];
                        _receiveBuffer.Read(temp, 0, (int)remaining);
                        _receiveBuffer.SetLength(0);
                        _receiveBuffer.Write(temp, 0, (int)remaining);
                    }
                    else
                    {
                        _receiveBuffer.SetLength(0);
                    }
                    _receiveBuffer.Position = 0;
                    return bytesRead;
                }
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                await _receiveSignal.WaitAsync(cancellationToken);
                lock (_receiveBuffer)
                {
                    _receiveBuffer.Position = 0;
                    int bytesToRead = (int)Math.Min(count, _receiveBuffer.Length);
                    int bytesRead = _receiveBuffer.Read(buffer, offset, bytesToRead);

                    // Remove read bytes from buffer
                    var remaining = _receiveBuffer.Length - _receiveBuffer.Position;
                    if (remaining > 0)
                    {
                        var temp = new byte[remaining];
                        _receiveBuffer.Read(temp, 0, (int)remaining);
                        _receiveBuffer.SetLength(0);
                        _receiveBuffer.Write(temp, 0, (int)remaining);
                    }
                    else
                    {
                        _receiveBuffer.SetLength(0);
                    }
                    _receiveBuffer.Position = 0;
                    return bytesRead;
                }
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                var sendBuffer = new byte[count];
                Array.Copy(buffer, offset, sendBuffer, 0, count);
                _dataChannel.SendMessage(sendBuffer);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                var sendBuffer = new byte[count];
                Array.Copy(buffer, offset, sendBuffer, 0, count);
                _dataChannel.SendMessage(sendBuffer);
                return Task.CompletedTask;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _dataChannel.MessageReceived -= OnMessageReceived;
                    _receiveBuffer.Dispose();
                    _receiveSignal.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
