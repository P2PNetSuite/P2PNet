using P2PNet.NetworkPackets;
using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static P2PNet.Distribution.DistributionHandler;

namespace P2PNet.Distribution.FileManager
{

    public class MemoryMappedFileManager : IFileManager
    {
        private static object _lock = new object(); // For thread safety

        // memory mapped files dictionary
        private Dictionary<string, MemoryMappedFile> _memoryMappedFiles = new Dictionary<string, MemoryMappedFile>();

        private Dictionary<string, MemoryEntry> _miscDataEntries = new Dictionary<string, MemoryEntry>();

        internal struct MemoryEntry
        {
            internal string Filename { get; set; }
            internal int Id { get; set; }
            internal object Data { get; set; }
        }

        public void Initialize()
        {
           _memoryMappedFiles = new Dictionary<string, MemoryMappedFile>();
           _miscDataEntries = new Dictionary<string, MemoryEntry>();
        }

        public async Task InboundDatapacketToFile(DataTransmissionPacket packet)
        {
            byte[] fileData = UnwrapData(packet);
            string fileName = "randomtest";
            WriteToFile(fileName, fileData);
        }
        public byte[] GetFile(string fileName)
        {
            lock (_lock)
            {
                if (_memoryMappedFiles.ContainsKey(fileName))
                {
                    using (var memoryMappedFile = _memoryMappedFiles[fileName])
                    {
                        using (var stream = memoryMappedFile.CreateViewStream())
                        {
                            byte[] buffer = new byte[stream.Length];
                            stream.Read(buffer, 0, buffer.Length);
                            return buffer;
                        }
                    }
                }
                else
                {
                    throw new FileNotFoundException($"File {fileName} not found in memory-mapped files.");
                }
            }
        }

        public byte[] GetFile(int fileId)
        {
            lock (_lock)
            {
                string fileName = _miscDataEntries.FirstOrDefault(x => x.Value.Id == fileId).Key;
                if (fileName != null && _memoryMappedFiles.ContainsKey(fileName))
                {
                    using (var memoryMappedFile = _memoryMappedFiles[fileName])
                    {
                        using (var stream = memoryMappedFile.CreateViewStream())
                        {
                            byte[] buffer = new byte[stream.Length];
                            stream.Read(buffer, 0, buffer.Length);
                            return buffer;
                        }
                    }
                }
                else
                {
                    throw new FileNotFoundException($"File with ID {fileId} not found in memory-mapped files.");
                }
            }
        }

        public void WriteToFile(string fileName, byte[] data)
        {
            lock (_lock)
            {
                if (_memoryMappedFiles.ContainsKey(fileName))
                {
                    using (var memoryMappedFile = _memoryMappedFiles[fileName])
                    {
                        using (var stream = memoryMappedFile.CreateViewStream())
                        {
                            stream.Write(data, 0, data.Length);
                        }
                    }
                }
                else
                {
                    throw new FileNotFoundException($"File {fileName} not found in memory-mapped files.");
                }
            }
        }

        public void WriteToFile(string fileName, string data)
        {
            byte[] byteData = Encoding.UTF8.GetBytes(data);
            WriteToFile(fileName, byteData);
        }
    }
}
