namespace HDHRBuddy.Models;

public class GuideState
{
    /// <summary>
    /// UTC time of the next scheduled download.
    /// </summary>
    public DateTime? NextRunUtc { get; set; }

    /// <summary>
    /// UTC time of the last download attempt, successful or not.
    /// </summary>
    public DateTime? LastRunUtc { get; set; }

    /// <summary>
    /// UTC time of the last download that completed successfully.
    /// </summary>
    public DateTime? LastSuccessUtc { get; set; }
}