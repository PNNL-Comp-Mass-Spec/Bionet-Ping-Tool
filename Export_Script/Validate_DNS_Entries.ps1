# Examine file DNS_Entries.txt to confirm that it has valid entries and does not have and InvalidOperation exception

$DNSEntriesFile = ".\DNS_Entries.txt"

if (-not (Test-Path $DNSEntriesFile)) {
	Write-Host "File not found: $DNSEntriesFile" -foregroundcolor "magenta"
	return
}

foreach($line in Get-Content $DNSEntriesFile ) {
	if ($line -like '*InvalidOperation*') { 
		Write-Host "Be sure to run this from an administrative level Powershell prompt" -foregroundcolor "yellow"
		Write-Host "Error: $line" -foregroundcolor "magenta"
		return
	}
}

Write-Host "Validated $DNSEntriesFile"
