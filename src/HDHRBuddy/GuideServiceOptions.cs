namespace HDHRBuddy
{
    public class GuideServiceOptions
    {
        /// <summary>
        /// HDHomeRun tuner IPs, parsed from the comma-separated TUNER_ADDRESSES variable.
        /// </summary>
        public List<string> TunerAddresses { get; set; } = [];

        /// <summary>
        /// Minimum randomized delay, in hours, before the next download after a run completes.
        /// </summary>
        public double MinDelayHours { get; set; } = 20;

        /// <summary>
        /// Maximum randomized delay, in hours, before the next download after a run completes.
        /// </summary>
        public double MaxDelayHours { get; set; } = 28;

        /// <summary>
        /// Directory used to store cache files.
        /// </summary>
        public string ConfigDirectory { get; set; } = "/config";

        /// <summary>
        /// Directory used to persist the last downloaded guide.
        /// </summary>
        public string DataDirectory { get; set; } = "/data";

        public static GuideServiceOptions FromConfiguration(IConfiguration configuration)
        {
            var options = new GuideServiceOptions
            {
                MinDelayHours = configuration.GetValue("MIN_DELAY_HOURS", 20.0),
                MaxDelayHours = configuration.GetValue("MAX_DELAY_HOURS", 28.0),
                ConfigDirectory = configuration["CONFIG_DIR"] ?? "/config",
                DataDirectory = configuration["DATA_DIR"] ?? "/data"
            };

            var tunerAddresses = configuration["TUNER_ADDRESSES"];

            if (!string.IsNullOrWhiteSpace(tunerAddresses))
            {
                options.TunerAddresses =
                [
                    .. tunerAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ];
            }

            return options;
        }
    }
}