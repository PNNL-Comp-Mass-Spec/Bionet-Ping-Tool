using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Data.SqlClient;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PRISM;

namespace BionetPingTool
{
    internal class Program
    {
        private const string PROGRAM_DATE = "October 9, 2018";

        private const string DMS_CONNECTION_STRING = "Data Source=gigasax;Initial Catalog=DMS5;Integrated Security=SSPI;";
        private const string UPDATE_HOST_STATUS_PROCEDURE = "UpdateBionetHostStatusFromList";
        private const int PING_TIMEOUT_SECONDS = 5;

        private static string mHostNameFile;
        private static string mHostOverrideList;

        private static bool mSimulatePing;
        private static bool mUpdateDatabase;
        private static bool mUpdateDatabaseAddNew;

        static int Main()
        {
            var commandLineParser = new clsParseCommandLine();

            mHostNameFile = string.Empty;
            mHostOverrideList = string.Empty;

            mSimulatePing = false;
            mUpdateDatabase = false;
            mUpdateDatabaseAddNew = false;

            try
            {
                bool success;

                if (commandLineParser.ParseCommandLine())
                {
                    success = SetOptionsUsingCommandLineParameters(commandLineParser);
                }
                else
                {
                    success = false;
                }

                if (commandLineParser.NeedToShowHelp || !success)
                {
                    ShowProgramHelp();
                    return -1;
                }

                PingBionetComputers(mHostNameFile, mHostOverrideList);

            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error occurred in Program->Main", ex);
                return -1;
            }

            return 0;
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
        /// <returns>List of host names</returns>
        private static ICollection<string> GetBionetHosts()
        {

            try
            {
                var hostNames = new List<string>();

                Console.WriteLine("Retrieving names of Bionet computers at " + GetTimeStamp());

                using (var cn = new SqlConnection(DMS_CONNECTION_STRING))
                {
                    cn.Open();

                    var sqlQuery =
                        "SELECT Host, IP " +
                        "FROM V_Bionet_Hosts_Export " +
                        "ORDER BY Host";

                    using (var cmd = new SqlCommand(sqlQuery, cn))
                    {
                        cmd.CommandTimeout = 30;
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var hostName = reader.GetString(0);
                                hostNames.Add(hostName);
                            }
                        }
                    }

                }
                Console.WriteLine();

                return hostNames;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error in GetBionetHosts", ex);
                return new List<string>();
            }
        }

        private static string GetColumnValue(IDataRecord reader, int fieldIndex, int maxWidth, bool isDate = false)
        {
            if (fieldIndex < 0)
                return string.Empty;

            if (isDate)
            {
                if (reader.IsDBNull(fieldIndex))
                    return string.Empty;

                var dateValue = reader.GetDateTime(fieldIndex);

                if (maxWidth < 11)
                    return dateValue.ToString("yyyy-MM-dd");

                return dateValue.ToString("yyyy-MM-dd hh:mm tt");
            }

            var value = reader.GetString(fieldIndex);
            if (value.Length <= maxWidth)
                return value;

            return value.Substring(0, maxWidth);

        }

        /// <summary>
        /// Determine the column index for each field name
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="fieldNames"></param>
        /// <returns>Dictionary mapping field name to column index (-1 if field not found)</returns>
        private static Dictionary<string, int> GetFieldMapping(IDataRecord reader, IEnumerable<string> fieldNames)
        {
            var fieldMapping = new Dictionary<string, int>();

            foreach (var fieldName in fieldNames)
            {
                try
                {
                    var fieldIndex = reader.GetOrdinal(fieldName);
                    fieldMapping.Add(fieldName, fieldIndex);
                }
                catch
                {
                    // Field not found
                    fieldMapping.Add(fieldName, -1);
                }
            }

            return fieldMapping;

        }

        private static string GetTimeStamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt");
        }

        /// <summary>
        /// Ping the bionet computers tracked by DMS
        /// <param name="hostNameFile">Text file with host names (optional)</param>
        /// <param name="explicitHostList">List of host names to contact (optional)</param>
        /// </summary>
        /// <remarks>When hostNameFile and explicitHostList are empty, obtains the bionet hosts by contacting DMS</remarks>
        private static void PingBionetComputers(string hostNameFile, string explicitHostList)
        {
            List<string> hostNames;

            if (string.IsNullOrWhiteSpace(explicitHostList))
                hostNames = new List<string>();
            else
            {
                hostNames= explicitHostList.Split(',').ToList();
            }


            if (!string.IsNullOrWhiteSpace(hostNameFile))
            {
                hostNames.AddRange(ReadHostsFromFile(hostNameFile));
            }

            PingBionetComputers(hostNames);

            Console.WriteLine(GetTimeStamp() + ": Exiting");
        }

        /// <summary>
        /// Ping the bionet computers tracked by DMS
        /// <param name="explicitHostList">Optional list of host names to use instead of contacting DMS</param>
        /// </summary>
        /// <remarks>When explicitHostList is empty, obtains the bionet hosts by contacting DMS</remarks>
        private static void PingBionetComputers(
            ICollection<string> explicitHostList)
        {
            try
            {

                ICollection<string> hostNames;
                var assureBionetSuffix = false;

                if (explicitHostList != null && explicitHostList.Count > 0)
                    hostNames = explicitHostList;
                else
                {
                    // Query DMS for the host names
                    hostNames = GetBionetHosts();
                    assureBionetSuffix = true;
                }

                // Ping the Hosts
                var activeHosts = PingHostList(hostNames, mSimulatePing, assureBionetSuffix);

                if (mUpdateDatabase)
                {

                    if (mSimulatePing)
                    {
                        var simulatedHosts = hostNames.ToDictionary(hostName => hostName, ip => string.Empty);

                        // Simulate updating the status for hosts
                        UpdateHostStatus(simulatedHosts, true);
                    }
                    else
                    {
                        // Update the status for hosts that responded
                        UpdateHostStatus(activeHosts, false);
                    }

                }

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
            IEnumerable<string> hostNames,
            bool simulatePing,
            bool assureBionetSuffix)
        {
            try
            {

                if (simulatePing)
                    Console.WriteLine(GetTimeStamp() + ": Simulating ping");
                else
                    Console.WriteLine(GetTimeStamp() + ": Contacting computers");

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
                if (assureBionetSuffix && !hostNameWithSuffix.ToLower().EndsWith(".bionet"))
                {
                    hostNameWithSuffix = hostNameWithSuffix + ".bionet";
                }

                if (simulatePing)
                {
                    Console.WriteLine("Ping " + hostNameWithSuffix);
                    return false;
                }

                var pingSender = new Ping();

                // Use the default TTL value of 128
                var options = new PingOptions();

                // Create a buffer of 32 bytes of data to be transmitted
                var buffer = Encoding.ASCII.GetBytes(new string('a', 32));
                var timeout = PING_TIMEOUT_SECONDS * 1000;
                var reply = pingSender.Send(hostNameWithSuffix, timeout, buffer, options);

                if (reply != null && reply.Status == IPStatus.Success)
                {
                    var bufferSize = reply.Buffer.Length;
                    ipAddress = reply.Address.ToString();

                    Console.WriteLine("Reply for {0,-28} from {1,-15}: bytes={2} time {3}ms TTL={4}", hostNameWithSuffix, ipAddress, bufferSize, reply.RoundtripTime, reply.Options.Ttl);
                    return true;
                }

                Console.WriteLine("Host timed out: {0} ", hostNameWithSuffix);

            }
            catch (Exception ex)
            {
                if (ex.InnerException is SocketException socketEx)
                {
                    if (socketEx.SocketErrorCode == SocketError.HostNotFound)
                        Console.WriteLine("Host not found: {0} ", hostNameWithSuffix);
                    else
                    {
                        Console.WriteLine("Socket error for {0}: {1}", hostNameWithSuffix, socketEx.SocketErrorCode.ToString());
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
        /// <returns></returns>
        private static IEnumerable<string> ReadHostsFromFile(string filePath)
        {
            var hostList = new List<string>();

            try
            {
                var fiHostFile = new FileInfo(filePath);
                if (!fiHostFile.Exists)
                {
                    ShowWarningMessage("Warning, file not found: " + filePath);
                    return hostList;
                }

                var reGetHostName = new Regex(@"^[^ \t]+", RegexOptions.Compiled);

                using (var reader = new StreamReader(new FileStream(fiHostFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
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
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error in ReadHostsFromFile", ex);
            }

            return hostList;

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

                Console.WriteLine(GetTimeStamp() + ": Updating DMS");

                using (var cn = new SqlConnection(DMS_CONNECTION_STRING))
                {
                    cn.Open();

                    // Procedure is UpdateBionetHostStatusFromList
                    using (var cmd = new SqlCommand(UPDATE_HOST_STATUS_PROCEDURE, cn))
                    {
                        cmd.CommandTimeout = 30;
                        cmd.CommandType = CommandType.StoredProcedure;

                        var paramHostNames = cmd.Parameters.Add(new SqlParameter("hostNames", SqlDbType.VarChar, 8000));

                        var hostNamesAndIPs = new StringBuilder();

                        foreach (var hostEntry in activeHosts)
                        {
                            if (hostNamesAndIPs.Length > 0)
                                hostNamesAndIPs.Append(",");

                            if (string.IsNullOrWhiteSpace(hostEntry.Value))
                                hostNamesAndIPs.Append(hostEntry.Key);
                            else
                            {
                                hostNamesAndIPs.Append(hostEntry.Key + "@" + hostEntry.Value);
                            }
                        }

                        paramHostNames.Value = hostNamesAndIPs.ToString();

                        var paramAddMissing = cmd.Parameters.Add(new SqlParameter("addMissingHosts", SqlDbType.TinyInt));
                        paramAddMissing.Value = BoolToTinyInt(mUpdateDatabaseAddNew);

                        var paramInfoOnly = cmd.Parameters.Add(new SqlParameter("infoOnly", SqlDbType.TinyInt));
                        paramInfoOnly.Value = BoolToTinyInt(simulateCall);

                        if (simulateCall)
                        {
                            var reader = cmd.ExecuteReader();

                            var fieldCount = reader.HasRows ? reader.FieldCount : 0;

                            if (fieldCount < 1)
                            {
                                Console.WriteLine("Warning: " + UPDATE_HOST_STATUS_PROCEDURE + " returned no data; cannot preview results");
                                return;
                            }

                            var fieldNames = new List<string>
                            {
                                "Host",
                                "IP",
                                "Warning",
                                "Last_Online",
                                "New_Last_Online",
                                "Last_IP"
                            };

                            var fieldMapping = GetFieldMapping(reader, fieldNames);

                            if (fieldMapping["Host"] < 0 || fieldMapping["Last_Online"] < 0)
                            {
                                Console.WriteLine("Warning: " + UPDATE_HOST_STATUS_PROCEDURE + " did not return the expected field names; cannot preview results");
                                Console.WriteLine("Expected names " + string.Join(", ", fieldNames));
                                return;
                            }

                            const string COLUMN_FORMAT_SPEC = "{0,-20} {1,-20} {2,-20} {3,-40}";

                            Console.WriteLine(COLUMN_FORMAT_SPEC, "Host", "Last_Online", "New_Last_Online", "Warning");

                            Console.WriteLine(COLUMN_FORMAT_SPEC, "-------------------", "-------------------", "-------------------", "----------");

                            while (reader.Read())
                            {
                                Console.WriteLine(COLUMN_FORMAT_SPEC,
                                    GetColumnValue(reader, fieldMapping["Host"], 20),
                                    GetColumnValue(reader, fieldMapping["Last_Online"], 20, true),
                                    GetColumnValue(reader, fieldMapping["New_Last_Online"], 20, true),
                                    GetColumnValue(reader, fieldMapping["Warning"], 40));
                            }

                            Console.WriteLine();
                            Console.WriteLine("Previewed update of " + activeHosts.Count + " hosts");
                        }
                        else
                        {
                            cmd.ExecuteNonQuery();
                            Console.WriteLine("Update complete for " + activeHosts.Count + " hosts");
                        }
                    }

                }
                Console.WriteLine();

            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error in UpdateHostStatus", ex);
            }
        }

        private static string GetAppVersion()
        {
            return PRISM.FileProcessor.ProcessFilesOrFoldersBase.GetAppVersion(PROGRAM_DATE);
        }

        /// <summary>
        /// Configure the options using command line arguments
        /// </summary>
        /// <param name="commandLineParser"></param>
        /// <returns>True if no problems, false if an error</returns>
        private static bool SetOptionsUsingCommandLineParameters(clsParseCommandLine commandLineParser)
        {
            var lstValidParameters = new List<string> { "File", "Manual", "DB", "DBAdd", "Simulate" };

            try
            {
                // Make sure no invalid parameters are present
                if (commandLineParser.InvalidParametersPresent(lstValidParameters))
                {
                    var badArguments = new List<string>();
                    foreach (var item in commandLineParser.InvalidParameters(lstValidParameters))
                    {
                        badArguments.Add("/" + item);
                    }

                    ShowErrors("Invalid command line parameters", badArguments);

                    return false;
                }

                // Query commandLineParser to see if various parameters are present

                if (commandLineParser.IsParameterPresent("File"))
                {
                    commandLineParser.RetrieveValueForParameter("File", out mHostNameFile);
                }

                if (commandLineParser.IsParameterPresent("Manual"))
                {
                    commandLineParser.RetrieveValueForParameter("Manual", out mHostOverrideList);
                }

                if (commandLineParser.IsParameterPresent("DB"))
                {
                    mUpdateDatabase = true;
                }

                if (commandLineParser.IsParameterPresent("DBAdd"))
                {
                    mUpdateDatabaseAddNew = true;
                }

                if (commandLineParser.IsParameterPresent("Simulate"))
                {
                    mSimulatePing = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error parsing the command line parameters", ex);
            }

            return false;
        }

        private static void ShowErrorMessage(string message, Exception ex = null)
        {
            ConsoleMsgUtils.ShowError(message, ex);
        }

        private static void ShowErrors(string title, IEnumerable<string> errorMessages)
        {
            ConsoleMsgUtils.ShowErrors(title, errorMessages);
        }


        private static void ShowProgramHelp()
        {
            var exeName = Path.GetFileName(PRISM.FileProcessor.ProcessFilesOrFoldersBase.GetAppPath());
            try
            {
                Console.WriteLine();
                Console.WriteLine("This program contacts DMS to retrieve a list of Bionet computers (hosts)");
                Console.WriteLine("It pings each computer to see which respond, then optionally contacts DMS with the list of active hosts");
                Console.WriteLine();

                Console.Write("Program syntax:" + Environment.NewLine + exeName);
                Console.WriteLine(" [/Manual:Host1,Host2,Host3] [/File:HostListFile] [/Simulate] [/DB] [/DBAdd]");

                Console.WriteLine();
                Console.WriteLine("By default contacts DMS to retrieve the list of bionet hosts, then pings each one (appending suffix .bionet)");
                Console.WriteLine("Alternatively, use /Manual to define a list of hosts to contact (.bionet is not auto-appended)");
                Console.WriteLine("Or use /File to specify a text file listing one host per line (.bionet is not auto-appended)");
                Console.WriteLine();
                Console.WriteLine("Use /Simulate to simulate the ping");
                Console.WriteLine();
                Console.WriteLine("Use /DB to store the results in the database (preview if /Simulate is used)");
                Console.WriteLine("Use /DBAdd to add new (unknown) hosts to the database");
                Console.WriteLine();
                Console.WriteLine("Program written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) in 2015");
                Console.WriteLine("Version: " + GetAppVersion());
                Console.WriteLine();

                Console.WriteLine("E-mail: matthew.monroe@pnnl.gov or matt@alchemistmatt.com");
                Console.WriteLine("Website: http://panomics.pnnl.gov/ or http://omics.pnl.gov or http://www.sysbio.org/resources/staff/");
                Console.WriteLine();

                // Delay for 750 msec in case the user double clicked this file from within Windows Explorer (or started the program via a shortcut)
                System.Threading.Thread.Sleep(750);

            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error displaying the program syntax", ex);
            }

        }

    }
}
