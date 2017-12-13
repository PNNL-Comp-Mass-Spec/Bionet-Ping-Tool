@echo off
echo Note: this script must be run from an admin-level command prompt
c:
cd C:\DMS_Programs\BionetPingTool

echo.
echo Retrieving DNS entries using WMI
powershell .\Export_DNS_Entries.ps1 > DNS_Entries.txt

echo.
echo Validating DNS_Entries.txt
powershell .\Validate_DNS_Entries.ps1
