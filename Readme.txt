This program contacts DMS to retrieve a list of Bionet computers (hosts)
It pings each computer to see which respond, then optionally contacts DMS with the list of active hosts

== Program syntax ==

BionetPingTool.exe [/Manual:Host1,Host2,Host3] [/File:HostListFile] [/Simulate] [/DB] [/DBAdd]

By default contacts DMS to retrieve the list of bionet hosts, then pings each one (appending suffix .bionet)
Alternatively, use /Manual to define a list of hosts to contact (.bionet is not auto-appended)
Or use /File to specify a text file listing one host per line (.bionet is not auto-appended)

Use /Simulate to simulate the ping

Use /DB to store the results in the database (preview if /Simulate is used)
Use /DBAdd to add new (unknown) hosts to the database

-------------------------------------------------------------------------------
Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) in 2015

E-mail: matthew.monroe@pnnl.gov or matt@alchemistmatt.com
Website: http://panomics.pnnl.gov/ or http://omics.pnl.gov or http://www.sysbio.org/resources/staff/
-------------------------------------------------------------------------------