# P2PNet

<p align="center">
    <img src="https://github.com/realChrisDeBon/P2PNet/assets/97779307/36f3441a-2905-476e-ac6a-c5fa8a9112b0" width="175" height="175" alt="p2pnet logo">
</p>

![passing](https://github.com/realChrisDeBon/P2PNet/actions/workflows/dotnet.yml/badge.svg) ![version](https://img.shields.io/badge/Version-.Net_8-purple)


P2PNet facilitates peer-to-peer networking with an array of components and options for setting up your network. Initial peer discovery can be initiated in the LAN, and facilitated over a WAN utilizing various methods such as bootstrapping and IPv6 ICMP blasting (widescan). The PeerNetwork will be able to use a range of interoperable WAN and LAN discovery mechanisms to expand and grow the network. Implementing the P2PNet library, you will be able to integrate your own verification steps and protocols to validate discovered peer members before establishing an enhanced connection that will facilitate the exchange of data and information.

<p>
    <img src="https://raw.githubusercontent.com/realChrisDeBon/P2PNet/refs/heads/master/misc/P2PNetwork.png" width="500" height="325" alt="peernetwork">
</p>


### Bootstrap 🤝
---
The application serves as a bootstrap node, providing an HTTP endpoint to distribute known peers to new peers joining the network. This setup ensures seamless peer discovery and initialization, enabling efficient and secure distributed data exchange within the peer network. By containerizing the application using Docker, deployment becomes significantly easier and makes quick VPS deployments easy. Additionally, the user control panel offers finer-grained controls over the network, including scaling and monitoring, which enhances the manageability and reliability of the peer network infrastructure.

<p>
    <img src="https://raw.githubusercontent.com/realChrisDeBon/P2PNet/refs/heads/master/misc/Bootstrap.png" width="500" height="325" alt="bootstrap chart">
</p>

**Note:** Bootstrap server still under construction 🏗️

### Widescan 📡
---
The P2PNet.Widescan class library project is designed to facilitate mass IPv6 address generation and peer discovery within a peer-to-peer network. This project leverages hardware capabilities, such as GPUs, to efficiently generate vast quantities of IPv6 addresses. By utilizing user-defined address prefixes, it allows for a more targeted and narrow scope of addresses to ping, enhancing the efficiency of the discovery process. This can be leveraged with publicly available information on IPv6 prefix registrations, like the [Ripe database](https://apps.db.ripe.net/db-web-ui/query), in order to refine the scope of the widescan. Additionally, the project employs lightweight ICMP packets to broadcast discovery information, ensuring minimal network overhead while effectively communicating with potential peers.

<p>
    <img src="https://raw.githubusercontent.com/realChrisDeBon/P2PNet/refs/heads/master/misc/Widescan.png" width="500" height="325" alt="widescan chart">
</p>



**Note:** DistributionHandler is still in mid development and more functionality will be rolled out. In its present state it works to transmit, recieve, and store.
