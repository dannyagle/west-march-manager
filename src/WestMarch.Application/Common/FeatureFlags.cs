namespace WestMarch.Application.Common;

/// <summary>Bound from configuration section "Features".</summary>
public class FeatureFlags
{
    public const string SectionName = "Features";

    /// <summary>
    /// Enables the best-effort D&D Beyond stat-header adapter on the DM session view.
    /// The core app never depends on it; when off (or failing) the UI degrades to the DDB link.
    /// </summary>
    public bool DdbStatHeaders { get; set; }
}
