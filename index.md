---
uid: p2pnethome
---

### P2PNet

---

P2PNet facilitates peer-to-peer networking with an array of components and options for setting up your network. Initial peer discovery can be initiated in the LAN, and facilitated over a WAN utilizing various methods such as bootstrapping and IPv6 ICMP blasting (widescan). The PeerNetwork will be able to use a range of interoperable WAN and LAN discovery mechanisms to expand and grow the network. Implementing the P2PNet library, you will be able to integrate your own verification steps and protocols to validate discovered peer members before establishing an enhanced connection that will facilitate the exchange of data and information.

### Documentation

- [Peer Network🌐](misc/p2pnetwork.md)
  * [Peers👥](misc/peers.md)
- [NetworkTasks🏭](misc/networktasks.md)
- [Testing🛠️](misc/testing.md)
- [Bootstrap Server🤝](misc/bootstrapserver.md)
- [Widescan📡](misc/widescan.md)

### WinPcap

The P2PNet library utilizes the WinPcap library for its advanced network functionality. P2PNet will not work if WinPcap is not installed on the system. You can [download](https://github.com/P2PNetSuite/P2PNet/releases/tag/dependency) a version in our GitHub releases that works with the P2PNet library.

### Getting Started

Below is a simple example application that demonstrates how to integrate P2PNet into your project.

1. Install the NuGet package in your project:
   
   dotnet add package P2PNet

2. Create a simple console application (or use your existing one) and add the following code:

```csharp
using System;
using P2PNet;
using P2PNet.Peers;
using static P2PNet.PeerNetwork;

namespace ExampleApplication
{
    class Program
    {
        static void Main(string[] args)
        {
            // Print log messages to console
            PeerNetwork.Logging.OutputLogMessages = true;

            // Load local network information and addresses
            PeerNetwork.LoadLocalAddresses();

            // Enable inbound peer connections
            PeerNetwork.BeginAcceptingInboundPeers();
            
            // Keep the application running to accept connections
            Console.WriteLine("P2PNet Example Application started.");
            Console.ReadLine();
        }
    }
}
```

This basic setup mirrors the style of the Example Application, letting you quickly get up and running without the complexity of advanced routines or configurations.