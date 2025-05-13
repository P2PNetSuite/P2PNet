$switchName = "nat"
$natName = "nat"
$adapterIP = "192.168.100.1"
$prefixLength = 24
$subnetPrefix = "192.168.100.0/24"

Write-Output "Be sure to check your system's IP configuration. Default is 192.168.... some systems may need 172.16... ect"

# create internal virtual switch
New-VMSwitch -Name $switchName -SwitchType Internal

# find adapter associated with virtual switch
$adapter = Get-NetAdapter | Where-Object { $_.Name -like "vEthernet ($switchName)" }
if ($null -eq $adapter) {
    Write-Output "vEthernet adapter for switch '$switchName' not found."
    exit
}

# assign the IP address
New-NetIPAddress -InterfaceAlias $adapter.Name -IPAddress $adapterIP -PrefixLength $prefixLength

# create a NAT network
New-NetNat -Name $natName -InternalIPInterfaceAddressPrefix $subnetPrefix

Write-Output "vEthernet adapter '$adapter.Name' created. IP $adapterIP assigned and NAT network '$natName' configured."
