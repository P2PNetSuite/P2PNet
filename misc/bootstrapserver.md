﻿---
uid: p2pnetbootstrap
---
### Bootstrap 🤝

---

The application serves as a bootstrap node, providing an HTTP endpoint to distribute known peers to new peers joining the network. This setup ensures seamless peer discovery and initialization, enabling efficient and secure distributed data exchange within the peer network. By containerizing the application using Docker, deployment becomes significantly easier and makes quick VPS deployments easy. Additionally, the user control panel offers finer-grained controls over the network, including scaling and monitoring, which enhances the manageability and reliability of the peer network infrastructure.

### Initialization

---

The `P2PBootstrap` project initializes by setting up the necessary configurations and services required for the bootstrap server to operate. The main entry point is the `Program.cs` file, which configures the application and starts the web server.

1. **Configuration**: The application reads configuration settings from the `appsettings.json` file. This includes settings for encryption key directory, primary key names, database path, and other essential configurations. In order to ease the deployment process for containerized instances, all configuration settings in the configuration file `appsettings.json` have an environmental variable equivalent that can be used. In order to tell the application to use these, the environmental variable `CONTAINERIZED_ENVIRONMENT` must be set to to `true`. In the table below, you can see each configuration key alongside its corresponding environment variable name and a brief description of its purpose.


| AppSettings Key                          | Environment Variable | Description                                                                                                                       |
| ------------------------------------------ | ---------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| Configuration:KeysDirectory              | KEYS_DIRECTORY       | Specifies the directory where encryption keys are stored.                                                                         |
| Configuration:BootstrapMode              | BOOTSTRAP_MODE       | Determines whether the bootstrap server runs in Authority or Trustless mode.                                                      |
| Configuration:AuthorityKey:PublicKey     | PUBLIC_KEY_PATH      | The filename (or path, relative to the base directory) for the public key used by the bootstrap server.                           |
| Configuration:AuthorityKey:PrivateKey    | PRIVATE_KEY_PATH     | The filename (or path) for the private key used by the bootstrap server.                                                          |
| Configuration:NetworkName                | NETWORK_NAME         | The identifier for the P2P network, used to distinguish this network from others.                                                 |
| Configuration:OptionalEndpoints:PublicIP | ENDPOINT_PUBLICIP    | Server will serve a GET endpoint`/api/Bootstrap/publicip` which can be used by clients to retrieve their public face IPv4 address |
| Database:DbFileName                      | DB_FILENAME          | The filename for the local database file storing bootstrap server data.                                                           |

When running in a containerized environment, the configuration management first attempts to retrieve these values from their respective environment variables (e.g., "KEYS_DIRECTORY" for the keys folder). If an environment variable isn't set, it falls back to the values specified in appsettings.json. This design allows for flexible configuration management during deployment, especially in environments like Docker containers where runtime settings might differ from development.

This side-by-side mapping ensures that you can maintain consistent configuration across different deployment scenarios with minimal code changes.

2. **Logging**: Logging is handled using the `ConsoleDebugger` package.
3. **Web Server Setup**: The application uses ASP.NET Core to set up the web server. It configures the HTTP request pipeline, enabling default files, static files, and routing. This is an AOT compatible application, as is most of the P2PNet library.

### Operation

---

The `P2PBootstrap` project operates by providing several key functionalities:

1. **Peer Distribution**: The bootstrap server provides a series of HTTP endpoints, such as (`/api/Bootstrap/peers`) in order to distribute the list of known peers to new peers joining the network. This endpoint can operate in two modes:
   - **Trustless Mode**: Returns the list of known peers. Serves mostly to keep a list of peers that is mostly likely up to date. Trustless static bootstrap server cannot exert any degree of central control over the network - no commands can be issued that carry any weight or decision making on
   - **Authority Mode**: Requires the client to receive and store a public key from the bootstrap server before returning the peer list. Authority mode allows the static bootstrap server to exert varying degrees of central control over the network, which can be leveraged to ensure network security and serve other particular use cases.

   <p>
    <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/p2pbootstrap_sequence.png" alt="bootstrap chart">
    </p>
2. **Admin Terminal Integration**: The server integrates an admin terminal to easily execute commands.
3. **Encryption Service**: The server initializes an encryption service to handle secure communication. This includes generating and loading PGP keys, generating new keys, clear signing messages, generating hashes of objects, and more.
4. **Database Initialization**: The server initializes a local database to store necessary data. It ensures the database directory exists and sets up the required files.

### User Control Panel

---

The user control panel provides a web-based interface for managing the bootstrap server. In this web-based interface is a terminal for executing commands. It includes the following pages for easier management as well:

1. **Overview**: Displays an overview of the server's status and key metrics.
2. **Settings**: Allows users to modify server settings, such as encryption keys and database paths.
3. **Peers**: Displays the list of known peers and provides options to perform actions on them, such as disconnecting or blocking peers.

### Overview

---

Below is a broad simplification of the P2PBootstrap server implementation:

1. **Bootstrap Server Architecture**: Shows a broad architecture of the bootstrap server.
2. **Endpoints**: Illustrates the flow of endpoints, from initial request to response.

<p>
    <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/Bootstrap.png" width="500" height="325" alt="bootstrap chart">
</p>

**Note:** Bootstrap server still under construction 🏗️
