using P2PNet.NetworkPackets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace P2PNet.Distribution.FileManager
{
    public interface IFileManager
    {
        /// <summary>
        /// Get the file from the file manager using the file name.
        /// </summary>
        public byte[] GetFile(string fileName);
        /// <summary>
        /// Get the file from the file manager using the file id.
        /// </summary>
        public byte[] GetFile(int fileId);
        /// <summary>
        /// Write the file to the file manager using the file name and raw bytes.
        /// </summary>
        public void WriteToFile(string fileName, byte[] data);
        /// <summary>
        /// Write the file to the file manager using the file name and string.
        /// </summary>
        public void WriteToFile(string fileName, string data);
        public Task InboundDatapacketToFile(DataTransmissionPacket packet);
        /// <summary>
        /// Initialize the file manager.
        /// </summary>
        public void Initialize();
    }
}
