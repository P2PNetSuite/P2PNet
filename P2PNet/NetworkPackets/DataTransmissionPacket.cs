using static P2PNet.Distribution.DistributionProtocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using P2PNet.Distribution.NetworkTasks;
using P2PNet.NetworkPackets.NetworkPacketBase;
using System.Text.Json.Serialization;

namespace P2PNet.NetworkPackets
    {
    /// <summary>
    /// Represents a data transmission packet used for transmitting data throughout the peer-to-peer network.
    /// </summary>
    /// <remarks>This packet can be used to transmit files, data, and <see cref="NetworkTask"/> objects throughout the network.</remarks>
    public sealed class DataTransmissionPacket : NetworkPacket
        {
        /// <summary>
        /// Gets or sets the data contained in the packet.
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// Gets or sets the format of the data.
        /// Acceptable data formats include:
        /// <list type="bullet">
        /// <item>
        /// <description>File - represents an in-memory file</description>
        /// </item>
        /// <item>
        /// <description>Task - represents a <see cref="NetworkTask"/></description>
        /// </item>
        /// <item>
        /// <description>MiscData - represents any other type of object or class</description>
        /// </item>
        /// </list>
        /// </summary>
        public DataPayloadFormat DataType { get; set; }
        [JsonConstructor]
        public DataTransmissionPacket() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataTransmissionPacket"/> class with specified data and data format.
        /// </summary>
        /// <param name="data">The data payload within the packet.</param>
        /// <param name="dataType">Denotes the type of data contained within the data payload.</param>
        /// <remarks>Upon instantiating an instance of the <see cref="DataTransmissionPacket"/> class, the <see cref="DataTransmissionPacket.DataType"/> parameter is used to wrap the raw data with corresponding tags so it can be parsed and more easily managed throughout its life cycle.</remarks>
        public DataTransmissionPacket(byte[] data, DataPayloadFormat dataType) : this()
            {
                {
                var tags = DataFormatTagMap[dataType];

                var combinedData = new byte[data.Length + tags.OpeningTag.Length + tags.ClosingTag.Length];

                Encoding.UTF8.GetBytes(tags.OpeningTag).CopyTo(combinedData, 0);
                data.CopyTo(combinedData, tags.OpeningTag.Length);
                Encoding.UTF8.GetBytes(tags.ClosingTag).CopyTo(combinedData, tags.OpeningTag.Length + data.Length);

                this.Data = combinedData;
                this.DataType = dataType;
                }
            }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataTransmissionPacket"/> class with a <see cref="NetworkTask"/> object.
        /// </summary>
        /// <param name="networkTask">The NetworkTask to store in the payload of the packet.</param>
        public DataTransmissionPacket(NetworkTask networkTask) : this(networkTask.ToByte(), DataPayloadFormat.Task) { }


        /// <summary>
        /// Returns a string representation of the data transmission packet's data payload.
        /// </summary>
        /// <returns>A string representation of the data transmission packet's payload.</returns>
        public override string ToString()
            {
            return $"{Encoding.UTF8.GetString(Data)}";
            }

        /// <summary>
        /// Returns a JSON serialized string representation of the data transmission packet.
        /// This is ideal for transmission over network.
        /// </summary>
        /// <returns>Returns a JSON serialized string representation of the data transmission packet.</returns>
        public string ToJsonString()
        {
            return Serialize<DataTransmissionPacket>(this);
        }
        }
    }