# P2PNet

A .Net library toolkit for quickly building P2P network applications. P2PNet is fully AOT compatible.

<p align="center">
    <img src="https://github.com/realChrisDeBon/P2PNet/assets/97779307/36f3441a-2905-476e-ac6a-c5fa8a9112b0" width="175" height="175" alt="p2pnet logo">
</p>

![passing](https://github.com/p2pnetsuite/P2PNet/actions/workflows/dotnet.yml/badge.svg) ![version](https://img.shields.io/badge/Version-.Net_9-purple) [![License](https://img.shields.io/badge/License-MIT-blue)](https://github.com/realChrisDeBon/P2PNet/blob/main/LICENSE)

### About

---

P2PNet facilitates peer-to-peer networking with an array of components for setting up your network. Initial peer discovery can be initiated in the LAN, and facilitated over a WAN utilizing various methods such as bootstrapping and IPv6 ICMP blasting. The PeerNetwork will be able to use a range of interoperable WAN and LAN discovery mechanisms to expand and grow the network, manage peer connections, and distribute data and information. Implementing the P2PNet library will make implementing peer-to-peer functionality in your application more seamless and integrated.


|        |                                                LAN Discovery                                                |                                                                                        IPv6<br /><br /> Serverless Discovery                                                                                        |                                      Static<br /> Server Bootstrap                                      |                                                 NAT Holepunching                                                 |                                                                                          File and<br /><br />Data Storage                                                                                          |
| -------- | :------------------------------------------------------------------------------------------------------------: | :-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------: | :--------------------------------------------------------------------------------------------------------: | :----------------------------------------------------------------------------------------------------------------: | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------: |
| Status | ![](https://custom-icon-badges.demolab.com/badge/stable-darkgreen.svg?logo=check-circle&logoSource=octicons) | ![](https://custom-icon-badges.demolab.com/badge/working-darkgreen.svg?logo=check-circle&logoSource=octicons)  ![](https://custom-icon-badges.demolab.com/badge/some--bugs-orange.svg?logo=bug&logoSource=octicons) | ![](https://custom-icon-badges.demolab.com/badge/in--progress-yellow.svg?logo=alert&logoSource=octicons) | ![](https://custom-icon-badges.demolab.com/badge/not--implemented-red.svg?logo=circle-slash&logoSource=octicons) | ![](https://custom-icon-badges.demolab.com/badge/working-darkgreen.svg?logo=check-circle&logoSource=octicons) ![](https://custom-icon-badges.demolab.com/badge/some--bugs-orange.svg?logo=bug&logoSource=octicons) |

### Dependency: WinPcap

---

The P2PNet library requires WinPcap installed on the target system. You can check the releases for a working version that interops with the library. For automation, CI/CD and distribution you are advised to read the license guidelines.

### Bootstrap Server Container

---

Docker image for launching an instance of the bootstrap server:

[![Docker](https://img.shields.io/badge/docker-%230db7ed.svg?style=for-the-badge&logo=docker&logoColor=white)](http://ghcr.io/p2pnetsuite/p2pnet/p2pbootstrap)

See live example deployment:

[![Fly.io Badge](https://img.shields.io/badge/Fly.io-24175B?logo=flydotio&logoColor=fff&style=for-the-badge)](https://p2pbootstrap.fly.dev/)

### Documentation

---

Technical overview and API documentation available down below:

[![view - Documentation](https://img.shields.io/badge/view-Documentation-blue?style=for-the-badge)](https://p2pnetsuite.github.io/P2PNet/)
