using static P2PNet.PeerNetwork;
using static P2PNet.Widescan.Widescan;
using static ConsoleDebugger.ConsoleDebugger;

using NUnit.Framework;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using P2PNet.Widescan;
using P2PNet;

namespace Testing
{
    [TestFixture]
    public class NetworkSystemTest
    {
        [SetUp]
        public void Setup()
        {
            // Initialize any required resources before tests
            
        }
        
        private bool IsNpcapAvailable()
        {
            string dllPath = Path.Combine(Environment.SpecialFolder.SystemX86.ToString(), "wpcap.dll");
            return File.Exists(dllPath);
        }

        [Test]
        public async Task PeerNetworkStartupTest()
        {           
            if (!IsNpcapAvailable())
            {
                Assert.Warn("Npcap DLL not found. Skipping network capture tests.");
                Assert.Inconclusive("Npcap DLL not found. Skipping test.");
                return;
            }
            
            DebugMessage("Starting PeerNetwork tests.", MessageType.General);

            // Load local addresses
            LoadLocalAddresses();

            // Begin accepting inbound peers
            AcceptInboundPeers = true;

            // Boot discovery channels
            StartBroadcastingLAN();

            // Assert that the local IP addresses are not null
            Xunit.Assert.NotNull(LocalIPV4Address);
            Xunit.Assert.NotNull(PublicIPV4Address);
            Xunit.Assert.NotNull(PublicIPV6Address);

            // Assert that ListeningPort is set
            Xunit.Assert.True(ListeningPort > 0, "ListeningPort should be greater than 0");

            // Optionally, wait for some time to allow network operations
            await Task.Delay(1000);

            // Clean up
            StopAcceptingInboundPeers();
        }

        [Test]
        public async Task WidescanTest()
        {
            // Set hardware mode
       //     SetHardwareMode(P2PNet.Widescan.HardwareMode.GPU);

            
            // Add address prefixes
            AddAddressPrefix("2001:0db8:85a3:0000");

            // Start Widescan
            StartWidescan();

            worker.Start();
            reader.Start();

            // Wait for a short period to simulate scanning
            Thread.Sleep(5000);

            // Stop Widescan
            StopWidescan();
            

            // Since Widescan runs asynchronously, we assume it starts and stops without exceptions
            NUnit.Framework.Assert.Pass("Widescan started and stopped successfully");
        }
    }
}
