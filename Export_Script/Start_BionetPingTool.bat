rem Note that file DNS_Entries.txt is created by Export_DNS_Entries.bat
rem which is called via a scheduled task every 8 hours

c:
cd C:\DMS_Programs\BionetPingTool
BionetPingTool.exe /File:DNS_Entries.txt /DB /DBAdd > BionetPingTool_Log.txt
