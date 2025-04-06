using P2PNet.Peers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace P2PNet.DicoveryChannels.WAN
    {
    /// <summary>
    /// Represents a bootstrap server using IPeer implementation.
    /// </summary>
    public class BootstrapPeer : IPeer
    {
        /// <summary>
        /// Gets or sets the IP address of the peer.
        /// </summary>
        public IPAddress IP { get; set; }
        /// <summary>
        /// Gets or sets the port of the peer.
        /// </summary>
        public int Port
        {
            get => _endpoint?.Port ?? _port;
            set
            {
                _port = value;
                if (_endpoint != null)
                {
                    _endpoint = new UriBuilder(_endpoint.Scheme, _endpoint.Host, value).Uri;
                }
            }
        }
        private int _port;

        /// <summary>
        /// Gets or sets the URL address for the bootstrap peer.
        /// </summary>
        public string Address
        {
            get => _endpoint?.ToString();
            set
            {
                Uri uri = new UriBuilder("http", value, Port).Uri;
                _endpoint = uri;
                // optional - update IP and Port if possible.
                if (IPAddress.TryParse(_endpoint.Host, out IPAddress ip))
                {
                    IP = ip;
                }
            }
        }
        /// <summary>
        /// Gets the endpoint Uri of the bootstrap server.
        /// </summary>
        public Uri Endpoint
        {
            get => _endpoint;
        }
        private Uri _endpoint { get; set; }
        /// <summary>
        /// Gets or sets the TCP client associated with the peer.
        /// </summary>
        public TcpClient Client { get; set; }
        /// <summary>
        /// Gets or sets the network stream associated with the peer.
        /// </summary>
        public NetworkStream Stream { get; set; }
        /// <summary>
        /// Gets or sets the unique identifier for the peer.
        /// </summary>
        public string Identifier { get; set; }
        public BootstrapPeer() { }

        /// <summary>
        /// Initializes a new instance for staging communication with the bootstrap server using a URL string.
        /// </summary>
        /// <param name="url">The URL of the bootstrap server.</param>
        public BootstrapPeer(string url)
            : this(new Uri(url))
        {
        }

        /// <summary>
        /// Initializes a new instance for staging communication with the bootstrap server using a URL string and a port.
        /// </summary>
        /// <param name="url">The URL of the bootstrap server.</param>
        /// <param name="port">The port of the bootstrap server.</param>
        public BootstrapPeer(string url, int port)
            : this(new Uri(url))
        {
            Port = port;
        }

        /// <summary>
        /// Initializes a new instance using a specified IP address and port for establishing a TCP connection.
        /// </summary>
        /// <param name="ip">The IP address of the bootstrap server.</param>
        /// <param name="port">The port of the bootstrap server.</param>
        public BootstrapPeer(IPAddress ip, int port)
        {
            IP = ip;
            Port = port;
            _endpoint = new UriBuilder("http", ip.ToString(), port).Uri;
        }

        /// <summary>
        /// Initializes a new instance for staging communication with the bootstrap server using a Uri.
        /// </summary>
        /// <param name="endpoint">The endpoint Uri of the bootstrap server.</param>
        public BootstrapPeer(Uri endpoint)
        {
            _endpoint = endpoint;
            // try to set IP and Port from the Host portion of the Uri.
            if (IPAddress.TryParse(endpoint.Host, out IPAddress ip))
            {
                IP = ip;
            }
            Port = endpoint.Port;
        }
    }
}
