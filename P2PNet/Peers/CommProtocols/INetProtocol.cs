using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace P2PNet.Peers.CommProtocols
{
    public interface INetProtocol : IDisposable
    {
        public NetProtocolType ProtocolType { get; }
        Stream GetStream();
        Task SendAsync(byte[] data, int offset, int count);
        Task<int> ReceiveAsync(byte[] buffer, int offset, int count);
        EndPoint RemoteEndPoint { get; }
        void Close();
        bool IsConnected { get; }
    }

    public enum NetProtocolType
    {
        Tcp,
        Udp,
        WebRTC
    }
}
