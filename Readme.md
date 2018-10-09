This program contacts DMS to retrieve a list of Bionet computers (hosts).
It pings each computer to see which respond, then optionally contacts DMS with the list of active hosts.

== Program syntax ==

BionetPingTool.exe [/Manual:Host1,Host2,Host3] [/File:HostListFile] [/HideInactive] [/Simulate] [/DB] [/DBAdd] [/NoDB]

By default contacts DMS to retrieve the list of bionet hosts, then pings each one (appending suffix .bionet).
Alternatively, use /Manual to define a list of hosts to contact (.bionet is not auto-appended).
Or use /File to specify a text file listing one host per line (.bionet is not auto-appended).

The /File switch is useful when used in conjunction with script Export_DNS_Entries.ps1,
which can be run daily via a scheduled task to export all hosts and IP addresses 
tracked by the bionet DNS server (Gigasax).

When using /File, the program will still contact DMS to determine which hosts are inactive,
and it will skip those hosts.  Use /HideInactive to not see the names of the inactive hosts"

Use /Simulate to simulate the ping

Use /DB to store the results in the database (preview if /Simulate is used)
Use /DBAdd to add new (unknown) hosts to the database

Use /NoDB to disable the use of the database, thus requiring that /Manual or /File be used.
In addition, will not contact DMS to find inactive hosts.

-------------------------------------------------------------------------------
Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) in 2015

E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov
Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/
-------------------------------------------------------------------------------