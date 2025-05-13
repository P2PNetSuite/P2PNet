$natName = "nat"
$switchName = "nat"
$subnet = "192.168.100.0/24"
$ipPattern = "192.168.100.*"

# remove the NAT network
$nat = Get-NetNat -Name $natName -ErrorAction SilentlyContinue
if ($nat) {
    Write-Output "Removing NAT Network '$natName'..."
    Remove-NetNat -Name $natName -Confirm:$false
    Write-Output "NAT Network '$natName' deleted."
} else {
    Write-Output "NAT Network '$natName' not found."
}

# remove the IP addresses from adapter
$adapter = Get-NetAdapter | Where-Object { $_.InterfaceDescription -like "*$switchName*" }
if ($adapter) {
    $interfaceAlias = $adapter.Name
    Write-Output "Removing IP addresses on interface '$interfaceAlias' matching '$ipPattern'..."
    $ips = Get-NetIPAddress -InterfaceAlias $interfaceAlias -ErrorAction SilentlyContinue | 
           Where-Object { $_.IPAddress -like $ipPattern }
    foreach ($ip in $ips) {
        Remove-NetIPAddress -InterfaceAlias $interfaceAlias -IPAddress $ip.IPAddress -Confirm:$false
    }
    Write-Output "IP addresses removed from '$interfaceAlias'."
} else {
    Write-Output "Hyper-V adapter for switch '$switchName' not found."
}

# remove the virtual switch
if (Get-VMSwitch -Name $switchName -ErrorAction SilentlyContinue) {
    Write-Output "Removing VMSwitch '$switchName'..."
    Remove-VMSwitch -Name $switchName -Force
    Write-Output "VMSwitch '$switchName' deleted."
} else {
    Write-Output "VMSwitch '$switchName' not found."
}
