using PRISM;

namespace BionetPingTool
{
    internal class CommandLineOptions
    {
        [Option("File", HelpShowsDefault = false, HelpText = "A text file listing one host per line (.bionet is not auto-appended).")]
        public string HostNameFile { get; set; }

        [Option("Manual", HelpShowsDefault = false, HelpText = "A list of hosts to contact (.bionet is not auto-appended).")]
        public string HostOverrideList { get; set; }

        [Option("HideInactive", HelpShowsDefault = false, HelpText = "If supplied, do not report the names of the skipped hosts")]
        public bool HideInactive { get; set; }

        [Option("Simulate", HelpShowsDefault = false, HelpText = "If supplied, simulate the ping.")]
        public bool SimulatePing { get; set; }

        [Option("DB", HelpShowsDefault = false, HelpText = "If supplied, store the results in the database (preview if /Simulate is used)")]
        public bool UpdateDatabase { get; set; }

        [Option("DBAdd", HelpShowsDefault = false, HelpText = "If supplied, add new (unknown) hosts to the database")]
        public bool UpdateDatabaseAddNew { get; set; }

        [Option("NoDB", HelpShowsDefault = false, HelpText = "If supplied, disable the use of the database, thus requiring that /Manual or /File be used. " +
                                                             "In addition, will not contact DMS to find inactive hosts.")]
        public bool DisableDatabase { get; set; }

        public CommandLineOptions()
        {
            HostNameFile = string.Empty;
            HostOverrideList = string.Empty;

            DisableDatabase = false;
            HideInactive = false;
            SimulatePing = false;
            UpdateDatabase = false;
            UpdateDatabaseAddNew = false;
        }
    }
}
