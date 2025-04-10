﻿---
uid: p2pnetworkbasics
---
### P2P Network Basics 🌐

---

The `P2PNet` library provides the core functionality for building and managing a peer-to-peer network. It includes classes and methods for peer discovery, connection management, data exchange, and network routines. This document provides a broad overview of the peer network basics.

### Initialization

---

The `PeerNetwork` class is the main entry point for initializing and managing the peer-to-peer network. It sets up the necessary configurations and services required for network operations.

1. **Configuration**: The peer network hosts several configuration fields which can be used to fine tune control over the network operations. Some of these fields include subclasses that can be explored under `TrustPolicies`. Other aspects of configuration are in design such as the usage of queues and events, or the use of delegates for handling certain scenarios.
2. **Logging**: Logging is handled using the `ConsoleDebugger` package. Logging can be toggled between being directed to either console output or the log file, or optionally both. By default logging is omitted. To toggle logging on, set `PeerNetwork.Logging.EmitConsoleMessages` to true for console output and/or `PeerNetwork.Logging.LogToFile` for logging to file. The `ConsoleDebugger` package is a lightweight package that is easy to configure and managed by the same team that made the P2PNet (the package was actually made as a result of P2PNet development).
3. **Network Configuration**: The application scans all network interface devices and collects essential information needed for the peer network, such as public IP addresses and LAN IP addresses.

### Operation

---

The `PeerNetwork` class operates by providing several key functionalities:

1. **Peer Discovery and Connection**:The library supports both LAN and WAN peer discovery. It uses broadcasting, multicasting, and designated ports to discover peers and establishes connections using TCP.

   - **LAN Discovery**: Broadcasts are used for peer discovery within the local network.
   - **WAN Discovery**: Specific WAN components and designated ports facilitate the discovery of peers over a wide area.
2. **Peer Management**: A list of known peers and active peer channels is maintained. This supports functions such as adding or removing peers and managing connection permissions.

   - **Known Peers**: Stores details of all discovered peers.
   - **Active Peer Channels**: Manages active communication channels and leverages the `PeerChannel` class to encapsulate connection logic.
3. **Data Exchange and Network Packets**: Data exchange between peers is accomplished via network packets formatted to ensure data consistency and integrity.

   - **Network Packets**: These packets encapsulate various types of information for transmission between peers. Types include identity packets, disconnect packets, data transmission packets, pure message packets, and more. The packets are wrapped using the `DistributionProtocol` so that each packet is tagged properly, making it easier for the receiver to determine the payload type. All network packet types derive from the `NetworkPacket` base class.
   - **DistributionHandler**: This static class is responsible for handling outgoing and incoming data packets. It wraps raw data into packets and distributes them to trusted peers. In addition, the handler supports unwrapping payloads to extract raw data before processing.
   - **NetworkTaskHandler**: In parallel with data handling, the `NetworkTaskHandler` manages network tasks defined as actions such as blocking a peer, sending messages, or synchronizing data. Tasks are enqueued and processed asynchronously to maintain smooth operations across the network.
4. **Peer Channels**:
   Each communication channel between peers is represented by an instance of the `PeerChannel` class. This class manages the relay of data packets, conducts connection retries, and enforces trust policies by invoking predefined delegates from the `PacketHandleProtocol`.

### Routines

---

The `NetworkRoutines` class provides a mechanism for managing network routines. Routines are tasks that run at specified intervals to perform various network-related operations.

1. **Routine Management**: The `NetworkRoutines` class manages a dictionary of routines and provides methods for adding, starting, stopping, and setting the interval of routines.
   - **Default Routines**: The application has default routines. These default routines do not automatically startup, but are automatically added in to the routines list.
   - **Custom Routines**: Users can add custom routines to perform specific tasks.

Routines are accessed using their `RoutineName` property. This is automatically handled when they are added as network routines.

### Trust Policies

---

The `PeerNetwork` class employs a multi-pronged trust model to ensure secure and robust peer interactions. These policies determine how incoming connections are verified, how bootstrap nodes are treated, and how the local network identifier is managed.

1. **Incoming Peer Trust Policy** This policy governs the verification and handling of peers attempting to connect to the network. It includes several configurable settings:

   - **AllowDefaultCommunication**: 
     Enables basic communication—such as exchanging `PureMessagePackets` and `DisconnectPackets`—without full verification.
     *Example use case*: In a trusted LAN environment, you might allow default communication to quickly establish a connection before a deeper security check.
   - **AllowEnhancedPacketExchange**: 
     When enabled, permits the exchange of complex packets (e.g., `DataTransmissionPackets`) that may carry critical data.
     *Example use case*: For networks where peers are pre-validated, you might allow enhanced packet exchange immediately to boost performance.
   - **RunDefaultTrustProtocol**: 
     Initiates the system’s built-in handshake mechanism (which you can replace with a custom `PeerTrustHandshake` delegate) that verifies a peer’s authenticity before granting them full network access.
   - **Incoming Peer Placement**: 
     Supports both queue-based and event-based models. Queue-based placement helps throttle connections when there are many incoming requests, while event-based placement provides immediate notification for further processing.
2. **Bootstrap Trust Policy** This policy handles the initial secure connection phase with bootstrap nodes that help a new node join the network. Key settings include:

   - **AllowBootstrapTrustlessConnection**: 
     Permits bootstrap connections without pre-established credentials, often useful for open networks needing quick scalability.
   - **AllowBootstrapAuthorityConnection**: 
     Allow bootstrap nodes to validate their credentials via key issuance, ensuring a tighter security model.
   - **MustBeAuthority**: Prohibits connecting to bootstrap nodes that are not operating in authority mode.
     *Example use case*: In a decentralized network, you may require that only nodes with verified authority can bootstrap, ensuring that malicious nodes cannot easily infiltrate the system.
   - **FirstSingleLockingAuthority**:
     Enforce strict measures so that the first trusted authority connection can be locked in—preventing further authority connections that might threaten network integrity.
3. **PeerNetworkTrustPolicy**  
   Controls how the local network identifier is set and maintained, and how the public IPv4 address is retrieved.  
   - **LocalIdentifierSetPolicy**: Defines policies like `StrictLocalOnly`, `Local`, `LocalAndRemote`, `RemoteOnly`, or `StrictRemoteOnly` to ensure that the identifier is assigned only under secure, predefined conditions.  
   - **PublicIPv4GetPolicy**: Determines the method used to retrieve the public IPv4 address. Options include:  
     - **SingleStaticURL**: Fetches the IP address from one static URL.  
     - **StaticURLAndFailover**: Tries a primary static URL and falls back to others if necessary.  
     - **URLListConsensus**: Utilizes multiple URLs and selects the most frequently returned IP address as the public IP.  
   - **PublicIPv4Sources**: A list of URLs that are queried to fetch the public-facing IPv4 address, based on the defined retrieval policy. URLs should point directly to an API endpoint serving a plain text response. 


These trust policies work in tandem to balance flexibility and security. You can adjust the settings to suit various network scenarios—from enterprise-level internal networks that rely on rapid, low-security handshakes, to public peer-to-peer systems that demand strict, authority-based validations.

### Overview

---

**Peer Network Architecture** 
Shows a broad overview of the architecture of the peer network, including default discovery mechanisms.

<p>
    <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/P2PNetwork.png" width="500" height="325" alt="peer network chart">
</p>
