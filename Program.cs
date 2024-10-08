﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PRISM;
using PRISMDatabaseUtils;

namespace BionetPingTool
{
    internal static class Program
    {
        // Ignore Spelling: bionet, ip, yyyy-MM-dd, hh:mm tt

        private const string PROGRAM_DATE = "August 18, 2024";

        private const string DMS_CONNECTION_STRING = "Data Source=gigasax;Initial Catalog=DMS5;Integrated Security=SSPI;";
        private const string UPDATE_HOST_STATUS_PROCEDURE = "update_bionet_host_status_from_list";
        private const int PING_TIMEOUT_SECONDS = 5;

        private static CommandLineOptions mOptions;

        static int Main(string[] args)
        {
            var appName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
            var exeName = Path.GetFileName(AppUtils.GetAppPath());

            var parser = new CommandLineParser<CommandLineOptions>(appName, AppUtils.GetAppVersion(PROGRAM_DATE))
            {
                ProgramInfo = ConsoleMsgUtils.WrapParagraph(
                                  "This program contacts DMS to retrieve a list of Bionet computers (hosts). " +
                                  "It pings each computer to see which respond, then optionally contacts DMS with the list of active hosts.") + Environment.NewLine + Environment.NewLine +
                              ConsoleMsgUtils.WrapParagraph(
                                  "By default, it contacts DMS to retrieve the list of bionet hosts, then pings each one (appending suffix .bionet)." +
                                  "Alternatively, use /Manual to define a list of hosts to contact (.bionet is not auto-appended). " +
                                  "Or use /File to specify a text file listing one host per line (.bionet is not auto-appended)." +
                                  "The /File switch is useful when used in conjunction with script Export_DNS_Entries.ps1, " +
                                  "which can be run daily via a scheduled task to export all hosts and IP addresses " +
                                  "tracked by the bionet DNS server (Gigasax).") + Environment.NewLine + Environment.NewLine +
                              ConsoleMsgUtils.WrapParagraph(
                                  "When using /File, this program will still contact DMS to determine which hosts are inactive, " +
                                  "and it will skip those hosts.  Use /HideInactive to not see the names of the skipped hosts"),
                ContactInfo = "Program written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)" + Environment.NewLine +
                              "E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov" + Environment.NewLine +
                              "Website: https://github.com/PNNL-Comp-Mass-Spec/ or https://www.pnnl.gov/integrative-omics"
            };

            parser.UsageExamples.Add(
                "Program syntax:" + Environment.NewLine +
                exeName + Environment.NewLine +
                " [/Manual:Host1,Host2,Host3] [/File:HostListFile] [/HideInactive]" + Environment.NewLine +
                " [/Simulate] [/DB] [/DBAdd] [/NoDB]");

            var result = parser.ParseArgs(args);
            mOptions = result.ParsedResults;

            // Running with no command-line arguments specified is valid.
            if (args.Length > 0 && !result.Success)
            {
                if (parser.CreateParamFileProvided)
                {
                    return 0;
                }

                // Delay for 750 msec in case the user double-clicked this file from within Windows Explorer (or started the program via a shortcut)
                System.Threading.Thread.Sleep(750);
                return -1;
            }

            try
            {
                PingBionetComputers(mOptions.HostNameFile, mOptions.HostOverrideList);
                return 0;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error occurred in Program->Main", ex);
                return -1;
            }
        }

        /// <summary>
        /// Adds hosts to hostNames, assuring there are no duplicates
        /// </summary>
        /// <param name="hostNames"></param>
        /// <param name="hostsToAdd"></param>
        private static void AddHosts(ISet<string> hostNames, IEnumerable<string> hostsToAdd)
        {
            foreach (var host in hostsToAdd)
            {
                if (hostNames.Contains(host))
                {
                    ShowWarning("Skipping duplicate host " + host, 0);
                }
                else
                {
                    hostNames.Add(host);
                }
            }
        }

        /// <summary>
        /// Convert a boolean to 0 or 1
        /// </summary>
        /// <param name="value">Boolean value</param>
        /// <returns>1 if true, otherwise 0</returns>
        private static byte BoolToTinyInt(bool value)
        {
            return value ? (byte)1 : (byte)0;
        }

        /// <summary>
        /// Contact DMS to get the bionet host names
        /// </summary>
        /// <returns>
        /// Dictionary where keys are host names and values are true if the host is active and should be monitored,
        /// or false if it should not be monitored
        /// </returns>
        private static Dictionary<string, bool> GetBionetHosts(bool onlyActiveHosts = false)
        {
            try
            {
                // Keys are host names, values are True if the host has Active=1 in T_Bionet_Hosts, otherwise false
                var hostsTrackedByDMS = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

                ShowTimestampMessage("Retrieving names of Bionet computers");

                var connectionStringToUse = DbToolsFactory.AddApplicationNameToConnectionString(DMS_CONNECTION_STRING, "BionetPingTool");

                var dbTools = DbToolsFactory.GetDBTools(connectionStringToUse);

                const string sqlQuery = "SELECT host, ip, active " +
                                        "FROM V_Bionet_Hosts_Export " +
                                        "ORDER BY host";

                var success = dbTools.GetQueryResults(sqlQuery, out var results);

                if (!success)
                {
                    ShowErrorMessage("Error obtaining bionet hosts from V_Bionet_Hosts_Export");
                    return hostsTrackedByDMS;
                }

                foreach (var item in results)
                {
                    var hostName = item[0];

                    if (!int.TryParse(item[2], out var isActive))
                    {
                        ShowWarning(string.Format("Unable to convert {0} to an integer", item[2]));
                        isActive = 1;
                    }

                    var hostIsActive = (isActive > 0);

                    if (onlyActiveHosts && !hostIsActive)
                        continue;

                    if (hostsTrackedByDMS.ContainsKey(hostName))
                    {
                        ShowWarning("Skipping duplicate host " + hostName, 0);
                    }
                    else
                    {
                        hostsTrackedByDMS.Add(hostName, hostIsActive);
                    }
                }

                return hostsTrackedByDMS;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error in GetBionetHosts", ex);
                return new Dictionary<string, bool>();
            }
        }

        /// <summary>
        /// Ping the bionet computers tracked by DMS
        /// <remarks>When hostNameFile and explicitHostList are empty, obtains the bionet hosts by contacting DMS</remarks>
        /// <param name="hostNameFile">Text file with host names (optional)</param>
        /// <param name="explicitHostList">List of host names to contact (optional)</param>
        /// </summary>
        private static void PingBionetComputers(string hostNameFile, string explicitHostList)
        {
            var hostNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(explicitHostList))
            {
                AddHosts(hostNames, explicitHostList.Split(',').ToList());
            }

            if (!string.IsNullOrWhiteSpace(hostNameFile))
            {
                var hostsFromFile = ReadHostsFromFile(hostNameFile);
                AddHosts(hostNames, hostsFromFile);
            }

            PingBionetComputers(hostNames);

            Console.WriteLine();
            ShowTimestampMessage("Exiting");
        }

        /// <summary>
        /// Ping the bionet computers tracked by DMS
        /// <remarks>When explicitHostList is empty, obtains the bionet hosts by contacting DMS</remarks>
        /// <param name="explicitHostList">Optional list of host names to use instead of contacting DMS</param>
        /// </summary>
        private static void PingBionetComputers(IReadOnlyCollection<string> explicitHostList)
        {
            try
            {
                var hostsToPing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                List<string> skippedInactiveHosts;

                bool assureBionetSuffix;

                // ReSharper disable once MergeIntoPattern
                if (explicitHostList?.Count > 0)
                {
                    foreach (var hostName in explicitHostList)
                    {
                        hostsToPing.Add(hostName);
                    }
                    assureBionetSuffix = false;

                    if (!mOptions.DisableDatabase)
                    {
                        RemoveInactiveHosts(hostsToPing, out skippedInactiveHosts);
                    }
                    else
                    {
                        skippedInactiveHosts = new List<string>();
                    }
                }
                else
                {
                    if (mOptions.DisableDatabase)
                    {
                        ShowWarning("Because /NoDB was used, you must use /Manual or /File");
                        return;
                    }

                    // Query DMS for the host names
                    var hostsActiveInDMS = GetBionetHosts(true);

                    foreach (var hostName in hostsActiveInDMS.Keys)
                    {
                        hostsToPing.Add(hostName);
                    }

                    assureBionetSuffix = true;

                    skippedInactiveHosts = new List<string>();
                }

                // Ping the Hosts (uses Parallel.ForEach)
                var activeHosts = PingHostList(hostsToPing, mOptions.SimulatePing, assureBionetSuffix);

                if (!mOptions.UpdateDatabase)
                {
                    ShowSkippedHosts(skippedInactiveHosts);
                    return;
                }

                if (mOptions.DisableDatabase)
                {
                    ShowWarning("Ignoring /DB since /NoDB was used");
                    return;
                }

                if (mOptions.SimulatePing)
                {
                    var simulatedHosts = hostsToPing.ToDictionary(hostName => hostName, _ => string.Empty);

                    // Simulate updating the status for hosts
                    UpdateHostStatus(simulatedHosts, true);
                }
                else
                {
                    // Update the status for hosts that responded
                    UpdateHostStatus(activeHosts, false);
                }

                ShowSkippedHosts(skippedInactiveHosts);
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error in PingBionetComputers", ex);
            }
        }

        /// <summary>
        /// Ping the computers in hostNames
        /// </summary>
        /// <param name="hostNames">List of hosts to ping</param>
        /// <param name="simulatePing">True to simulate the ping, false to actually ping each computer</param>
        /// <param name="assureBionetSuffix">True when testing bionet computers; will make sure each name ends with ".bionet"</param>
        /// <returns>
        /// Dictionary of hosts that were successfully contacted (empty dictionary if simulatePing is true)
        /// Key is host name, value is IP
        /// </returns>
        private static Dictionary<string, string> PingHostList(
            ICollection<string> hostNames,
            bool simulatePing,
            bool assureBionetSuffix)
        {
            try
            {
                Console.WriteLine();

                if (simulatePing)
                    ShowTimestampMessage("Simulating ping");
                else
                    ShowTimestampMessage("Pinging computers");

                Console.WriteLine();

                // Keys are host name, value is IP (or empty string if no response)
                var activeHosts = new Dictionary<string, string>();

                Parallel.ForEach(hostNames, hostName =>
                {
                    var result = PingHost(hostName, simulatePing, assureBionetSuffix, out var ipAddress);

                    if (result && !simulatePing)
                        activeHosts.Add(hostName, ipAddress);
                });

                Console.WriteLine();
                Console.WriteLine("{0} {1} computers", simulatePing ? "Would ping" : "Pinged", hostNames.Count);
                Console.WriteLine();

                return activeHosts;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error in PingHostList", ex);
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Ping hostName
        /// </summary>
        /// <param name="hostName">Computer name</param>
        /// <param name="simulatePing">True to simulate the ping, false to actually ping each computer</param>
        /// <param name="assureBionetSuffix">True when testing bionet computers; will make sure each name ends with ".bionet"</param>
        /// <param name="ipAddress">IP Address</param>
        /// <returns>True if the host responds (returns false if simulatePing is true)</returns>
        private static bool PingHost(
            string hostName,
            bool simulatePing,
            bool assureBionetSuffix,
            out string ipAddress)
        {
            var hostNameWithSuffix = hostName.Trim();
            ipAddress = string.Empty;

            try
            {
                if (assureBionetSuffix && !hostNameWithSuffix.EndsWith(".bionet", StringComparison.OrdinalIgnoreCase))
                {
                    hostNameWithSuffix += ".bionet";
                }

                if (simulatePing)
                {
                    Console.WriteLine("Ping " + hostNameWithSuffix, 0);
                    return false;
                }

                var pingSender = new Ping();

                // Use the default TTL value of 128
                var options = new PingOptions();

                // Create a buffer of 32 bytes of data to be transmitted
                var buffer = Encoding.ASCII.GetBytes(new string('a', 32));

                const int timeout = PING_TIMEOUT_SECONDS * 1000;

                var reply = pingSender.Send(hostNameWithSuffix, timeout, buffer, options);

                if (reply is { Status: IPStatus.Success })
                {
                    var bufferSize = reply.Buffer.Length;
                    ipAddress = reply.Address.ToString();

                    Console.WriteLine("Reply for {0,-28} from {1,-15}: bytes={2} time {3}ms TTL={4}",
                                      hostNameWithSuffix, ipAddress, bufferSize, reply.RoundtripTime, reply.Options.Ttl);
                    return true;
                }

                ShowWarning(string.Format("Host timed out: {0} ", hostNameWithSuffix), 0);
            }
            catch (Exception ex)
            {
                if (ex.InnerException is SocketException socketEx)
                {
                    if (socketEx.SocketErrorCode == SocketError.HostNotFound)
                    {
                        ShowWarning(string.Format("Host not found: {0} ", hostNameWithSuffix));
                    }
                    else
                    {
                        ShowWarning(string.Format("Socket error for {0}: {1}", hostNameWithSuffix, socketEx.SocketErrorCode.ToString()));
                    }
                }
                else
                {
                    ShowErrorMessage("Error in PingHost for " + hostNameWithSuffix, ex.InnerException ?? ex);
                }
            }

            return false;
        }

        /// <summary>
        /// Read host names from a text file
        /// </summary>
        /// <param name="filePath"></param>
        private static IEnumerable<string> ReadHostsFromFile(string filePath)
        {
            var hostList = new List<string>();

            try
            {
                var hostFile = new FileInfo(filePath);

                if (!hostFile.Exists)
                {
                    ShowWarning("Warning, file not found: " + filePath);
                    return hostList;
                }

                var reGetHostName = new Regex(@"^[^ \t]+", RegexOptions.Compiled);

                using var reader = new StreamReader(new FileStream(hostFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

                while (!reader.EndOfStream)
                {
                    var dataLine = reader.ReadLine();

                    if (string.IsNullOrWhiteSpace(dataLine))
                        continue;

                    // Trim at the first whitespace character
                    var matches = reGetHostName.Matches(dataLine.Trim());

                    if (matches.Count == 0)
                        continue;

                    hostList.Add(matches[0].Value);
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error in ReadHostsFromFile", ex);
            }

            return hostList;
        }

        /// <summary>
        /// Contact DMS to find active bionet hosts
        /// Remove hosts from hostNames that are not active
        /// </summary>
        /// <param name="hostNames">Host names loaded from a file or specified via /Manual</param>
        /// <param name="skippedInactiveHosts">Inactive hosts that were removed from hostNames</param>
        private static void RemoveInactiveHosts(ICollection<string> hostNames, out List<string> skippedInactiveHosts)
        {
            // Keys are host names, values are True if the host has Active=1 in T_Bionet_Hosts, otherwise false
            var hostsTrackedByDMS = GetBionetHosts();

            skippedInactiveHosts = new List<string>();

            foreach (var host in hostNames)
            {
                var trimIndex = host.IndexOf(".bionet", StringComparison.OrdinalIgnoreCase);
                var hostNameNoSuffix = trimIndex > 0 ? host.Substring(0, trimIndex) : string.Empty;

                if (!hostsTrackedByDMS.TryGetValue(host, out var hostIsActive1) &
                    !hostsTrackedByDMS.TryGetValue(hostNameNoSuffix, out var hostIsActive2))
                {
                    // New host, unknown to DMS, we will ping it
                    continue;
                }

                if (hostIsActive1 || hostIsActive2)
                    continue;

                // Host is known to DMS, and is inactive
                skippedInactiveHosts.Add(host);
            }

            foreach (var hostToRemove in skippedInactiveHosts)
            {
                hostNames.Remove(hostToRemove);
            }
        }

        private static void ShowSkippedHosts(IReadOnlyCollection<string> skippedInactiveHosts)
        {
            switch (skippedInactiveHosts.Count)
            {
                case 0:
                    return;

                case 1:
                    ShowWarning("Skipped 1 inactive host: " + skippedInactiveHosts.First());
                    return;
            }

            ShowWarning(string.Format("Skipped {0} inactive hosts", skippedInactiveHosts.Count));

            if (mOptions.HideInactive)
                return;

            foreach (var skippedHost in skippedInactiveHosts)
            {
                ShowDebug(skippedHost, 0);
            }
        }

        /// <summary>
        /// Update DMS with the list of bionet hosts that responded to a ping
        /// </summary>
        /// <param name="activeHosts">Dictionary of host names and IP addresses</param>
        /// <param name="simulateCall">True to simulate the DB call</param>
        private static void UpdateHostStatus(Dictionary<string, string> activeHosts, bool simulateCall)
        {
            try
            {
                ShowTimestampMessage("Updating DMS");

                var serverType = DbToolsFactory.GetServerTypeFromConnectionString(DMS_CONNECTION_STRING);

                var connectionStringToUse = DbToolsFactory.AddApplicationNameToConnectionString(DMS_CONNECTION_STRING, "BionetPingTool");

                var dbTools = DbToolsFactory.GetDBTools(connectionStringToUse);

                // Procedure is update_bionet_host_status_from_list
                var cmd = dbTools.CreateCommand(UPDATE_HOST_STATUS_PROCEDURE, CommandType.StoredProcedure);

                var hostNamesAndIPs = new StringBuilder();

                foreach (var hostEntry in activeHosts)
                {
                    if (hostNamesAndIPs.Length > 0)
                        hostNamesAndIPs.Append(",");

                    if (string.IsNullOrWhiteSpace(hostEntry.Value))
                    {
                        hostNamesAndIPs.Append(hostEntry.Key);
                    }
                    else
                    {
                        hostNamesAndIPs.AppendFormat("{0}@{1}", hostEntry.Key, hostEntry.Value);
                    }
                }

                dbTools.AddParameter(cmd, "hostNames", SqlType.VarChar, 8000, hostNamesAndIPs.ToString());

                if (serverType == DbServerTypes.PostgreSQL)
                {
                    dbTools.AddParameter(cmd, "addMissingHosts", SqlType.Boolean).Value = mOptions.UpdateDatabaseAddNew;
                    dbTools.AddParameter(cmd, "infoOnly", SqlType.Boolean).Value = simulateCall;
                }
                else
                {
                    dbTools.AddParameter(cmd, "addMissingHosts", SqlType.TinyInt).Value = BoolToTinyInt(mOptions.UpdateDatabaseAddNew);
                    dbTools.AddParameter(cmd, "infoOnly", SqlType.TinyInt).Value = BoolToTinyInt(simulateCall);
                }

                var messageParam = dbTools.AddParameter(cmd, "message", SqlType.VarChar, 4000, ParameterDirection.InputOutput);
                var returnCodeParam = dbTools.AddParameter(cmd, "returnCode", SqlType.VarChar, 512, ParameterDirection.InputOutput);

                var resCode = dbTools.ExecuteSP(cmd, 1);

                var returnCode = DBToolsBase.GetReturnCode(returnCodeParam);

                var outputMessage = messageParam.Value.CastDBVal<string>();

                if (resCode == 0 && returnCode == 0)
                {
                    if (simulateCall)
                    {
                        // The message parameter has a vertical bar delimited list of host names
                        var hostList = outputMessage.Split('|');

                        if (hostList.Length < 1)
                        {
                            ShowWarning("Warning: " + UPDATE_HOST_STATUS_PROCEDURE + " returned an empty string in the message argument; cannot preview results");
                            return;
                        }

                        const string COLUMN_FORMAT_SPEC = "{0,-20} {1,-20}";

                        Console.WriteLine();
                        Console.WriteLine(COLUMN_FORMAT_SPEC, "Host",  "Info");

                        Console.WriteLine(COLUMN_FORMAT_SPEC, "-------------------", "-------------------");

                        foreach (var item in hostList)
                        {
                            var trimmedText = item.Trim();

                            if (trimmedText.StartsWith("Hosts to "))
                            {
                                // The first item is "Hosts to add or update" or "Hosts to update"; skip it
                                continue;
                            }

                            var spaceIndex = trimmedText.IndexOf(' ');

                            if (spaceIndex <= 0)
                            {
                                Console.WriteLine(COLUMN_FORMAT_SPEC, trimmedText, string.Empty);
                            }
                            else
                            {
                                var hostName = trimmedText.Substring(0, spaceIndex);
                                var info = trimmedText.Substring(spaceIndex + 1);

                                // Trim leading and trailing parentheses
                                var trimmedInfo = info.Replace("(", "").Replace(")", "").Trim();

                                Console.WriteLine(COLUMN_FORMAT_SPEC, hostName, trimmedInfo);
                            }
                        }

                        Console.WriteLine();
                        Console.WriteLine("Previewed update of " + activeHosts.Count + " hosts");
                    }
                    else
                    {
                        dbTools.ExecuteSP(cmd, 1);
                        Console.WriteLine("Update complete for " + activeHosts.Count + " hosts");
                    }

                    Console.WriteLine();
                    return;
                }

                if (resCode != 0 && returnCode == 0)
                {
                    ShowErrorMessage(string.Format(
                        "ExecuteSP() reported result code {0} calling {1}",
                        resCode, UPDATE_HOST_STATUS_PROCEDURE));

                    return;
                }

                var message = string.IsNullOrWhiteSpace(outputMessage) ? "Unknown error" : outputMessage;

                ShowErrorMessage(string.Format(
                    "Error updating bionet host status, {0} returned {1}, message: {2}",
                    UPDATE_HOST_STATUS_PROCEDURE, returnCodeParam.Value.CastDBVal<string>(), message));
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error in UpdateHostStatus", ex);
            }
        }

        private static void ShowDebug(string message, int emptyLinesBeforeMessage = 1)
        {
            ConsoleMsgUtils.ShowDebugCustom(message, emptyLinesBeforeMessage: emptyLinesBeforeMessage);
        }

        private static void ShowErrorMessage(string message, Exception ex = null)
        {
            ConsoleMsgUtils.ShowError(message, ex);
        }

        private static void ShowTimestampMessage(string message)
        {
            Console.WriteLine("{0:yyyy-MM-dd hh:mm:ss tt}: {1}", DateTime.Now, message);
        }

        private static void ShowWarning(string message, int emptyLinesBeforeMessage = 1)
        {
            ConsoleMsgUtils.ShowWarningCustom(message, emptyLinesBeforeMessage: emptyLinesBeforeMessage);
        }
    }
}
