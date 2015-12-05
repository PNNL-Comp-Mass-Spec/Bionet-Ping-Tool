# This script must be run from an admin-level powershell prompt
Get-WmiObject `
   -Namespace Root\MicrosoftDNS `
   -Query "SELECT OwnerName, RecordData FROM MicrosoftDNS_AType WHERE ContainerName = 'bionet' AND OwnerName <> 'bionet'" | `
   Format-table -Property OwnerName, RecordData -HideTableHeaders