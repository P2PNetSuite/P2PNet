---
uid: testing
---

### Testing

---

This portion of documentation outlines different methods of testing, and different test types with varying levels of setup complexity.

**LAN Tests**


* Quick LAN test - quickly setup a peer connection using the ExampleApplication container and a local instance started from Visual Studio

* Multi-peer LAN test - runs a Powershell script which automates a Docker Compose setup to simulate 3 peers establishing connection over NAT

**WAN Tests**


* Bootstrap test - the current ExampleApplication has been modified to establish a bootstrap connection with the official P2PNet boostrap server example instance... a more complex test scenario is underway

* Widescan test - *in progress*

---

### Quick LAN test

1. In the ExampleApplication, startup the Docker container from Visual Studio.
   <p>
        <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/screenshots/quick_lan_test_1.png"  >
    </p>
2. Once the container has verifiably started, go ahead and stop the container (Ctrl + C or from Docker dashboard)
   <p>
        <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/screenshots/quick_lan_test_2.png"  >
    </p>
3. With the container still stopped, go ahead and run a regular instance of the ExampleApplication from Visual Studio (Ctrl + F5)
   <p>
        <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/screenshots/quick_lan_test_3.png"  >
    </p>
4. Ensure the regular instance of the ExampleApplication starts up. Leave this open.
   <p>
        <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/screenshots/quick_lan_test_4.png"  >
    </p>
5. With the regular instance running, go into your Docker dashboard and go to Containers. Proceed to startup the ExampleApplication container (simply click the Start button). Go to the Exec tab and you should see the Windows terminal open. Type `ExampleApplication.exe` then hit enter.
   <p>
        <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/screenshots/quick_lan_test_5.png"  >
    </p>
6. You should now be able to see the application start up in the Docker container. From this point, you have two instances of the ExampleApplication running which you should be able to observe side-by-side. Observe and see if the default trust protocol shows back and forth communication between the peers.
   <p>
        <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/screenshots/quick_lan_test_6.png"  >
    </p>

### Multi-peer LAN test

1. Open the solution in Visual Studio. It is advised to Clean Solution and then to do a fresh Build Solution. Then, proceed to run the `LAN_Test.ps1` script that should be in the root directory
   <p>
        <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/screenshots/multipeer_lan_test_1.png">
    </p>
2. The Docker Compose build process may take a minute or two. This is normal.
   <p>
        <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/screenshots/multipeer_lan_test_2.png">
    </p>
3. The build process creates a new NAT network and 3 individual peer containers. This is automated through the Powershell script.
   <p>
        <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/screenshots/multipeer_lan_test_3.png">
    </p>
4. You should then be able to observe the multi-peer LAN test output within the Developer Powershell console in Visual Studio. It will typically look something like this:
   <p>
        <img src="https://raw.githubusercontent.com/p2pnetsuite/P2PNet/refs/heads/master/misc/screenshots/multipeer_lan_test_4.png">
    </p>
