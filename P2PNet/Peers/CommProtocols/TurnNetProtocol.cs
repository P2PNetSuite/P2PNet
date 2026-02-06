using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using P2PNet.DicoveryChannels.WAN;
using P2PNet.Distribution;
using static P2PNet.PeerNetwork;

namespace P2PNet.Peers.CommProtocols
{
    /// <summary>
    /// Provides a TURN-based network protocol implementation that relays data through a bootstrap server.
    /// Uses continuous stream loops with inbound/outbound queues for efficient data transfer.
    /// </summary>
    public class TurnNetProtocol : INetProtocol
    {
        private readonly BootstrapChannelBase _parentChannel;
        private readonly string _remotePeerId;
        private readonly string _connectionId;
        private bool _isConnected;
        private bool _disposed;

        private readonly Channel<byte[]> _inboundQueue;
        private readonly Channel<byte[]> _outboundQueue;
        private CancellationTokenSource _streamLoopCts;
        private Task _inboundStreamTask;
        private Task _outboundStreamTask;

        /// <summary>
        /// Gets the protocol type identifier.
        /// </summary>
        public NetProtocolType ProtocolType => NetProtocolType.Turn;

        /// <summary>
        /// Gets a value indicating whether this protocol requires a direct IP address connection.
        /// TURN connections are relay-based and do not require direct IP connectivity.
        /// </summary>
        public bool IsDirectConnection => false;

        /// <summary>
        /// Gets a value indicating whether the TURN connection is currently active.
        /// </summary>
        public bool IsConnected => _isConnected && !_disposed;

        /// <summary>
        /// Gets the remote endpoint information. For TURN connections, this returns a placeholder endpoint
        /// since communication is relayed through the bootstrap server.
        /// </summary>
        public EndPoint RemoteEndPoint => new TurnEndPoint(_remotePeerId, _parentChannel.BootstrapServerEndpoint);

        /// <summary>
        /// Gets the parent bootstrap channel that owns this TURN connection.
        /// </summary>
        public BootstrapChannelBase ParentChannel => _parentChannel;

        /// <summary>
        /// Gets the connection identifier for this TURN session.
        /// </summary>
        public string ConnectionId => _connectionId;

        /// <summary>
        /// Gets the remote peer identifier.
        /// </summary>
        public string RemotePeerId => _remotePeerId;

        /// <summary>
        /// Initializes a new instance of the <see cref="TurnNetProtocol"/> class.
        /// </summary>
        /// <param name="parentChannel">The bootstrap channel that owns this TURN connection.</param>
        /// <param name="remotePeerId">The identifier of the remote peer.</param>
        /// <param name="connectionId">The unique identifier for this TURN connection.</param>
        public TurnNetProtocol(BootstrapChannelBase parentChannel, string remotePeerId, string connectionId)
        {
            _parentChannel = parentChannel;
            _remotePeerId = remotePeerId;
            _connectionId = connectionId;
            _inboundQueue = Channel.CreateUnbounded<byte[]>();
            _outboundQueue = Channel.CreateUnbounded<byte[]>();
            _isConnected = true;
        }

        /// <summary>
        /// Starts the continuous stream loops for inbound and outbound data transfer.
        /// </summary>
        public void StartStreamLoops()
        {
            if (_streamLoopCts != null) return;

            _streamLoopCts = new CancellationTokenSource();
            _inboundStreamTask = Task.Run(() => RunInboundStreamLoop(_streamLoopCts.Token));
            _outboundStreamTask = Task.Run(() => RunOutboundStreamLoop(_streamLoopCts.Token));

            DebugMessage($"TURN stream loops started for connection {_connectionId} with peer {_remotePeerId}", ConsoleColor.DarkGray, Logging.Bootstrap);
        }

        /// <summary>
        /// Stops the continuous stream loops.
        /// </summary>
        public void StopStreamLoops()
        {
            if (_streamLoopCts != null) _streamLoopCts.Cancel();
            _inboundQueue.Writer.TryComplete();
            _outboundQueue.Writer.TryComplete();
        }

        /// <summary>
        /// Continuously reads from the bootstrap server's TURN stream endpoint and enqueues incoming data.
        /// </summary>
        private async Task RunInboundStreamLoop(CancellationToken cancellationToken)
        {
            var httpClient = _parentChannel.GetTurnHttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);
            var streamUri = DistributionProtocol.GetEndpointURI(
                DistributionProtocol.CommonBootstrapEndpoints.TurnStream, 
                _parentChannel.BootstrapServerEndpoint);
            var requestUri = $"{streamUri}?connectionId={Uri.EscapeDataString(_connectionId)}&peerId={Uri.EscapeDataString(Identifier)}";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("data: "))
                        line = line.Substring(6);
                    if (string.IsNullOrEmpty(line) || line == "keepalive") continue;

                    byte[] data = Convert.FromBase64String(line);
                    await _inboundQueue.Writer.WriteAsync(data, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                DebugMessage($"TURN inbound stream error: {ex.Message}", MessageType.Warning, Logging.Bootstrap);
            }
        }

        /// <summary>
        /// Continuously reads from the outbound queue and sends data to the bootstrap server's TURN relay endpoint.
        /// </summary>
        private async Task RunOutboundStreamLoop(CancellationToken cancellationToken)
        {
            var httpClient = _parentChannel.GetTurnHttpClient();
            var relayUri = DistributionProtocol.GetEndpointURI(
                DistributionProtocol.CommonBootstrapEndpoints.TurnRelay, 
                _parentChannel.BootstrapServerEndpoint);

            await foreach (var data in _outboundQueue.Reader.ReadAllAsync(cancellationToken))
            {
                string base64Data = Convert.ToBase64String(data);
                var requestUri = $"{relayUri}?connectionId={Uri.EscapeDataString(_connectionId)}&fromPeerId={Uri.EscapeDataString(Identifier)}";
                using var content = new StringContent(base64Data, Encoding.UTF8, "text/plain");
                await httpClient.PutAsync(requestUri, content, cancellationToken);
            }
        }

        /// <summary>
        /// Enqueues data to be sent to the remote peer via the TURN relay.
        /// </summary>
        /// <param name="data">The data to send.</param>
        /// <returns>True if the data was enqueued successfully; otherwise, false.</returns>
        public bool EnqueueOutgoing(byte[] data)
        {
            if (_disposed || !_isConnected) return false;
            return _outboundQueue.Writer.TryWrite(data);
        }

        /// <summary>
        /// Reads incoming data from the inbound queue as an async enumerable.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async enumerable of byte arrays.</returns>
        public async IAsyncEnumerable<byte[]> ReadIncomingAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var data in _inboundQueue.Reader.ReadAllAsync(cancellationToken))
            {
                yield return data;
            }
        }

        /// <summary>
        /// Gets the stream for reading and writing data through the TURN relay.
        /// </summary>
        /// <returns>A stream that interfaces with the TURN queues.</returns>
        public Stream GetStream() => new TurnQueueStream(this);

        /// <summary>
        /// Sends data through the TURN relay to the remote peer.
        /// </summary>
        public async Task SendAsync(byte[] data, int offset, int count)
        {
            if (_disposed || !_isConnected)
                throw new ObjectDisposedException(nameof(TurnNetProtocol));

            var payload = new byte[count];
            Array.Copy(data, offset, payload, 0, count);
            
            if (!_outboundQueue.Writer.TryWrite(payload))
            {
                await _outboundQueue.Writer.WriteAsync(payload);
            }
        }

        /// <summary>
        /// Receives data from the TURN relay stream.
        /// </summary>
        public async Task<int> ReceiveAsync(byte[] buffer, int offset, int count)
        {
            if (_disposed || !_isConnected)
                throw new ObjectDisposedException(nameof(TurnNetProtocol));

            try
            {
                var data = await _inboundQueue.Reader.ReadAsync();
                int bytesToCopy = Math.Min(count, data.Length);
                Array.Copy(data, 0, buffer, offset, bytesToCopy);
                return bytesToCopy;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
        }

        /// <summary>
        /// Closes the TURN connection and notifies the bootstrap server.
        /// </summary>
        public void Close()
        {
            if (_disposed) return;

            _isConnected = false;
            StopStreamLoops();
            if (_parentChannel != null) _parentChannel.UnregisterTurnConnection(_connectionId);
        }

        /// <summary>
        /// Disposes of the TURN protocol resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Close();
            if (_streamLoopCts != null) _streamLoopCts.Dispose();
        }
    }

    /// <summary>
    /// Represents a virtual endpoint for TURN-based connections.
    /// </summary>
    public class TurnEndPoint : EndPoint
    {
        /// <summary>
        /// Gets the remote peer identifier.
        /// </summary>
        public string RemotePeerId { get; }

        /// <summary>
        /// Gets the bootstrap server URI used for relaying.
        /// </summary>
        public Uri RelayServerUri { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TurnEndPoint"/> class.
        /// </summary>
        public TurnEndPoint(string remotePeerId, Uri relayServerUri)
        {
            RemotePeerId = remotePeerId;
            RelayServerUri = relayServerUri;
        }

        /// <summary>
        /// Returns a string representation of the TURN endpoint.
        /// </summary>
        public override string ToString()
        {
            string host = RelayServerUri != null ? RelayServerUri.Host : "unknown";
            return $"TURN:{RemotePeerId}@{host}";
        }
    }

    /// <summary>
    /// Provides a stream abstraction over the TURN protocol's inbound/outbound queues.
    /// </summary>
    internal class TurnQueueStream : Stream
    {
        private readonly TurnNetProtocol _protocol;
        private bool _disposed;

        public TurnQueueStream(TurnNetProtocol protocol)
        {
            _protocol = protocol;
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
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TurnQueueStream));
            return await _protocol.ReceiveAsync(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TurnQueueStream));
            await _protocol.SendAsync(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
