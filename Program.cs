using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Data.SqlClient;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FileProcessor;

namespace BionetPingTool
{
    class Program
    {
        private const string PROGRAM_DATE = "December 3, 2015";

        private const string DMS_CONNECTION_STRING = "Data Source=gigasax;Initial Catalog=DMS5;Integrated Security=SSPI;";
        private const string UPDATE_HOST_STATUS_PROCEDURE = "UpdateBionetHostStatusFromList";
        private const int PING_TIMEOUT_SECONDS = 5;

        private static string mHostOverrideList;
        private static bool mSimulatePing;
        private static bool mUpdateDatabase;

        static int Main()
        {
            var objParseCommandLine = new clsParseCommandLine();

            mHostOverrideList = string.Empty;
            mSimulatePing = false;
            mUpdateDatabase = false;
            
            try
            {
                if (objParseCommandLine.ParseCommandLine())
                {
                    SetOptionsUsingCommandLineParameters(objParseCommandLine);
                }

                if (objParseCommandLine.NeedToShowHelp)
                {
                    ShowProgramHelp();
                    return -1;

                }

                PingBionetComputers(mSimulatePing, mUpdateDatabase, mHostOverrideList);

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error occurred in Program->Main: " + Environment.NewLine + ex.Message);
                Console.WriteLine(ex.StackTrace);
                return -1;
            }

            return 0;
        }

        private static void PingBionetComputers(bool simulatePing, bool updateDatabase, string explicitHostList)
        {
            List<string> hostNames;

            if (string.IsNullOrWhiteSpace(explicitHostList))
                hostNames = new List<string>();
            else
            {
                hostNames= explicitHostList.Split(',').ToList();
            }

            PingBionetComputers(simulatePing, updateDatabase, hostNames);
        }

        /// <summary>
        /// Ping the bionet computers tracked by DMS
        /// <param name="simulatePing">True to simulate the ping, false to actually ping each computer</param>
        /// <param name="updateDatabase">True to call stored procedure UpdateBionetHostStatusFromList for hosts that are successfully contacted</param>
        /// <param name="explicitHostList">Optional list of host names to use instead of contacting DMS</param>
        /// </summary>
        private static void PingBionetComputers(
            bool simulatePing, 
            bool updateDatabase, 
            ICollection<string> explicitHostList)
        {
            try
            {

                // Query DMS for the host names
                IEnumerable<string> hostNames;
                var assureBionetSuffix = false;

                if (explicitHostList != null && explicitHostList.Count > 0)
                    hostNames = explicitHostList;
                else
                {
                    hostNames = GetBionetHosts();
                    assureBionetSuffix = true;
                }

                // Ping the Hosts
                var activeHosts = PingHostList(hostNames, simulatePing, assureBionetSuffix);

                if (updateDatabase)
                {
                    // Update the status for hosts that responded
                    UpdateHostStatus(activeHosts);
                }

            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error in PingBionetComputers: " + ex.Message);
            }

        }

        /// <summary>
        /// Ping the computers in hostNames
        /// </summary>
        /// <param name="hostNames">List of hosts to ping</param>
        /// <param name="simulatePing">True to simulate the ping, false to actually ping each computer</param>
        /// <param name="assureBionetSuffix">True when testing bionet computers; will make sure each name ends with ".bionet"</param>
        /// <returns>List of hosts that were successfully contacted (empty list if simulatePing is true)</returns>
        private static List<string> PingHostList(
            IEnumerable<string> hostNames, 
            bool simulatePing, 
            bool assureBionetSuffix)
        {
            try
            {

                if (simulatePing)
                    Console.WriteLine("Simulating ping");
                else
                    Console.WriteLine("Contacting computers");

                var activeHosts = new List<string>();

                Parallel.ForEach(hostNames, hostName =>
                {
                    var result = PingHost(hostName, simulatePing, assureBionetSuffix);

                    if (result && !simulatePing)
                        activeHosts.Add(hostName);
                });

                Console.WriteLine();

                return activeHosts;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error in PingHostList: " + ex.Message);
                return new List<string>();
            }

        }

        /// <summary>
        /// Ping hostName
        /// </summary>
        /// <param name="hostName">Computer name</param>
        /// <param name="simulatePing">True to simulate the ping, false to actually ping each computer</param>
        /// <param name="assureBionetSuffix">True when testing bionet computers; will make sure each name ends with ".bionet"</param>
        /// <returns>True if the host responds (returns false if simulatePing is true)</returns>
        private static bool PingHost(
            string hostName, 
            bool simulatePing, 
            bool assureBionetSuffix)
        {
            var hostNameWithSuffix = hostName.Trim();

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
                    Console.WriteLine("Reply for {0,-28} from {1,-15}: bytes={2} time {3}ms TTL={4}", hostNameWithSuffix, reply.Address, bufferSize, reply.RoundtripTime, reply.Options.Ttl);
                    return true;
                }

                Console.WriteLine("Host timed out: {0} ", hostNameWithSuffix);

            }
            catch (Exception ex)
            {
                var socketEx = ex.InnerException as SocketException;

                if (socketEx != null)
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
                    if (ex.InnerException != null)
                        ShowErrorMessage("Error in PingHost for " + hostNameWithSuffix + ": " + ex.InnerException.Message);
                    else
                        ShowErrorMessage("Error in PingHost for " + hostNameWithSuffix + ": " + ex.Message);
                }
            }

            return false;
        }

        /// <summary>
        /// Update DMS with the list of bionet hosts that responded to a ping
        /// </summary>
        /// <param name="activeHosts"></param>
        private static void UpdateHostStatus(ICollection<string> activeHosts)
        {
            try
            {

                Console.WriteLine("Updating DMS");

                using (var cn = new SqlConnection(DMS_CONNECTION_STRING))
                {
                    cn.Open();

                    // Procedure is UpdateBionetHostStatusFromList    
                    using (var cmd = new SqlCommand(UPDATE_HOST_STATUS_PROCEDURE, cn))
                    {
                        cmd.CommandTimeout = 30;
                        cmd.CommandType = CommandType.StoredProcedure;

                        var paramHostNames = cmd.Parameters.Add(new SqlParameter("hostNames", SqlDbType.VarChar, 8000));
                        paramHostNames.Value = string.Join(",", activeHosts);

                        var paramAddMissing = cmd.Parameters.Add(new SqlParameter("addMissingHosts", SqlDbType.TinyInt));
                        paramAddMissing.Value = 0;

                        var paramInfoOnly = cmd.Parameters.Add(new SqlParameter("infoOnly", SqlDbType.TinyInt));
                        paramInfoOnly.Value = 0;

                        cmd.ExecuteNonQuery();

                        Console.WriteLine("Update complete for " + activeHosts.Count + " hosts");
                    }

                }
                Console.WriteLine();

            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error in UpdateHostStatus: " + ex.Message);
            }
        }


        /// <summary>
        /// Contact DMS to get the bionet host names
        /// </summary>
        /// <returns>List of host names</returns>
        private static IEnumerable<string> GetBionetHosts()
        {

            try
            {
                var hostNames = new List<string>();

                Console.WriteLine("Retrieving names of Bionet computers at " + DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt"));

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
                ShowErrorMessage("Error in GetBionetHosts: " + ex.Message);
                return new List<string>();
            }
        }

        private static string GetAppVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + " (" + PROGRAM_DATE + ")";
        }

        private static bool SetOptionsUsingCommandLineParameters(clsParseCommandLine objParseCommandLine)
        {
            // Returns True if no problems; otherwise, returns false
            var lstValidParameters = new List<string> { "Manual", "DB", "Simulate"};

            try
            {
                // Make sure no invalid parameters are present
                if (objParseCommandLine.InvalidParametersPresent(lstValidParameters))
                {
                    var badArguments = new List<string>();
                    foreach (var item in objParseCommandLine.InvalidParameters(lstValidParameters))
                    {
                        badArguments.Add("/" + item);
                    }

                    ShowErrorMessage("Invalid commmand line parameters", badArguments);

                    return false;
                }

                // Query objParseCommandLine to see if various parameters are present						
                if (objParseCommandLine.IsParameterPresent("Manual"))
                {
                    objParseCommandLine.RetrieveValueForParameter("Manual", out mHostOverrideList);
                }

                if (objParseCommandLine.IsParameterPresent("DB"))
                {
                    mUpdateDatabase = true;
                }

                if (objParseCommandLine.IsParameterPresent("Simulate"))
                {
                    mSimulatePing = true;
                    mUpdateDatabase = false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error parsing the command line parameters: " + Environment.NewLine + ex.Message);
            }

            return false;
        }

        private static void ShowErrorMessage(string strMessage, bool writeToErrorStream = false)
        {
            const string strSeparator = "------------------------------------------------------------------------------";

            Console.WriteLine();
            Console.WriteLine(strSeparator);
            Console.WriteLine(strMessage);
            Console.WriteLine(strSeparator);
            Console.WriteLine();

            if (writeToErrorStream)
                WriteToErrorStream(strMessage);
        }

        private static void ShowErrorMessage(string strTitle, IEnumerable<string> items)
        {
            const string strSeparator = "------------------------------------------------------------------------------";

            Console.WriteLine();
            Console.WriteLine(strSeparator);
            Console.WriteLine(strTitle);
            var strMessage = strTitle + ":";

            foreach (var item in items)
            {
                Console.WriteLine("   " + item);
                strMessage += " " + item;
            }
            Console.WriteLine(strSeparator);
            Console.WriteLine();

            WriteToErrorStream(strMessage);
        }


        private static void ShowProgramHelp()
        {
            var exeName = Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            try
            {
                Console.WriteLine();
                Console.WriteLine("This program contacts DMS to retrieve a list of Bionet computers (hosts)");
                Console.WriteLine("It pings each computer to see which respond, then optionally contacts DMS with the list of active hosts");
                Console.WriteLine();

                Console.Write("Program syntax:" + Environment.NewLine + exeName);
                Console.WriteLine(" [/Manual:Host1,Host2,Host3] [/Simulate] [/DB]");

                Console.WriteLine();
                Console.WriteLine("By default contacts DMS to retrieve the list of bionet hosts, then pings each one (appending suffix .bionet)");
                Console.WriteLine("Alternatively, use /Manual to define a list of hosts to contact");
                Console.WriteLine();
                Console.WriteLine("Use /Simulate to simulate the ping");
                Console.WriteLine();
                Console.WriteLine("Use /DB to store the results in the database (ignored if /Simulate is used)");
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
                Console.WriteLine("Error displaying the program syntax: " + ex.Message);
            }

        }

        private static void WriteToErrorStream(string strErrorMessage)
        {
            try
            {
                using (var swErrorStream = new StreamWriter(Console.OpenStandardError()))
                {
                    swErrorStream.WriteLine(strErrorMessage);
                }
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch
            {
                // Ignore errors here
            }
        }

    }
}
