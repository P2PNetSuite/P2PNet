---
uid: nettasks
---
## Network Tasks

Network tasks represent discrete actions carried out across the peer-to-peer network, such as blocking, messaging, or synchronizing data. Each task is described by a `NetworkTask` object, which contains:

- **TaskType**: An enum value indicating the exact operation (e.g., `BlockIP`, `SendData`, or `AuthorizePeer`).
- **TaskData**: A dictionary holding any supporting information (e.g., target peer identifier, a message payload).

### Task Flow

Tasks queue within the NetworkTaskHandler move in two directions:

1. **Inbound Queue**  
   When a peer sends a `NetworkTask`, it is placed in the inbound queue for validation. The `NetworkTaskOriginInfo` provides metadata such as the sender’s IP address, SourceOriginIdentifier of the packet it came from, or a public key if known. The `NetworkTaskTrustConfiguration` (exposed as `NetworkTaskTrustSettings` within `PeerNetwork.TrustPolicies.PeerNetworkTrustPolicies`) applies trust rules—if a task fails to comply, it is blocked; otherwise it is routed to the corresponding handler delegate and executed.

2. **Outbound Queue**  
   Locally created tasks (e.g., to send a message or request data) go into the outbound queue. Before dispatching, the system checks whether the connection requires wrapping the task in a higher-level `DataTransmissionPacket` or can send it directly. Typically, connectionless transmission is wrapped (e.g. UDP or HTTP)

<p>
    <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/networktasks_flow.png" alt="peer network chart">
</p>

### Trust Configuration

`NetworkTaskTrustConfiguration` maps each `TaskType` to a set of required `TaskTrustParameter` values. These parameters (e.g., `Open`, `TrustedPeer`, or `AuthorityBootstrapServer`) define which security checks, if any, must be conducted to ensure the NetworkTask complies with security standards. This system lets developers tailor how strictly each task is authenticated or whether it needs a signed hash.

<p>
    <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/networktasks_configuration.png" alt="peer network chart">
</p>

### Handlers and Delegates

`NetworkTaskHandler` houses default delegate methods for each `TaskType` (e.g., `DefaultSendMessageHandler`). You can override these delegates to implement custom logic. Valid tasks are handed to their respective delegate, which performs the corresponding action (block, authorize, send data, ect).

This approach ensures that each task is processed securely and in a consistent, extensible manner.