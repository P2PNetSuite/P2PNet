using static P2PNet.PeerNetwork;
using static ConsoleDebugger.ConsoleDebugger;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Text;
using P2PNet.Peers;
using PacketDotNet.Utils;
using System.Diagnostics;
using OpenCL.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Testing")]
namespace P2PNet.Widescan
{
    public enum HardwareMode
    {
        GPU,
        Accelerator,
        CPU
    }
    public enum MaximumMemoryAllocation
    {
        NoLimit,
        TwoHundredFiftyMegabytes,
        FiveHundredMegabytes,
        OneGigabyte,
        TwoGigabytes,
        FourGigabytes,
        EightGigabytes,
        SixteenGigabytes,
    }

    /// <summary>
    /// An IPv6 tool that leverages hardware capabilities to generate vast quantities of IPv6 addresses to send identifier packets to.
    /// The widescan throws a large net out to every possible combination of addresses within the provided prefix.
    /// </summary>
    public class Widescan
    {
        private static BlockingCollection<string> ipv6Collection = new BlockingCollection<string>();
        private static bool processing = true;
        private static List<string> addressPrefixes = new List<string>();
        private static int currentAddress = 0; // used to track which address prefix we're currently on

        internal static Thread worker = new Thread(() => { });
        internal static Thread reader = new Thread(() => { });
        internal static Thread broadcaster = new Thread(() => { });

        /// <summary>
        /// Gets the hardware mode for the IPv6 address generation.
        /// </summary>
        public static HardwareMode HardwareMode { get; private set; } = new HardwareMode();
        private const string KernelSource = @"
            __kernel void GenerateIPv6(__global uint* ipv6Addresses, ulong initialOffset, uint rangeStart, uint rangeEnd) {
                ulong globalIndex = get_global_id(0);
                ulong index = initialOffset + globalIndex;
                uint addrPart1 = (index >> 48) & 0xFFFF;
                uint addrPart2 = (index >> 32) & 0xFFFF;
                uint addrPart3 = rangeStart + (globalIndex / 0xFFFF);
                uint addrPart4 = globalIndex % 0xFFFF;
                int baseIndex = globalIndex * 4;
                while (addrPart3 < rangeEnd) {
                    ipv6Addresses[baseIndex] = addrPart1;
                    ipv6Addresses[baseIndex + 1] = addrPart2;
                    ipv6Addresses[baseIndex + 2] = addrPart3;
                    ipv6Addresses[baseIndex + 3] = addrPart4;
                    addrPart4++;
                    if (addrPart4 == 0xFFFF) {
                        addrPart3++;
                        addrPart4 = 0;
                    }
                    if (addrPart3 == 0xFFFF) {
                        addrPart2++;
                        addrPart3 = 0;
                    }
                    if (addrPart2 == 0xFFFF) {
                        addrPart1++;
                        addrPart2 = 0;
                    }
                    baseIndex += 4;
                }
            }";
        private static CancellationTokenSource cancelAddressGeneration = new CancellationTokenSource();

        ///<summary>
        /// Gets or sets the maximum memory allowed for the application to use.
        /// IPv6 addresses can generate very rapidly and take a lot of system memory.
        /// The maximum allocation will pause address generation to allows processing to catch up, then resume processing.
        /// </summary>
        public static MaximumMemoryAllocation MaximumMemoryAllocated
        {
            get { return maximummemoryallowed; }
            set
            {
                maximummemoryallowed = value;
                updateMemoryParameter(value);
            }
        }
        private static void updateMemoryParameter(MaximumMemoryAllocation newvalue)
        {
            if (newvalue != MaximumMemoryAllocation.NoLimit)
            {
                // Turn on memory cap
                HasMemoryLimit = true;
                MaxMB = MemoryAllocations[maximummemoryallowed];
            }
            else
            {
                // Turn off memory cap
                HasMemoryLimit = false;
            }
        }
        private static int MbUsage = 0;
        private static int MaxMB = 0;
        private static bool HasMemoryLimit = false;
        private static MaximumMemoryAllocation maximummemoryallowed = MaximumMemoryAllocation.NoLimit;
        private static readonly Dictionary<MaximumMemoryAllocation, int> MemoryAllocations = new Dictionary<MaximumMemoryAllocation, int>
                {
                    { MaximumMemoryAllocation.NoLimit, 99999999 },
                    { MaximumMemoryAllocation.TwoHundredFiftyMegabytes, 250 },
                    { MaximumMemoryAllocation.FiveHundredMegabytes, 500 },
                    { MaximumMemoryAllocation.OneGigabyte, 1024 },
                    { MaximumMemoryAllocation.TwoGigabytes, 2048 },
                    { MaximumMemoryAllocation.FourGigabytes, 4096 },
                    { MaximumMemoryAllocation.EightGigabytes, 8192 },
                    { MaximumMemoryAllocation.SixteenGigabytes, 16384 }
                };
        static System.Threading.Timer timer;
        static void CheckMemoryUsage(object state)
        {
            // Get the current process
            Process currentProcess = Process.GetCurrentProcess();

            // Get the physical memory usage in bytes
            long memoryUsageBytes = currentProcess.WorkingSet64;

            // Convert to megabytes
            double memoryUsageMB = memoryUsageBytes / (1024.0 * 1024.0);

            MbUsage = (int)Math.Round(memoryUsageMB);
        }

        /// <summary>
        /// Represents the first 4 higher segments of an IPv6 address (right to left).
        /// At least 1 is required to begin address generation.
        /// (TIP: use online resources to find reputable ISP companies registering prefixes and use those)
        /// </summary>
        /// <param name="input">This should be a 4 segment IPv6 prefix.</param>
        public static void AddAddressPrefix(string input)
        {
            string inputPrefix = input.Trim();
            string[] segments = inputPrefix.Split(':');
            if (segments.Length != 4)
            {
                DebugMessage("Invalid input. Must be 4 segment prefix.", MessageType.Warning);
                return;
            } // user provided too few or too many segments

            foreach (string segment in segments)
            {
                if (!IsValidHexSegment(segment))
                {
                    DebugMessage("Invalid input. Segment is not valid hexadecimal.", MessageType.Warning);
                    return;
                } // user gave bad segment in address
            }
            // no issues, add input
            addressPrefixes.Add(input);
        }
        public static void AddAddressPrefix(string[] input)
        {
            foreach (string address_ in input)
            {
                string inputPrefix = address_.Trim();
                string[] segments = inputPrefix.Split(':');
                if (segments.Length != 4)
                {
                    DebugMessage("Invalid input. Must be 4 segment prefix.", MessageType.Warning);
                    return;
                } // user provided too few or too many segments

                foreach (string segment in segments)
                {
                    if (!IsValidHexSegment(segment))
                    {
                        DebugMessage("Invalid input. Segment is not valid hexadecimal.", MessageType.Warning);
                        return;
                    } // user gave bad segment in address
                }
                // no issues, add input
                addressPrefixes.Add(address_);
            }
        }
        public static void AddAddressPrefix(List<string> input)
        {
            foreach (string address_ in input)
            {
                string inputPrefix = address_.Trim();
                string[] segments = inputPrefix.Split(':');
                if (segments.Length != 4)
                {
                    DebugMessage("Invalid input. Must be 4 segment prefix.", MessageType.Warning);
                    return;
                } // user provided too few or too many segments

                foreach (string segment in segments)
                {
                    if (!IsValidHexSegment(segment))
                    {
                        DebugMessage("Invalid input. Segment is not valid hexadecimal.", MessageType.Warning);
                        return;
                    } // user gave bad segment in address
                }
                // no issues, add input
                addressPrefixes.Add(address_);
            }
        }

        // Helper functions for IPv6 address checking
        private static bool IsValidHexSegment(string segment)
        {
            return segment.Length <= 4 && BigInteger.TryParse(segment, System.Globalization.NumberStyles.HexNumber, null, out _);
        }
        private static BigInteger ParsePrefix(string[] segments)
        {
            byte[] addressBytes = new byte[16]; // 16 bytes for IPv6

            for (int i = 0; i < segments.Length; i++)
            {
                ushort segmentValue = ushort.Parse(segments[i], System.Globalization.NumberStyles.HexNumber);
                // Big-endian order: place the high byte first
                addressBytes[2 * i] = (byte)((segmentValue >> 8) & 0xFF);
                addressBytes[2 * i + 1] = (byte)(segmentValue & 0xFF);
            }
            // Remaining bytes are initialized to 0 by default

            // Convert the byte array to BigInteger
            BigInteger address = new BigInteger(addressBytes, isUnsigned: true, isBigEndian: true);

            return address;
        }

        /// <summary>
        /// Sets the target hardware system for handling IPv6 address generation.
        /// </summary>
        /// <param name="hardwareMode">The intended hardware offload.</param>
        public static void SetHardwareMode(HardwareMode hardwareMode)
        {
            HardwareMode = hardwareMode;
        }

        public Widescan()
        {
            timer = new System.Threading.Timer(CheckMemoryUsage, null, 0, 1000); // check application memory usage
        }

        /// <summary>
        /// Begins the widescanning.
        /// </summary>
        public static async void StartWidescan()
        {
            Thread packetcapture = new Thread(() => PacketIntercept.StartCapturing(cancelAddressGeneration.Token));

            // Determine which hard task to run. If memory limit is on, then use the override that
            // accepts a bool value (this one will automatically throttle).
            worker = new Thread(() => CPUWorker(cancelAddressGeneration.Token));
            reader = new Thread(() => ReadAddressesFromPipe(cancelAddressGeneration.Token));
            broadcaster = new Thread(() => SendOutboundPing(cancelAddressGeneration.Token));

            if (P2PNet.PeerNetwork.PublicIPV6Address == null)
            {

                DebugMessage("A local IPv6 address is required for IPv6 widescan.", MessageType.Warning);

                return;
            }
            if (addressPrefixes.Count < 1)
            {

                DebugMessage("Please provide at least one address prefix to start.", MessageType.Warning);

                return;
            }

            switch (HardwareMode)
            {
                case HardwareMode.GPU:
                    if (HasMemoryLimit == true)
                    {
                        worker = new Thread(() => GPUWorker(true, cancelAddressGeneration.Token));
                    }
                    else
                    {
                        worker = new Thread(() => GPUWorker(cancelAddressGeneration.Token));
                    }
                    break;

                case HardwareMode.Accelerator:
                    if (HasMemoryLimit == true)
                    {
                        worker = new Thread(() => AcceleratorWorker(true, cancelAddressGeneration.Token));
                    }
                    else
                    {
                        worker = new Thread(() => AcceleratorWorker(cancelAddressGeneration.Token));
                    }
                    break;

                case HardwareMode.CPU:
                    if (HasMemoryLimit == true)
                    {
                        worker = new Thread(() => CPUWorker(true, cancelAddressGeneration.Token));
                    }
                    else
                    {
                        worker = new Thread(() => CPUWorker(cancelAddressGeneration.Token));
                    }

                    break;
            }
            packetcapture.Start();
            worker.Start();
            reader.Start();
            broadcaster.Start();

            packetcapture.Join();
            worker.Join();
            reader.Join();
            broadcaster.Join();

        }

        /// <summary>
        /// Stops the widescanning.
        /// </summary>
        public static async void StopWidescan()
        {
            cancelAddressGeneration.Cancel();
        }

        static void ReadAddressesFromPipe(CancellationToken cancellationToken)
        {
            using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", "IPv6_Pipe", PipeDirection.In))
            {
                pipeClient.Connect();
                using (StreamReader reader = new StreamReader(pipeClient))
                {
                    while (!cancellationToken.IsCancellationRequested) // implement cancellation token 
                    {
                        string ipv6Address = reader.ReadLine();
                        if (ipv6Address != null)
                        {
                            ipv6Collection.Add(ipv6Address);

                            // This happens so rapidly it will flood the debug message queue and make it impossible to see what's going on
                            DebugMessage($"Read IPv6 Address: {ipv6Address}", MessageType.General); // Log read address

                        }
                        else
                        {
                            Thread.Sleep(10);
                        }
                    }
                }
            }
        }

        #region Worker functions
        // Workers all need cancellation tokens
        static void GPUWorker(CancellationToken cancellationToken)
        {
            using (NamedPipeServerStream pipeServer = new NamedPipeServerStream("IPv6_Pipe", PipeDirection.Out, 1, PipeTransmissionMode.Byte))
            {
                // 1. Initialize OpenCL
                ErrorCode error;
                var platform = Cl.GetPlatformIDs(out error).First();
                var device = Cl.GetDeviceIDs(platform, DeviceType.Gpu, out error).First();
                var context = Cl.CreateContext(null, 1, new[] { device }, null, IntPtr.Zero, out error);
                var commandQueue = Cl.CreateCommandQueue(context, device, CommandQueueProperties.None, out error);

                // 2. Compile the OpenCL Kernel
                var program = Cl.CreateProgramWithSource(context, 1, new[] { KernelSource }, null, out error);
                error = Cl.BuildProgram(program, 1, new[] { device }, string.Empty, null, IntPtr.Zero);
                if (error != ErrorCode.Success)
                {
                    var buildLog = Cl.GetProgramBuildInfo(program, device, ProgramBuildInfo.Log, out error);
                    throw new Exception($"Error building program: {buildLog}");
                }

                var kernel = Cl.CreateKernel(program, "GenerateIPv6", out error);

                // 3. Create Buffers
                long numAddresses = 65536L * 65536L;
                int chunkSize = 65536; // Process in smaller chunks to avoid GPU overload
                int bufferSize = chunkSize * 4 * Marshal.SizeOf(typeof(uint));
                var buffer = Cl.CreateBuffer(context, MemFlags.WriteOnly, bufferSize, out error);

                pipeServer.WaitForConnection();
                using (StreamWriter writer = new StreamWriter(pipeServer))
                {
                    for (long chunkOffset = 0; chunkOffset < numAddresses; chunkOffset += chunkSize)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        } // check for cancellation call
                        while (MbUsage >= MaxMB)
                        {
                            int x = GC.GetGeneration(ipv6Collection);
                            GC.Collect(x);
                            Thread.Sleep(1000);
                        }


                        uint rangeStart = (uint)(chunkOffset >> 16) & 0xFFFF;
                        uint rangeEnd = rangeStart + 1;

                        // Set Kernel Arguments for current chunk
                        error = Cl.SetKernelArg(kernel, 0, buffer);
                        error = Cl.SetKernelArg(kernel, 1, chunkOffset);
                        error = Cl.SetKernelArg(kernel, 2, rangeStart);
                        error = Cl.SetKernelArg(kernel, 3, rangeEnd);

                        // Execute the Kernel for the current chunk
                        var globalWorkSize = new IntPtr[] { new IntPtr(chunkSize) };
                        error = Cl.EnqueueNDRangeKernel(commandQueue, kernel, 1, null, globalWorkSize, null, 0, null, out _);

                        // Read Back Results for the current chunk
                        var results = new uint[chunkSize * 4];
                        error = Cl.EnqueueReadBuffer(commandQueue, buffer, Bool.True, IntPtr.Zero, new IntPtr(bufferSize), results, 0, null, out _);
                        Cl.Finish(commandQueue);

                        // Write results to the named pipe
                        for (int i = 0; i < chunkSize; i++)
                        {
                            string ipv6Address = $"{results[i * 4]:X}:{results[i * 4 + 1]:X}:{results[i * 4 + 2]:X}:{results[i * 4 + 3]:X}";
                            writer.WriteLine(ipv6Address);

                            // This happens so rapidly it will flood the debug message queue and make it impossible to see what's going on
                            DebugMessage($"Generated IPv6 Address: {ipv6Address}", MessageType.General); // Log read address

                        }
                        writer.Flush();
                    }
                }

                // Cleanup
                Cl.ReleaseKernel(kernel);
                Cl.ReleaseProgram(program);
                Cl.ReleaseMemObject(buffer);
                Cl.ReleaseCommandQueue(commandQueue);
                Cl.ReleaseContext(context);
            }
        }
        static void GPUWorker(bool limit, CancellationToken cancellationToken)
        {
            using (NamedPipeServerStream pipeServer = new NamedPipeServerStream("IPv6_Pipe", PipeDirection.Out, 1, PipeTransmissionMode.Byte))
            {
                // 1. Initialize OpenCL
                ErrorCode error;
                var platform = Cl.GetPlatformIDs(out error).First();
                var device = Cl.GetDeviceIDs(platform, DeviceType.Gpu, out error).First();
                var context = Cl.CreateContext(null, 1, new[] { device }, null, IntPtr.Zero, out error);
                var commandQueue = Cl.CreateCommandQueue(context, device, CommandQueueProperties.None, out error);

                // 2. Compile the OpenCL Kernel
                var program = Cl.CreateProgramWithSource(context, 1, new[] { KernelSource }, null, out error);
                error = Cl.BuildProgram(program, 1, new[] { device }, string.Empty, null, IntPtr.Zero);
                if (error != ErrorCode.Success)
                {
                    var buildLog = Cl.GetProgramBuildInfo(program, device, ProgramBuildInfo.Log, out error);
                    throw new Exception($"Error building program: {buildLog}");
                }

                var kernel = Cl.CreateKernel(program, "GenerateIPv6", out error);

                // 3. Create Buffers
                long numAddresses = 65536L * 65536L;
                int chunkSize = 65536; // Process in smaller chunks to avoid GPU overload
                int bufferSize = chunkSize * 4 * Marshal.SizeOf(typeof(uint));
                var buffer = Cl.CreateBuffer(context, MemFlags.WriteOnly, bufferSize, out error);

                pipeServer.WaitForConnection();
                using (StreamWriter writer = new StreamWriter(pipeServer))
                {
                    for (long chunkOffset = 0; chunkOffset < numAddresses; chunkOffset += chunkSize)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        } // cheeck for cancellation call

                        while (MbUsage >= MaxMB)
                        {
                            int x = GC.GetGeneration(ipv6Collection);
                            GC.Collect(x);
                            Thread.Sleep(10);
                        }

                        uint rangeStart = (uint)(chunkOffset >> 16) & 0xFFFF;
                        uint rangeEnd = rangeStart + 1;

                        // Set Kernel Arguments for current chunk
                        error = Cl.SetKernelArg(kernel, 0, buffer);
                        error = Cl.SetKernelArg(kernel, 1, chunkOffset);
                        error = Cl.SetKernelArg(kernel, 2, rangeStart);
                        error = Cl.SetKernelArg(kernel, 3, rangeEnd);

                        // Execute the Kernel for the current chunk
                        var globalWorkSize = new IntPtr[] { new IntPtr(chunkSize) };
                        error = Cl.EnqueueNDRangeKernel(commandQueue, kernel, 1, null, globalWorkSize, null, 0, null, out _);

                        // Read Back Results for the current chunk
                        var results = new uint[chunkSize * 4];
                        error = Cl.EnqueueReadBuffer(commandQueue, buffer, Bool.True, IntPtr.Zero, new IntPtr(bufferSize), results, 0, null, out _);
                        Cl.Finish(commandQueue);

                        // Write results to the named pipe
                        for (int i = 0; i < chunkSize; i++)
                        {
                            string ipv6Address = $"{results[i * 4]:X}:{results[i * 4 + 1]:X}:{results[i * 4 + 2]:X}:{results[i * 4 + 3]:X}";
                            writer.WriteLine(ipv6Address);

                            // This happens so rapidly it will flood the debug message queue and make it impossible to see what's going on
                            DebugMessage($"Generated IPv6 Address: {ipv6Address}", MessageType.General); // Log read address

                        }
                        writer.Flush();
                    }
                }

                // Cleanup
                Cl.ReleaseKernel(kernel);
                Cl.ReleaseProgram(program);
                Cl.ReleaseMemObject(buffer);
                Cl.ReleaseCommandQueue(commandQueue);
                Cl.ReleaseContext(context);
            }
        }
        static void CPUWorker(CancellationToken cancellationToken)
        {
            foreach (string addressPrefix in addressPrefixes)
            {
                string[] segments = addressPrefix.Split(':');
                int prefixLength = segments.Length;

                BigInteger startAddress = ParsePrefix(segments);

                // Calculate the number of addresses to generate based on the prefix length
                int suffixBits = (8 - prefixLength) * 16; // Each segment is 16 bits
                BigInteger addressCount = BigInteger.Pow(2, suffixBits);

                BigInteger endAddress = startAddress + addressCount - 1;

                // Parallel Processing
                int numberOfTasks = System.Environment.ProcessorCount;

                Parallel.For(0, numberOfTasks, (taskIndex, state) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        state.Stop();
                    }

                    BigInteger rangeSize = addressCount / numberOfTasks;
                    BigInteger taskStart = startAddress + taskIndex * rangeSize;
                    BigInteger taskEnd = (taskIndex == numberOfTasks - 1) ? endAddress : taskStart + rangeSize - 1;

                    for (BigInteger address = taskStart; address <= taskEnd; address++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            state.Stop();
                            break;
                        }

                        string ipv6Address = BigIntegerToIPv6Address(address);

                        ipv6Collection.Add(ipv6Address);

                        DebugMessage($"Generated IPv6 Address: {ipv6Address}", MessageType.General);
                    }
                });
            }
        }
        static void CPUWorker(bool limit, CancellationToken cancellationToken)
        {
            foreach (string addressPrefix in addressPrefixes)
            {
                string[] segments = addressPrefix.Split(':');
                int prefixLength = segments.Length;

                BigInteger startAddress = ParsePrefix(segments);

                // Calculate the number of addresses to generate based on the prefix length
                int suffixBits = (8 - prefixLength) * 16; // Each segment is 16 bits
                BigInteger addressCount = BigInteger.Pow(2, suffixBits);

                BigInteger endAddress = startAddress + addressCount - 1;

                // Parallel Processing
                int numberOfTasks = System.Environment.ProcessorCount;

                Parallel.For(0, numberOfTasks, (taskIndex, state) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        state.Stop();
                    }

                    BigInteger rangeSize = addressCount / numberOfTasks;
                    BigInteger taskStart = startAddress + taskIndex * rangeSize;
                    BigInteger taskEnd = (taskIndex == numberOfTasks - 1) ? endAddress : taskStart + rangeSize - 1;

                    for (BigInteger address = taskStart; address <= taskEnd; address++)
                    {
                        while (MbUsage <= MaxMB)
                        {
                            Thread.Sleep(15); // pause
                            if (cancellationToken.IsCancellationRequested)
                            {
                                state.Stop();
                                break;
                            }
                        }

                        if (cancellationToken.IsCancellationRequested)
                        {
                            state.Stop();
                            break;
                        }

                        string ipv6Address = BigIntegerToIPv6Address(address);

                        ipv6Collection.Add(ipv6Address);

                        DebugMessage($"Generated IPv6 Address: {ipv6Address}", MessageType.General);
                    }
                });
            }
        }

        static string BigIntegerToIPv6Address(BigInteger address)
        {
            byte[] addressBytes = address.ToByteArray(isUnsigned: true, isBigEndian: true);

            if (addressBytes.Length < 16)
            {
                byte[] paddedBytes = new byte[16];
                Array.Copy(addressBytes, 0, paddedBytes, 16 - addressBytes.Length, addressBytes.Length);
                addressBytes = paddedBytes;
            }
            else if (addressBytes.Length > 16)
            {
                byte[] trimmedBytes = new byte[16];
                Array.Copy(addressBytes, addressBytes.Length - 16, trimmedBytes, 0, 16);
                addressBytes = trimmedBytes;
            }

            IPAddress ipAddress = new IPAddress(addressBytes);
            return ipAddress.ToString();
        }



        static void AcceleratorWorker(CancellationToken cancellationToken)
        {

        }
        static void AcceleratorWorker(bool limit, CancellationToken cancellationToken)
        {

        }
        #endregion

        #region ICMP packet handling
        private static class PacketIntercept
        {
            internal static List<LibPcapLiveDevice> devices = new List<LibPcapLiveDevice>();
            internal const string startPattern = ":Connect:";
            internal const string endPattern = ":End:";
            internal const string space_buffer = "     "; // first five spaces tend to be gibberish

            static PacketIntercept() { }

            // start reading incoming packets
            public static void StartCapturing(CancellationToken cancellationToken)
            {
                // Retrieve the device list
                NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                var devices_found = CaptureDeviceList.Instance;
                foreach (var dev in devices_found)
                {
                    if (dev is LibPcapLiveDevice libPcapDevice)
                    {
                        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                        {
                            if ((dev.Description.Equals(ni.Description, StringComparison.OrdinalIgnoreCase)) &&
                                (ni.OperationalStatus == OperationalStatus.Up) &&
                                ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                                )
                            {
                                devices.Add(libPcapDevice);

                                DebugMessage($"Loaded network driver: {dev.Description}", MessageType.General);

                                break;
                            }
                        }
                    }
                }

                if (devices.Count < 1)
                {

                    DebugMessage("No devices found on this machine");

                    return;
                }

                foreach (var device in devices)
                {
                    // Open the device for capturing
                    int readTimeoutMilliseconds = 1000;
                    device.Open(DeviceModes.Promiscuous, readTimeoutMilliseconds);

                    // Register our handler function to the 'packet arrival' event
                    device.OnPacketArrival += Device_OnPacketArrival;

                    // Start capturing packets
                    device.StartCapture();

                    DebugMessage("Device opened.");

                }

                while (true)
                {
                    Thread.Sleep(10);
                }
            }

            // function to subscribe to packet arrival event
            private static void Device_OnPacketArrival(object sender, PacketCapture e)
            {
                try
                {
                    // Parse the packet
                    var packet = Packet.ParsePacket(e.GetPacket().LinkLayerType, e.GetPacket().Data);

                    // Check if it's an IP packet
                    var ipPacket = packet.Extract<IPv6Packet>();
                    if (ipPacket != null)
                    {
                        // Check if it's an ICMP packet
                        var icmpPacket = packet.Extract<IcmpV6Packet>();
                        if (icmpPacket != null)
                        {

                            DebugMessage($"ICMP packet from {ipPacket.SourceAddress} to {ipPacket.DestinationAddress}");

                            if (ipPacket.SourceAddress !=P2PNet.PeerNetwork.PublicIPV4Address)
                            {
                                LogPacketDetails(ipPacket, icmpPacket);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {

                }
            }

            private static void LogPacketDetails(IPv6Packet ipPacket, IcmpV6Packet icmpPacket)
            {

                DebugMessage($"Source IP: {ipPacket.SourceAddress}");
                DebugMessage($"Destination IP: {ipPacket.DestinationAddress}");
                DebugMessage($"TTL: {ipPacket.TimeToLive}");
                DebugMessage($"Payload: {HexToString(BitConverter.ToString(icmpPacket.PayloadData))}");
                DebugMessage("------------------------------");

                string dataout = HexToString(BitConverter.ToString(icmpPacket.PayloadData));
                bool goodmessage = ValidMessage(dataout, out int destinationport);
                if (goodmessage == true)
                {

                    DebugMessage("Detected port:" + destinationport);

                    try
                    {
                        IPAddress sourceIPAddress = IPAddress.Parse(ipPacket.SourceAddress.ToString());

                        DebugMessage("Parsed IP address: " + sourceIPAddress);

                        IPEndPoint remoteEndPoint = new IPEndPoint(sourceIPAddress, destinationport);

                        DebugMessage("Remote endpoint: " + remoteEndPoint.Address + System.Environment.NewLine + "Port: " + destinationport);

                        // TODO : Implement filtering to prevent localhost peers (self broadcast)
                        GenericPeer newpeer = new GenericPeer(sourceIPAddress, destinationport);
                        AddPeer(newpeer);

                    }
                    catch (FormatException fe)
                    {

                        DebugMessage("IP Address parsing error: " + fe.Message);

                    }
                    catch (ArgumentOutOfRangeException aoore)
                    {

                        DebugMessage("Port number error: " + aoore.Message);

                    }
                    catch (Exception ex)
                    {

                        DebugMessage("Unexpected error: " + ex.Message);

                    }

                }
            }

            // determine if there is recognizable information
            public static bool ValidMessage(string data, out int port)
            {
                port = 0;

                int startIndex = data.IndexOf(startPattern);
                int endIndex = data.IndexOf(endPattern);

                if (startIndex != -1 && endIndex != -1 && startIndex < endIndex)
                {
                    string portStr = data.Substring(startIndex + startPattern.Length, endIndex - (startIndex + startPattern.Length)).Trim();
                    if (int.TryParse(portStr, out port))
                    {
                        return true;
                    }
                }
                return false;
            }

            public static string HexToString(string hex)
            {
                // Remove hyphens
                hex = hex.Replace("-", string.Empty);

                // Convert hex string to byte array
                byte[] bytes = Enumerable.Range(0, hex.Length / 2)
                                         .Select(x => Convert.ToByte(hex.Substring(x * 2, 2), 16))
                                         .ToArray();

                // Determine the appropriate encoding based on the data type
                string decodedString;
                if (IsAscii(bytes))
                {
                    // If the data is ASCII-encoded, use ASCII encoding
                    decodedString = Encoding.ASCII.GetString(bytes);
                }
                else
                {
                    // If the data is binary or UTF-8 encoded, use UTF-8 encoding
                    decodedString = Encoding.UTF8.GetString(bytes);
                }

                return decodedString;
            }

            // Helper method to check if the byte array contains ASCII characters
            private static bool IsAscii(byte[] bytes)
            {
                foreach (byte b in bytes)
                {
                    if (b < 32 || b > 127)
                    {
                        return false; // Non-ASCII character found
                    }
                }
                return true; // All bytes are within ASCII range
            }

            public static PhysicalAddress GetLocalMacAddress()
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        return ni.GetPhysicalAddress();
                    }
                }
                return null;
            }
        }

        static void SendOutboundPing(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested) // Implement cancellation token
            {
                try
                {
                    // run a few threads on each ocde of the processor
                    Parallel.ForEach(Enumerable.Range(0, System.Environment.ProcessorCount), _ =>
                    {
                        for (int x = 0; x < 5; x++)
                        {
                            Task.Factory.StartNew(() => broadcast(), cancellationToken, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                            Thread.Sleep(2);
                        }
                    });

                }
                catch (Exception ex)
                {

                    DebugMessage($"Problem occurred: {ex.ToString()}", MessageType.Critical);

                }

            }
        }
        static void broadcast()
        {
            try
            {
                // Take blocks until an item is available or the token is canceled
                string ip_last4_segments = ipv6Collection.Take();

                string targetip = $"{addressPrefixes[currentAddress]}:{ip_last4_segments}";

                bool valid = IPAddress.TryParse(targetip, out IPAddress address);
                if (!valid)
                {

                    DebugMessage($"Bad address: {targetip}", MessageType.Warning);

                }
                else
                {
                    string messagepayload = $"{PacketIntercept.space_buffer}{PacketIntercept.startPattern}{P2PNet.PeerNetwork.ListeningPort}{PacketIntercept.endPattern}";
                    byte[] icmpData = Encoding.UTF8.GetBytes(messagepayload);

                    var icmpPacket = new IcmpV6Packet(new ByteArraySegment(icmpData))
                    {
                        Type = IcmpV6Type.EchoRequest,
                        Code = 1,
                        Checksum = 0,
                        PayloadData = icmpData
                    };

                    icmpPacket.UpdateCalculatedValues();

                    var ipPacket = new IPv6Packet(P2PNet.PeerNetwork.PublicIPV6Address, address)
                    {
                        NextHeader = PacketDotNet.ProtocolType.IcmpV6,
                        HopLimit = 128,
                        PayloadPacket = icmpPacket
                    };

                    var ethernetPacket = new EthernetPacket(
                        PacketIntercept.GetLocalMacAddress(),
                        PhysicalAddress.Parse("ff:ff:ff:ff:ff:ff"),
                        EthernetType.IPv6)
                    {
                        PayloadPacket = ipPacket
                    };

                    foreach (var device in PacketIntercept.devices)
                    {
                        bool error = false;
                        try
                        {
                            device.SendPacket(ethernetPacket);

                            DebugMessage($"Sent packet out {device.Description}.\n", ConsoleColor.DarkGreen);

                        }
                        catch (Exception ex)
                        {

                            DebugMessage($"Packet failed to send out {device.Description}\n", ConsoleColor.DarkRed);
                            DebugMessage($"Error: {ex.Message}\n", ConsoleColor.DarkRed);

                            error = true;
                        }
                        finally
                        {
                            if (!error)
                            {
                                try
                                {
                                    device.SendPacket(ethernetPacket);
                                    Thread.Sleep(50);
                                    device.SendPacket(ethernetPacket);

                                    DebugMessage($"Sent additional packets out {device.Description}.\n", ConsoleColor.DarkGreen);

                                }
                                catch (Exception ex)
                                {

                                    DebugMessage($"Failed to send additional packets out {device.Description}\n", ConsoleColor.DarkRed);
                                    DebugMessage($"Error: {ex.Message}\n", ConsoleColor.DarkRed);

                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {

                DebugMessage($"Problem occurred: {ex.ToString()}", MessageType.Critical);

            }
        }

    }
    #endregion
}

