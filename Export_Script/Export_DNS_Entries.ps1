# This script must be run from an admin-level powershell prompt
# Other WMI Classes: https://docs.microsoft.com/en-us/windows/win32/dns/dns-wmi-classes
Get-WmiObject `
   -Namespace Root\MicrosoftDNS `
   -Query "SELECT OwnerName, RecordData FROM MicrosoftDNS_AType WHERE ContainerName = 'bionet' AND OwnerName <> 'bionet'" | `
   Format-table -Property OwnerName, RecordData -HideTableHeaders -AutoSize