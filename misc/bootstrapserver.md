---
uid: p2pnetbootstrap
---
### Bootstrap 🤝

---

The application serves as a bootstrap node, providing an HTTP endpoint to distribute known peers to new peers joining the network. This setup ensures seamless peer discovery and initialization, enabling efficient and secure distributed data exchange within the peer network. By containerizing the application using Docker, deployment becomes significantly easier and makes quick VPS deployments easy. Additionally, the user control panel offers finer-grained controls over the network, including scaling and monitoring, which enhances the manageability and reliability of the peer network infrastructure.

### Initialization

---

The `P2PBootstrap` project initializes by setting up the necessary configurations and services required for the bootstrap server to operate. The main entry point is the `Program.cs` file, which configures the application and starts the web server.

1. **Configuration**: The application reads configuration settings from the `appsettings.json` file. This includes settings for encryption key directory, primary key names, database path, and other essential configurations. In order to ease the deployment process for containerized instances, all configuration settings in the configuration file `appsettings.json` have an environmental variable equivalent that can be used. In order to tell the application to use these, the environmental variable `CONTAINERIZED_ENVIRONMENT` must be set to to `true`. In the table below, you can see each configuration key alongside its corresponding environment variable name and a brief description of its purpose.


| AppSettings Key                          | Environment Variable | Description                                                                                                                                                 |
| ------------------------------------------ | ---------------------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Configuration:KeysDirectory              | KEYS_DIRECTORY       | Specifies the directory where encryption keys are stored.                                                                                                   |
| Configuration:BootstrapMode              | BOOTSTRAP_MODE       | Determines whether the bootstrap server runs in Authority or Trustless mode.                                                                                |
| Configuration:AuthorityKey:PublicKey     | PUBLIC_KEY_PATH      | The filename (or path, relative to the base directory) for the public key used by the bootstrap server.                                                     |
| Configuration:AuthorityKey:PrivateKey    | PRIVATE_KEY_PATH     | The filename (or path) for the private key used by the bootstrap server.                                                                                    |
| Configuration:NetworkName                | NETWORK_NAME         | The alias for the P2P network, used to distinguish this network from others. This is also used and referenced within encryption logic for some keys.        |
| Configuration:OptionalEndpoints:PublicIP | ENDPOINT_PUBLICIP    | Server will serve a GET endpoint`/api/Bootstrap/publicip` which can be used by clients to retrieve their public face IPv4 address                           |
| Configuration:Identifier                 | IDENTIFIER           | The peer network Identifier value upon initialization. This is required for authority mode operations and cannot be blank unless running in trustless mode. |
| Database:DbFileName                      | DB_FILENAME          | The filename for the local database file storing bootstrap server data.                                                                                     |

When running in a containerized environment, the configuration management first attempts to retrieve these values from their respective environment variables (e.g., "KEYS_DIRECTORY" for the keys folder). If an environment variable isn't set, it falls back to the values specified in appsettings.json. This design allows for flexible configuration management during deployment, especially in environments like Docker containers where runtime settings might differ from development.

This side-by-side mapping ensures that you can maintain consistent configuration across different deployment scenarios with minimal code changes.

2. **Logging**: Logging is handled using the `ConsoleDebugger` package.
3. **Web Server Setup**: The application uses ASP.NET Core to set up the web server. It configures the HTTP request pipeline, enabling default files, static files, and routing. This is an AOT compatible application, as is most of the P2PNet library.

### Operation

---

The `P2PBootstrap` project operates by providing several key functionalities:

1. **Peer Distribution**: The bootstrap server provides a series of HTTP endpoints, such as (`/api/Bootstrap/peers`) in order to distribute the list of known peers to new peers joining the network. This endpoint can operate in two modes:

   - **Trustless Mode**: Returns the list of known peers. Serves mostly to keep a list of peers that is most likely up to date. Trustless static bootstrap server cannot exert any degree of central control over the network - no commands can be issued that carry any weight or decision making.
   - **Authority Mode**: Requires the client to receive and store a public key from the bootstrap server before returning the peer list. Authority mode allows the static bootstrap server to exert varying degrees of central control over the network, which can be leveraged to ensure network security and serve other particular use cases.

   <p>
    <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/p2pbootstrap_sequence.png" alt="bootstrap chart">
    </p>
2. **Admin Terminal Integration**: The server integrates an admin terminal to easily execute commands. This is accessible upon logging into the control panel via the web portal page.
3. **Encryption Service**: The server initializes an encryption service to handle secure communication. This includes generating and loading PGP keys, generating new keys, clear signing messages, generating hashes of objects, and more.

   - **⚠️IMPORTANT⚠️**: In the appsettings.json configuration file, the `NetworkName` field is used as an authorization point of reference. Change the `NetworkName` field after an initialization under a different value will render any previously generated PGP keys unusable. You will either have to purge all previous keys, or revert the `NetworkName` value back. This *can* break functionality, so be careful.
   - **Note**: During startup, the server verifies whether the passphrase for its PGP keys is configured. If the passphrase is missing or invalid, the server logs a message advising the operator to supply one. This step ensures that key-based encryption and clear signing remain functional. If the passphrase needs resetting, administrators can update the relevant environment variable or appsettings configuration, and then restart the server to apply the changes.
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

### Bootstrap Channels

The **BootstrapChannel** class defines the core routines for communicating with a bootstrap server. It is able to apply these routines in both *authority* and *trustless* modes to help establish a node’s identity and retrieve known peers.

1. **Core Responsibilities**  
   - **Initial Handshake**: The `InitialBootstrapHandler` delegate determines how the channel attempts its first connection. In authority mode, the base logic validates a public key and processes a peer list. In trustless mode, the peer list is simply loaded without further validation.  
   - **Heartbeat Routine**: A periodic heartbeat signals the node’s presence to the bootstrap server. The default behavior is controlled by the `HeartbeatOutHandler` delegate, which sends an outbound heartbeat on a timer. The bootstrap server will respond with `NetworkTasks` to be handled by the `NetworkTaskhandler`.
   - **Error Handling**: When the server indicates a failure (e.g., due to invalid packets or unreachable endpoints), the `HandleErrorResponse` delegate processes and logs the issue.  

2. **Mode Differences**  
   - **Trustless Mode**: Returns a straightforward peer list, facilitating faster onboarding for new peers. Minimal validation is performed, suitable for open or LAN-oriented networks.  
   - **Authority Mode**: Incorporates a public key check, verifies network tasks (e.g., hashing and signatures), and may lock the first recognized authority to prevent unauthorized servers from impersonation.  

3. **Delegate Customization**  
   Each of the public delegates in `BootstrapChannelBase` can be replaced to modify the default behaviors (e.g., injecting custom logging or retry logic). For instance, the `InitialBootstrapHandler` can be overwritten to integrate special authentication steps before loading the peer list, while the `IsValidNetworkHash` delegate allows you to plug in a custom hash or signature validation routine.

4. **Sample Usage Outline**  
   4. **Sample Usage Outline**  
   - Instantiate a `BootstrapChannel`  
     • via the simple constructor, e.g. `new BootstrapChannel("https://bootstrap.example.com", true)`  
     • or by supplying a `BootstrapChannelConnectionOptions` instance for advanced settings, e.g.  
       ```csharp
       var options = new BootstrapChannelConnectionOptions(
           new Uri("https://bootstrap.example.com"),
           isAuthorityMode: true,
           bootstrapPeer: null);
       var channel = new BootstrapChannel(options);
       ```  
   - Either A) Call `channel.OpenBootstrapChannel()` to trigger the initial handshake, or B) Call `AddBootstrapChannel(channel)` and when ready to open all bootstrap channels, call `StartBootstrapConnections();`. In authority mode, the server’s public key is validated and the peer list is processed; in trustless mode, the peer list is loaded directly.  
   - Control the bootstrap channel stream with `StartBootstrapChannelStream()` / `StopBootstrapChannelStream()`, or replace the `HandleIncomingStreamTask` delegate to customize how incoming network tasks are processed.  
   - When overriding delegates (e.g., `InitialBootstrapHandler` or `HandleIncomingStreamTask`), remember to maintain the essential tasks of each step—handshake, data retrieval, or error checks—to keep the channel stable.

These classes ensure that each node communicates with the bootstrap server in a consistent manner while still allowing extensibility for custom security or operational requirements.

**Note:** Bootstrap server still under construction 🏗️
