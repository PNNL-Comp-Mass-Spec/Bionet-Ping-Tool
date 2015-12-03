This program contacts DMS to retrieve a list of Bionet computers (hosts)
It pings each computer to see which respond, then optionally contacts DMS with the list of active hosts

== Program syntax ==

BionetPingTool.exe [/Manual:Host1,Host2,Host3] [/Simulate] [/DB]

By default contacts DMS to retrieve the list of bionet hosts, then pings each one (appending suffix .bionet)
Alternatively, use /Manual to define a list of hosts to contact

Use /Simulate to simulate the ping

Use /DB to store the results in the database (ignored if /Simulate is used)

-------------------------------------------------------------------------------
Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) in 2015

E-mail: matthew.monroe@pnnl.gov or matt@alchemistmatt.com
Website: http://panomics.pnnl.gov/ or http://omics.pnl.gov or http://www.sysbio.org/resources/staff/
-------------------------------------------------------------------------------