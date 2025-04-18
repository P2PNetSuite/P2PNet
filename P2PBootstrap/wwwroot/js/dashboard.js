// Check if the user is logged in
if (sessionStorage.getItem('loggedIn') !== 'true') {
    // Redirect to login page if not logged in
    window.location.href = 'index.html';
}

// Logout button functionality
document.getElementById('logout-button').addEventListener('click', function () {
    // Clear login state
    sessionStorage.removeItem('loggedIn');

    // Redirect to login page
    window.location.href = 'index.html';
});

// Save button functionality
document.getElementById('save-button').addEventListener('click', function () {
    // Implement save functionality here
    alert('Settings saved successfully.');
});

// Navigation functionality
function navigateTo(section) {
    var mainContent = document.getElementById('main-content');

    // Clear existing content
    mainContent.innerHTML = '';

    // Load content based on the selected section
    if (section === 'overview') {
        mainContent.innerHTML = `
            <h2>Overview</h2>
            <p>This is the overview section.</p>
        `;
    } else if (section === 'settings') {
        mainContent.innerHTML = `
            <h2>Settings</h2>
            <p>Modify your settings here.</p>
            <!-- Add settings form or inputs -->
        `;
    } else if (section === 'peers') {
        mainContent.innerHTML = `
            <h2>Peers</h2>
            <table id="peers-table">
                <thead>
                    <tr>
                        <th>IP Address</th>
                        <th>Action</th>
                    </tr>
                </thead>
                <tbody>
                    <!-- Peers will be populated here -->
                </tbody>
            </table>
        `;
        loadPeers();
    } else {
        mainContent.innerHTML = `
            <h2>Welcome to the Bootstrap Server Dashboard</h2>
            <p>Select an option from the navigation menu to get started.</p>
        `;
    }
}

// Function to load peers and populate the table
function loadPeers() {
    fetch('/api/peers')
        .then(response => response.json())
        .then(data => {
            var peers = data;

            var tbody = document.querySelector('#peers-table tbody');

            tbody.innerHTML = ''; // Clear existing rows

            if (!peers || peers.length === 0) {
                // Render table with header and one empty row
                var row = document.createElement('tr');
                row.classList.add('empty-row');

                var emptyCell = document.createElement('td');
                emptyCell.setAttribute('colspan', '2');
                emptyCell.textContent = 'No peers available.';
                row.appendChild(emptyCell);

                tbody.appendChild(row);
            } else {
                peers.forEach((peer, index) => {
                    var row = document.createElement('tr');

                    // IP Address Cell
                    var ipCell = document.createElement('td');
                    ipCell.textContent = peer.address;
                    row.appendChild(ipCell);

                    // Action Cell
                    var actionCell = document.createElement('td');

                    // Dropdown Menu
                    var select = document.createElement('select');
                    select.id = `action-select-${index}`;
                    var optionDisconnect = document.createElement('option');
                    optionDisconnect.value = 'disconnect';
                    optionDisconnect.textContent = 'Disconnect';
                    var optionBlock = document.createElement('option');
                    optionBlock.value = 'block';
                    optionBlock.textContent = 'Block';
                    select.appendChild(optionDisconnect);
                    select.appendChild(optionBlock);
                    actionCell.appendChild(select);

                    // Save Button
                    var saveButton = document.createElement('button');
                    saveButton.textContent = 'Save';
                    saveButton.onclick = function () {
                        var selectedAction = select.value;
                        var peerAddress = peer.address;
                        performActionOnPeer(peerAddress, selectedAction);
                    };
                    actionCell.appendChild(saveButton);

                    row.appendChild(actionCell);

                    tbody.appendChild(row);
                });
            }
        })
        .catch(error => {
            console.error('Error fetching peers:', error);
        });
}


// In dashboard.js
// Terminal functionality
document.addEventListener('DOMContentLoaded', async () => {
    const terminalContainer = document.getElementById('terminal-container');
    const term = new Terminal({ cursorBlink: true });
    term.open(terminalContainer);

    // Prompt
    term.write('> ');
    let lineBuffer = '';

    term.onKey(e => {
        if (e.key == '\r') {
            // Send command to the server
            fetch('/api/parser/input', {
                method: 'PUT',
                headers: {
                    'Content-Type': 'text/plain'
                },
                body: lineBuffer
            });

            term.writeln('');              // Move to a new line
            term.write('> ');              // Reprompt

            lineBuffer = ''; // Clear buffer
        }
        // Check for Backspace
        else if (e.key === '\u007F') {
            if (lineBuffer.length > 0) {
                // Move cursor back, clear char, move cursor back again
                term.write('\b\b');
                lineBuffer = lineBuffer.slice(0, -1);
            }
        }
        else {
            lineBuffer += e.key;
            term.write(e.key);
        }
    });

    // Polling function to get output from the server
    async function pollOutput() {
        try {
            const response = await fetch('/api/parser/output');
            if (response.ok) {
                const output = await response.text();
                if (output) {
                    term.writeln(output);
                    term.write('> ');
                }
            }
        } catch (error) {
            console.error('Error fetching output:', error);
        }
    }

    // Start polling every 100ms
    setInterval(pollOutput, 100);
});

// Function to perform action on peer
function performActionOnPeer(peerAddress, action) {
    fetch('/api/managepeer', {
        method: 'PUT',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({ peerAddress: peerAddress, action: action })
    })
        .then(response => response.text())
        .then(message => {
            alert(message);
            // optionally reload of the peers list if necessary
            loadPeers();
        })
        .catch(error => {
            console.error('Error performing peer action:', error);
        });
}


// Initialize the default section
navigateTo('overview');
