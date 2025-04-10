using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Partitions;
using DiscUtils.Vhd;
using DiscUtils.Streams;
using P2PNet.NetworkPackets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace P2PNet.Distribution.FileManager
{
    public class VirtualImageFileManager : IFileManager, IDisposable
    {
        private const string _diskname = "virtual_image.vhd";
        private readonly string _imagePath = Path.Combine(AppContext.BaseDirectory, _diskname);
        private VirtualDisk _disk;
        private FatFileSystem _fatFs;
        private readonly object _lock = new object();

        // 10Mb
        private const long DiskSize = 10 * 1024 * 1024;

        public void Initialize()
        {
            if (!File.Exists(_imagePath))
            {
                CreateAndFormatImageFile();
            }
            OpenDiskAndMountFileSystem();
        }

        private void CreateAndFormatImageFile()
        {
            using (var diskStream = File.Create(_imagePath))
            {
                using (var disk = Disk.InitializeDynamic(diskStream, Ownership.Dispose, DiskSize))
                {
                    BiosPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsFat);
                    FatFileSystem.FormatPartition(disk, 0, "filestorage");
                }
            }
        }

        private void OpenDiskAndMountFileSystem()
        {
            _disk = new Disk(_imagePath, FileAccess.ReadWrite);
            var volume = _disk.Partitions[0].Open();
            _fatFs = new FatFileSystem(volume);
        }

        public byte[] GetFile(string fileName)
        {
            lock (_lock)
            {
                if (_fatFs.FileExists(fileName))
                {
                    using (Stream stream = _fatFs.OpenFile(fileName, FileMode.Open, FileAccess.Read))
                    {
                        byte[] buffer = new byte[stream.Length];
                        stream.Read(buffer, 0, buffer.Length);
                        return buffer;
                    }
                }
                else
                {
                    throw new FileNotFoundException($"File {fileName} not found on virtual image file system.");
                }
            }
        }

        public byte[] GetFile(int fileId)
        {
            throw new NotImplementedException("File ID lookup is not implemented in the virtual image file system.");
        }

        public void WriteToFile(string fileName, byte[] data)
        {
            lock (_lock)
            {
                // FAT
                var safeName = Path.GetFileName(fileName).ToUpperInvariant();
                if (safeName.Length > 11)
                    safeName = safeName.Substring(0, 11);


                using (Stream stream = _fatFs.OpenFile(safeName, FileMode.Create, FileAccess.Write))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                }

                _disk.Content.Flush();
                // DO NOT REMOVE BELOW -- THIS MITIGATES ODD BEHAVIOR OF WRITTEN FILE CONTENTS NOT APPEARING DURING EXTERNAL EXAMINATION
                using (var stream = _fatFs.OpenFile(fileName, FileMode.Open, FileAccess.Read))
                using (var reader = new StreamReader(stream))
                {
                 //   string contents = reader.ReadToEnd();
                }
            }
        }
        public void WriteToFile(string fileName, string data)
        {
            WriteToFile(fileName, Encoding.UTF8.GetBytes(data));
        }

        public async Task InboundDatapacketToFile(DataTransmissionPacket packet)
        {
            byte[] fileData = UnwrapData(packet);
            string fileName = "randomtest";
            WriteToFile(fileName, fileData);
        }

        public void Dispose()
        {
            _fatFs?.Dispose();
            _disk?.Dispose();
        }
    }
}
